using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Framework;
using Settlement_Services.Framework.Compat;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Pricing;
using Settlement_Services.Framework.Specialty;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Hospitality
{
    public class LodgingServiceWorker : SettlementServiceWorker
    {
        private const string DrugStockCategory = "SettlementStock_Drugs";
        private const string SubstanceDrugGroupKey = "SettlementServices.Label.SubstanceChoice";
        private const float TavernSocialXp = 600f;
        private const float FullRestoreAmount = 100f;

        private static readonly string[] BaseRestoredNeedDefNames = { "Food", "Rest", "Comfort" };
        private static readonly PawnRelationDef[] PartnerRelations = { PawnRelationDefOf.Spouse, PawnRelationDefOf.Lover, PawnRelationDefOf.Fiance };

        public override bool UsesPerTargetOptionSelections => true;

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            foreach (HospitalityPackageDef pkg in HospitalityPackageRegistry.AllValid)
            {
                if (!PackageEligible(pkg, ctx)) continue;
                yield return new ServiceDisplayOption { key = pkg.defName, label = pkg.LabelCap, description = pkg.description, isOptional = true };
            }

            if (ctx.SelectedOptionKeys.Contains(HospitalityPackageDefOf.HospitalityPackage_Substance.defName))
            {
                foreach (ThingDef drugDef in SubstanceEligibilityService.EligibleDrugs(ctx.Settlement))
                    yield return new ServiceDisplayOption { key = drugDef.defName, label = drugDef.LabelCap, groupKey = SubstanceDrugGroupKey, allowMultipleSelectionInGroup = false };
            }
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) =>
            BuildQuoteLineItems(request, new ServiceBatchAllocationContext());

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request, ServiceBatchAllocationContext batchContext)
        {
            var lineItems = new List<ServiceLineItem>();

            ServiceInputPlan foodPlan = batchContext.GetOrCreateInputPlan(request, () => HospitalityInputPlanning.PlanFood(request, ResolveNights(request), batchContext.StockLedger));
            int foodCost = HospitalityInputPlanning.PreviewCost(foodPlan);
            if (foodCost > 0) lineItems.Add(new ServiceLineItem("SettlementServices.LineItem.LodgingFood", foodCost));

            float specialtyPriceModifierPct = request.settlement != null
                ? SettlementSpecialtyService.TotalPriceModifierPct(request.settlement, def.category)
                : 0f;

            foreach (string key in request.selectedOptionKeys)
            {
                HospitalityPackageDef pkg = DefDatabase<HospitalityPackageDef>.GetNamedSilentFail(key);
                if (pkg == null || !HospitalityPackageRegistry.AllValid.Contains(pkg)) continue;

                int cost = HospitalityPackagePricing.ScaledCost(pkg, ServicePricingContext.Current, def.difficultyScaling, specialtyPriceModifierPct);
                lineItems.Add(new ServiceLineItem("SettlementServices.LineItem.HospitalityPackage", cost, labelArgument: pkg.LabelCap));
            }

            string drugDefName = SelectedDrugDefName(request);
            if (drugDefName != null)
            {
                ThingDef drugDef = DefDatabase<ThingDef>.GetNamedSilentFail(drugDefName);
                if (drugDef != null && AllocateDrug(request, drugDefName).Success)
                    lineItems.Add(new ServiceLineItem("SettlementServices.LineItem.HospitalitySubstance", Mathf.RoundToInt(drugDef.BaseMarketValue), labelArgument: drugDef.LabelCap));
            }

            return lineItems;
        }

        public override List<ServiceStockRequirement> GetDynamicStockRequirements(SettlementServiceRequest request)
        {
            var result = new List<ServiceStockRequirement>();

            ServiceStockRequirement foodRequirement = HospitalityInputPlanning.FoodRequirement(request, ResolveNights(request));
            if (foodRequirement != null) result.Add(foodRequirement);

            string drugDefName = SelectedDrugDefName(request);
            if (drugDefName != null)
                result.Add(new ServiceStockRequirement { stockCategoryDefName = DrugStockCategory, thingDefName = drugDefName, amount = 1, playerCanSupply = false });

            return result;
        }

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote) =>
            PlanInputs(request, quote, new ServiceBatchAllocationContext());

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote, ServiceBatchAllocationContext batchContext)
        {
            ServiceInputPlan foodPlan = batchContext.GetOrCreateInputPlan(request, () => HospitalityInputPlanning.PlanFood(request, ResolveNights(request), batchContext.StockLedger));
            var plan = new ServiceInputPlan();
            plan.stockConsumed.AddRange(foodPlan.stockConsumed);
            plan.playerSuppliedConsumed.AddRange(foodPlan.playerSuppliedConsumed);

            string drugDefName = SelectedDrugDefName(request);
            if (drugDefName != null)
            {
                StockAllocationResult allocation = AllocateDrug(request, drugDefName);
                if (allocation.Success)
                {
                    plan.stockConsumed.AddRange(allocation.SettlementSupplied);
                    plan.playerSuppliedConsumed.AddRange(allocation.PlayerSupplied);
                }
            }

            return plan;
        }

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTarget?.liveThing is Pawn pawn)) return ServiceCompletionResult.Ok();

            IReadOnlyList<string> selectedKeys = ctx.SelectedOptionKeys;

            RestoreNeeds(pawn, selectedKeys);
            GrantPackageThoughts(pawn, selectedKeys);
            ApplyPackageHediffs(pawn, selectedKeys);

            if (selectedKeys.Contains(HospitalityPackageDefOf.HospitalityPackage_Adult.defName))
                GrantPartnerStrayedThought(pawn);

            if (selectedKeys.Contains(HospitalityPackageDefOf.HospitalityPackage_Tavern.defName) && pawn.skills != null)
                pawn.skills.Learn(SkillDefOf.Social, TavernSocialXp);

            if (selectedKeys.Contains(HospitalityPackageDefOf.HospitalityPackage_Substance.defName))
                IngestSelectedDrug(pawn, selectedKeys);

            GrantExtendedStayThought(pawn, ctx);

            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        public override float EventChanceMultiplierFor(ServiceJobContext ctx)
        {
            float multiplier = 1f;
            foreach (string key in ctx.SelectedOptionKeys)
            {
                HospitalityPackageDef pkg = DefDatabase<HospitalityPackageDef>.GetNamedSilentFail(key);
                if (pkg != null) multiplier *= pkg.eventChanceMultiplier;
            }
            return multiplier;
        }

        public override int? ExpectedDurationTicksFor(SettlementServiceRequest request)
        {
            if (request.settlement == null) return null;

            int nightCount = Mathf.Max(1, Mathf.RoundToInt(ResolveNights(request)));
            int bookingTick = request.bookingTick >= 0 ? request.bookingTick : Find.TickManager.TicksGame;

            float longitude = Find.WorldGrid.LongLatOf(request.settlement.Tile).x;
            int dayTick = GenDate.DayTick(GenDate.TickGameToAbs(bookingTick), longitude);

            int checkoutTick = 7 * GenDate.TicksPerHour;
            int ticksToFirstCheckout = dayTick < checkoutTick
                ? checkoutTick - dayTick
                : GenDate.TicksPerDay - dayTick + checkoutTick;

            return ticksToFirstCheckout + (nightCount - 1) * GenDate.TicksPerDay;
        }

        private float ResolveNights(SettlementServiceRequest request) =>
            ServicePricingEngine.ResolveTier(def, request.selectedTierKey)?.durationMultiplier ?? 1f;

        private static string SelectedDrugDefName(SettlementServiceRequest request)
        {
            if (!request.selectedOptionKeys.Contains(HospitalityPackageDefOf.HospitalityPackage_Substance.defName)) return null;
            return request.selectedOptionKeys.FirstOrDefault(key => DefDatabase<ThingDef>.GetNamedSilentFail(key) != null);
        }

        private static StockAllocationResult AllocateDrug(SettlementServiceRequest request, string drugDefName)
        {
            var requirement = new ServiceStockRequirement { stockCategoryDefName = DrugStockCategory, thingDefName = drugDefName, amount = 1, playerCanSupply = false };
            return SettlementStockService.TryAllocate(request.settlement, new List<ServiceStockRequirement> { requirement }, request.playerSuppliedInputs);
        }

        private static bool PackageEligible(HospitalityPackageDef pkg, SettlementServiceContext ctx)
        {
            if (!pkg.requiredCapabilityTags.NullOrEmpty() && ctx.Settlement != null
                && !pkg.requiredCapabilityTags.All(tag => SettlementSpecialtyService.HasCapabilityTag(ctx.Settlement, tag)))
                return false;

            if (pkg.requiresAdultPawn && ctx.SelectedTarget is Pawn pawn && !pawn.DevelopmentalStage.Adult()) return false;

            if (ctx.Settlement?.Faction?.def != null && (int)pkg.requiredTechLevel > (int)ctx.Settlement.Faction.def.techLevel) return false;

            return true;
        }

        private static void RestoreNeeds(Pawn pawn, IReadOnlyList<string> selectedKeys)
        {
            if (pawn.needs == null) return;

            var needDefNames = new HashSet<string>(BaseRestoredNeedDefNames);
            foreach (string key in selectedKeys)
            {
                HospitalityPackageDef pkg = DefDatabase<HospitalityPackageDef>.GetNamedSilentFail(key);
                if (pkg?.restoredNeedDefNames != null) needDefNames.UnionWith(pkg.restoredNeedDefNames);
            }

            foreach (string needDefName in needDefNames)
            {
                NeedDef needDef = DefDatabase<NeedDef>.GetNamedSilentFail(needDefName);
                if (needDef != null && pawn.needs.TryGetNeed(needDef, out Need need)) need.CurLevelPercentage = 1f;
            }

            DubsBadHygieneAdapter.TryRestoreHygiene(pawn, FullRestoreAmount);
            DubsBadHygieneAdapter.TryRestoreBladder(pawn, FullRestoreAmount);
        }

        private static void GrantPackageThoughts(Pawn pawn, IReadOnlyList<string> selectedKeys)
        {
            if (pawn.needs?.mood == null) return;

            foreach (string key in selectedKeys)
            {
                HospitalityPackageDef pkg = DefDatabase<HospitalityPackageDef>.GetNamedSilentFail(key);
                if (pkg == null || pkg.thoughtDefName.NullOrEmpty()) continue;

                ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(pkg.thoughtDefName);
                if (thought != null) pawn.needs.mood.thoughts.memories.TryGainMemory(thought);
            }
        }

        private static void ApplyPackageHediffs(Pawn pawn, IReadOnlyList<string> selectedKeys)
        {
            if (pawn.health == null) return;

            foreach (string key in selectedKeys)
            {
                HospitalityPackageDef pkg = DefDatabase<HospitalityPackageDef>.GetNamedSilentFail(key);
                if (pkg == null || pkg.hediffDefName.NullOrEmpty()) continue;

                HediffDef hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(pkg.hediffDefName);
                if (hediffDef == null) continue;

                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
                if (existing != null) pawn.health.RemoveHediff(existing);

                pawn.health.AddHediff(HediffMaker.MakeHediff(hediffDef, pawn));
            }
        }

        private static void GrantPartnerStrayedThought(Pawn pawn)
        {
            if (pawn.relations == null) return;

            foreach (PawnRelationDef relationDef in PartnerRelations)
            {
                Pawn partner = pawn.relations.GetFirstDirectRelationPawn(relationDef);
                if (partner == null || partner.Destroyed || partner.needs?.mood == null) continue;

                ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail("SettlementServiceEvent_AdultRecreation_PartnerStrayed");
                if (thought != null) partner.needs.mood.thoughts.memories.TryGainMemory(thought);
                break;
            }
        }

        private static void IngestSelectedDrug(Pawn pawn, IReadOnlyList<string> selectedKeys)
        {
            string drugDefName = selectedKeys.FirstOrDefault(key => DefDatabase<ThingDef>.GetNamedSilentFail(key) != null);
            ThingDef drugDef = drugDefName != null ? DefDatabase<ThingDef>.GetNamedSilentFail(drugDefName) : null;
            if (drugDef == null) return;

            Thing drug = ThingMaker.MakeThing(drugDef);
            drug.Ingested(pawn, 0f);
        }

        private static void GrantExtendedStayThought(Pawn pawn, ServiceJobContext ctx)
        {
            if (pawn.needs?.mood == null) return;

            string thoughtDefName;
            switch (ctx.Job.acceptedQuote?.selectedTierKey)
            {
                case "ThreeNights":
                    thoughtDefName = "SettlementServiceEvent_Hospitality_ExtendedStay_ThreeNights";
                    break;
                case "OneWeek":
                    thoughtDefName = "SettlementServiceEvent_Hospitality_ExtendedStay_OneWeek";
                    break;
                default:
                    return;
            }

            ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(thoughtDefName);
            if (thought != null) pawn.needs.mood.thoughts.memories.TryGainMemory(thought);
        }
    }
}

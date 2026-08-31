using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Medical
{
    public class SurgeryServiceWorker : SettlementServiceWorker
    {
        private const string ImplantGroupKey = "SettlementServices.Label.ImplantChoice";
        private const int MedicineAmount = 2;
        private const int OperationDurationTicks = 2 * GenDate.TicksPerHour;
        private const float BaseComplicationChance = 0.2f;
        private const float ComplicationChanceQualityWeight = 0.25f;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) return ServiceAvailabilityReport.Available;
            return SurgeryOptionService.FindOfferedOptions(pawn, ctx.Settlement, ctx.RequestingCaravan).Any()
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoCompatibleImplants");
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) yield break;
            List<SurgeryOptionService.ImplantOption> allOptions = SurgeryOptionService.FindOfferedOptions(pawn, ctx.Settlement, ctx.RequestingCaravan);
            foreach (SurgeryOptionService.ImplantOption option in allOptions)
                yield return new ServiceDisplayOption
                {
                    key = option.Key,
                    label = option.Label,
                    groupKey = ImplantGroupKey,
                    allowMultipleSelectionInGroup = true,
                    conflictingOptionKeys = SurgeryOptionService.ConflictingKeysFor(option, allOptions),
                };
        }

        public override IEnumerable<string> GetDisplaySummaryLines(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) yield break;
            if (!SurgeryOptionService.TryResolveSelected(pawn, ctx.SelectedOptionKeys, out List<SurgeryOptionService.ImplantOption> resolved, out _)) yield break;
            if (resolved.Count == 0) yield break;

            yield return "SettlementServices.Label.SurgeryPlayerSuppliedExplanation".Translate();
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request)
        {
            var lineItems = new List<ServiceLineItem>();
            if (!(request.target.thing is Pawn pawn)) return lineItems;

            List<SurgeryOptionService.ImplantOption> resolved = SurgeryOptionService.ResolveAvailable(pawn, request.selectedOptionKeys);
            if (resolved.Count == 0) return lineItems;

            List<ThingDefCountClass> remainingPlayerSupplied = CloneCounts(request.playerSuppliedInputs);

            foreach (SurgeryOptionService.ImplantOption option in resolved)
            {
                ThingDefCountClass playerEntry = remainingPlayerSupplied.Find(c => c.thingDef == option.stockThingDef);
                if (playerEntry != null && playerEntry.count > 0)
                {
                    playerEntry.count--;
                    continue;
                }

                lineItems.Add(new ServiceLineItem("SettlementServices.LineItem.SurgeryPart", Mathf.RoundToInt(option.stockThingDef.BaseMarketValue), labelArgument: option.Label));
            }

            return lineItems;
        }

        public override List<ServiceStockRequirement> GetDynamicStockRequirements(SettlementServiceRequest request)
        {
            var result = new List<ServiceStockRequirement>();
            if (!(request.target.thing is Pawn pawn)) return result;

            List<SurgeryOptionService.ImplantOption> resolved = SurgeryOptionService.ResolveAvailable(pawn, request.selectedOptionKeys);
            foreach (SurgeryOptionService.ImplantOption option in resolved)
                result.Add(new ServiceStockRequirement { thingDefName = option.stockThingDef.defName, amount = 1, playerCanSupply = true });
            return result;
        }

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote)
        {
            if (!(request.target.thing is Pawn pawn)) return new ServiceInputPlan();

            List<SurgeryOptionService.ImplantOption> resolved = SurgeryOptionService.ResolveAvailable(pawn, request.selectedOptionKeys);

            var requirements = new List<ServiceStockRequirement>
            {
                new ServiceStockRequirement { stockCategoryDefName = "SettlementStock_Medicine", preferredThingDefName = "MedicineIndustrial", amount = MedicineAmount, playerCanSupply = true },
            };
            foreach (SurgeryOptionService.ImplantOption option in resolved)
                requirements.Add(new ServiceStockRequirement { thingDefName = option.stockThingDef.defName, amount = 1, playerCanSupply = true });

            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, requirements, request.playerSuppliedInputs);
            if (!allocation.Success) return new ServiceInputPlan();

            return new ServiceInputPlan { stockConsumed = allocation.SettlementSupplied, playerSuppliedConsumed = allocation.PlayerSupplied };
        }

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            if (!(request.target.thing is Pawn pawn)) return null;
            if (request.selectedOptionKeys.Count == 0) return null;
            return SurgeryOptionService.TryResolveSelected(pawn, request.selectedOptionKeys, out _, out string errorKey) ? null : errorKey;
        }

        public override int? BaseDurationTicksFor(SettlementServiceRequest request)
        {
            if (!(request.target.thing is Pawn pawn)) return null;
            if (!SurgeryOptionService.TryResolveSelected(pawn, request.selectedOptionKeys, out List<SurgeryOptionService.ImplantOption> resolved, out _)) return null;
            if (resolved.Count == 0) return null;
            return def.duration + resolved.Count * OperationDurationTicks;
        }

        public override ServiceStartResult Start(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTargetThing is Pawn pawn)) return ServiceStartResult.Ok;
            return SurgeryOptionService.TryResolveSelected(pawn, ctx.SelectedOptionKeys, out _, out string errorKey)
                ? ServiceStartResult.Ok
                : ServiceStartResult.Fail(errorKey);
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTarget?.liveThing is Pawn pawn)) return ServiceCompletionResult.Ok();

            IReadOnlyList<string> keys = ctx.SelectedOptionKeys;
            if (keys.Count == 0) return ServiceCompletionResult.Ok();

            Settlement settlement = ctx.ResolveSettlement();
            foreach (string key in keys)
            {
                SurgeryOptionService.ImplantOption? option = SurgeryOptionService.FindByKey(pawn, key);
                if (option == null) return ServiceCompletionResult.Fail("SettlementServices.Error.NoCompatibleImplants");
                PerformSurgery(pawn, option.Value, settlement, def.category);
            }

            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static List<ThingDefCountClass> CloneCounts(List<ThingDefCountClass> source) =>
            source?.Select(c => new ThingDefCountClass(c.thingDef, c.count)).ToList() ?? new List<ThingDefCountClass>();

        private static void PerformSurgery(Pawn pawn, SurgeryOptionService.ImplantOption option, Settlement settlement, ServiceCategoryDef category)
        {
            option.recipe.Worker.ApplyOnPawn(pawn, option.part, null, new List<Thing>(), null);

            float quality = MedicalQualityService.TreatmentQuality(settlement, category);
            float complicationChance = Mathf.Clamp01(BaseComplicationChance - quality * ComplicationChanceQualityWeight);
            if (!Rand.Chance(complicationChance)) return;

            HediffDef complication = DefDatabase<HediffDef>.GetNamedSilentFail("SettlementService_SurgicalComplication");
            if (complication != null) pawn.health.AddHediff(complication, option.part);
        }
    }
}

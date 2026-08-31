using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Crafting
{
    public class CraftingCommissionServiceWorker : SettlementServiceWorker
    {
        private const float MaterialMarkupPct = 1.4f;
        private const int BaselineSkillLevel = 6;
        private const float SkillLevelPerSurchargePct = 8f;
        private const int SkillLevelsPerEventQualityStep = 4;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx) =>
            CraftingCommissionCatalog.EligibleRecipes(ctx.Settlement).Any()
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoCraftableItemsAvailable");

        public override string ValidateUnitRequest(SettlementServiceRequest request) =>
            TryPlan(request, out _, out string errorKey) ? null : errorKey;

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request)
        {
            var items = new List<ServiceLineItem>();
            if (!TryPlan(request, out CraftingProductionPlan plan, out _)) return items;

            int materialsCost = SettlementSuppliedMaterialsCost(request, plan);
            if (materialsCost > 0) items.Add(new ServiceLineItem("SettlementServices.LineItem.CraftingMaterials", materialsCost));
            return items;
        }

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote)
        {
            if (!TryPlan(request, out CraftingProductionPlan plan, out _)) return ServiceInputPlan.None;

            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, CraftingStockRequirements.ToStockRequirements(plan), request.playerSuppliedInputs);
            if (!allocation.Success) return ServiceInputPlan.None;

            return new ServiceInputPlan { stockConsumed = allocation.SettlementSupplied, playerSuppliedConsumed = allocation.PlayerSupplied };
        }

        public override List<ServiceStockRequirement> GetDynamicStockRequirements(SettlementServiceRequest request)
        {
            if (CraftingProductionPlanner.TryPlan(request.settlement, request.craftingCommissionLines, request.playerSuppliedInputs, request.craftingCrafterCount, out CraftingProductionPlan plan, out _, out CraftingMaterialRequirement blocking))
                return CraftingStockRequirements.ToStockRequirements(plan);

            if (blocking == null) return new List<ServiceStockRequirement>();

            return new List<ServiceStockRequirement> { CraftingStockRequirements.ToBlockingRequirement(blocking, request.playerSuppliedInputs) };
        }

        public override float BasePriceMultiplierFor(SettlementServiceRequest request) =>
            TryPlan(request, out CraftingProductionPlan plan, out _) ? Mathf.Max(1, plan.billedCrafterDays) : 1f;

        public override int? BaseDurationTicksFor(SettlementServiceRequest request) =>
            TryPlan(request, out CraftingProductionPlan plan, out _) ? plan.longestCrafterDurationTicks : (int?)null;

        public override object BuildAcceptedData(SettlementServiceRequest request, SettlementServiceQuote quote) =>
            TryPlan(request, out CraftingProductionPlan plan, out _) ? plan.Clone() : null;

        public override ServiceStartResult Start(ServiceJobContext ctx) =>
            CraftingProductionPlanValidator.Validate(ctx.Job.craftingProductionPlan, out string errorKey) ? ServiceStartResult.Ok : ServiceStartResult.Fail(errorKey);

        public override ServiceTickResult Tick(ServiceJobContext ctx, int ticksSinceLastCall) =>
            CraftingProductionPlanValidator.Validate(ctx.Job.craftingProductionPlan, out string errorKey) ? ServiceTickResult.NoChange : ServiceTickResult.Failed(errorKey);

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            CraftingProductionPlan plan = ctx.Job.craftingProductionPlan;
            if (!CraftingProductionPlanValidator.Validate(plan, out string errorKey)) return ServiceCompletionResult.Fail(errorKey);

            Settlement settlement = ctx.ResolveSettlement();
            TechLevel techLevel = settlement?.Faction?.def.techLevel ?? TechLevel.Industrial;
            int effectiveSkillLevel = EffectiveSkillLevel(ctx, settlement);

            var resultThings = new List<Thing>();
            foreach (CraftingProductionOperation op in plan.operations)
            {
                if (!op.producesFinalResult) continue;

                RecipeNode node = CraftingRecipeDependencyIndex.NodeFor(op.Identity);
                CraftingCommissionRecipe recipe = node.recipe;

                ThingDef stuff = null;
                if (recipe.Kind == CraftingRecipeKind.Vanilla && recipe.madeFromStuff)
                {
                    stuff = (op.stuffDefName != null ? DefDatabase<ThingDef>.GetNamedSilentFail(op.stuffDefName) : null)
                        ?? GenStuff.RandomStuffByCommonalityFor(recipe.primaryOutput, techLevel);
                }

                resultThings.AddRange(recipe.MakeProducts(op.executions, stuff, effectiveSkillLevel));
            }

            return ServiceCompletionResult.Ok(resultThings: resultThings);
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        public override IEnumerable<string> GetDisplaySummaryLines(SettlementServiceContext ctx)
        {
            yield return "SettlementServices.Label.EffectiveCrafterSkill".Translate(DraftEffectiveSkillLevel(ctx.Settlement, ctx.SelectedTierKey));
        }

        private int DraftEffectiveSkillLevel(Settlement settlement, string selectedTierKey)
        {
            float qualityAboveBaseline = CraftingQualityService.Quality(settlement, def.category) - 0.6f;
            float tierSurcharge = def.priorityTiers?.Find(t => t.key == selectedTierKey)?.costSurchargePct ?? 0f;
            int skillLevel = BaselineSkillLevel + Mathf.RoundToInt(qualityAboveBaseline * 20f) + Mathf.RoundToInt(tierSurcharge * SkillLevelPerSurchargePct);
            return Mathf.Clamp(skillLevel, 0, 20);
        }

        private int EffectiveSkillLevel(ServiceJobContext ctx, Settlement settlement)
        {
            int baseline = DraftEffectiveSkillLevel(settlement, ctx.Job.acceptedQuote?.selectedTierKey);
            int eventQualityOffset = ResolveAppliedEventQualityOffset(ctx.Job);
            return Mathf.Clamp(baseline + eventQualityOffset * SkillLevelsPerEventQualityStep, 0, 20);
        }

        private static int ResolveAppliedEventQualityOffset(ServiceJobRecord job)
        {
            if (job.eventOutcome == null || !job.eventOutcome.applied || job.eventOutcome.eventDefName == null) return 0;

            ServiceEventDef eventDef = DefDatabase<ServiceEventDef>.GetNamedSilentFail(job.eventOutcome.eventDefName);
            ServiceEventEffects effects = job.eventOutcome.choiceIndexSelected >= 0 && eventDef?.choices != null
                ? eventDef.choices.ElementAtOrDefault(job.eventOutcome.choiceIndexSelected)?.effects
                : eventDef?.effects;
            return effects?.qualityOffset ?? 0;
        }

        private static int SettlementSuppliedMaterialsCost(SettlementServiceRequest request, CraftingProductionPlan plan)
        {
            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, CraftingStockRequirements.ToStockRequirements(plan), request.playerSuppliedInputs);
            if (!allocation.Success) return 0;

            float total = allocation.SettlementSupplied.Sum(c => c.count * c.thingDef.BaseMarketValue * MaterialMarkupPct);
            return Mathf.RoundToInt(total);
        }

        private static bool TryPlan(SettlementServiceRequest request, out CraftingProductionPlan plan, out string errorKey) =>
            CraftingProductionPlanner.TryPlan(request.settlement, request.craftingCommissionLines, request.playerSuppliedInputs, request.craftingCrafterCount, out plan, out errorKey);
    }
}

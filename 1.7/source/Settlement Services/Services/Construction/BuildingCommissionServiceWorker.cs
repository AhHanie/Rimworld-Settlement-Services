using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Events;
using Settlement_Services.Framework.Specialty;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Construction
{
    public class BuildingCommissionServiceWorker : SettlementServiceWorker
    {
        private const float MaterialMarkupPct = 1.4f;
        private const int BaselineSkillLevel = 6;
        private const float SkillLevelPerSurchargePct = 8f;
        private const int SkillLevelsPerEventQualityStep = 4;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx) =>
            BuildingCommissionCatalog.EligibleBuildings(ctx.Settlement).Any()
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoCommissionableBuildingsAvailable");

        public override string ValidateUnitRequest(SettlementServiceRequest request) =>
            TryPlan(request, out _, out string errorKey) ? null : errorKey;

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request)
        {
            var items = new List<ServiceLineItem>();
            if (!TryPlan(request, out BuildingCommissionPlan plan, out _)) return items;

            int materialsCost = SettlementSuppliedMaterialsCost(request, plan);
            if (materialsCost > 0) items.Add(new ServiceLineItem("SettlementServices.LineItem.ConstructionMaterials", materialsCost));
            return items;
        }

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote)
        {
            if (!TryPlan(request, out BuildingCommissionPlan plan, out _)) return ServiceInputPlan.None;

            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, BuildingCommissionPlanner.ToStockRequirements(plan), request.playerSuppliedInputs);
            if (!allocation.Success) return ServiceInputPlan.None;

            return new ServiceInputPlan { stockConsumed = allocation.SettlementSupplied, playerSuppliedConsumed = allocation.PlayerSupplied };
        }

        public override List<ServiceStockRequirement> GetDynamicStockRequirements(SettlementServiceRequest request) =>
            TryPlan(request, out BuildingCommissionPlan plan, out _) ? BuildingCommissionPlanner.ToStockRequirements(plan) : new List<ServiceStockRequirement>();

        public override int? BaseDurationTicksFor(SettlementServiceRequest request) =>
            TryPlan(request, out BuildingCommissionPlan plan, out _) ? plan.workTicks : (int?)null;

        public override object BuildAcceptedData(SettlementServiceRequest request, SettlementServiceQuote quote) =>
            TryPlan(request, out BuildingCommissionPlan plan, out _) ? plan.Clone() : null;

        public override ServiceStartResult Start(ServiceJobContext ctx) =>
            BuildingCommissionPlanValidator.Validate(ctx.Job.buildingCommissionPlan, out string errorKey) ? ServiceStartResult.Ok : ServiceStartResult.Fail(errorKey);

        public override ServiceTickResult Tick(ServiceJobContext ctx, int ticksSinceLastCall) =>
            BuildingCommissionPlanValidator.Validate(ctx.Job.buildingCommissionPlan, out string errorKey) ? ServiceTickResult.NoChange : ServiceTickResult.Failed(errorKey);

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            BuildingCommissionPlan plan = ctx.Job.buildingCommissionPlan;
            if (!BuildingCommissionPlanValidator.Validate(plan, out string errorKey)) return ServiceCompletionResult.Fail(errorKey);

            Settlement settlement = ctx.ResolveSettlement();
            int effectiveSkillLevel = EffectiveConstructorSkill(ctx, settlement);

            var resultThings = new List<Thing>();
            foreach (BuildingCommissionLine line in plan.lines)
            {
                ThingDef building = DefDatabase<ThingDef>.GetNamedSilentFail(line.buildingDefName);
                ThingDef stuff = line.stuffDefName != null ? DefDatabase<ThingDef>.GetNamedSilentFail(line.stuffDefName) : null;

                for (int i = 0; i < line.count; i++)
                {
                    Thing thing = ThingMaker.MakeThing(building, stuff);

                    CompQuality compQuality = thing.TryGetComp<CompQuality>();
                    if (compQuality != null)
                    {
                        QualityCategory quality = QualityUtility.GenerateQualityCreatedByPawn(effectiveSkillLevel, inspired: false);
                        compQuality.SetQuality(quality, ArtGenerationContext.Outsider);
                    }

                    MinifiedThing minified = thing.MakeMinified();
                    if (minified != null) resultThings.Add(minified);
                }
            }

            return ServiceCompletionResult.Ok(resultThings: resultThings);
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        public override IEnumerable<string> GetDisplaySummaryLines(SettlementServiceContext ctx)
        {
            yield return "SettlementServices.Label.EffectiveConstructorSkill".Translate(DraftEffectiveConstructorSkill(ctx.Settlement, ctx.SelectedTierKey));
        }

        private int DraftEffectiveConstructorSkill(Settlement settlement, string selectedTierKey)
        {
            float qualityOffset = settlement != null ? SettlementSpecialtyService.TotalQualityOffset(settlement, def) : 0f;
            float tierSurcharge = def.priorityTiers?.Find(t => t.key == selectedTierKey)?.costSurchargePct ?? 0f;
            int skillLevel = BaselineSkillLevel + Mathf.RoundToInt(qualityOffset * 20f) + Mathf.RoundToInt(tierSurcharge * SkillLevelPerSurchargePct);
            return Mathf.Clamp(skillLevel, 0, 20);
        }

        private int EffectiveConstructorSkill(ServiceJobContext ctx, Settlement settlement)
        {
            int baseline = DraftEffectiveConstructorSkill(settlement, ctx.Job.acceptedQuote?.selectedTierKey);
            int eventQualityOffset = ServiceEventEffectApplier.ResolveAppliedEventQualityOffset(ctx.Job);
            return Mathf.Clamp(baseline + eventQualityOffset * SkillLevelsPerEventQualityStep, 0, 20);
        }

        private static int SettlementSuppliedMaterialsCost(SettlementServiceRequest request, BuildingCommissionPlan plan)
        {
            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, BuildingCommissionPlanner.ToStockRequirements(plan), request.playerSuppliedInputs);
            if (!allocation.Success) return 0;

            float total = allocation.SettlementSupplied.Sum(c => c.count * c.thingDef.BaseMarketValue * MaterialMarkupPct);
            return Mathf.RoundToInt(total);
        }

        private static bool TryPlan(SettlementServiceRequest request, out BuildingCommissionPlan plan, out string errorKey) =>
            BuildingCommissionPlanner.TryPlan(request.settlement, request.buildingCommissionLines, out plan, out errorKey);
    }
}

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Framework;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Genetics
{
    public class BiosculpterTreatmentServiceWorker : SettlementServiceWorker
    {
        private const string CycleGroupKey = "SettlementServices.Label.BiosculpterCycleChoice";

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!BiosculpterCycleService.PodDefExists())
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.RequiresIdeology");
            if (!(ctx.SelectedTarget is Pawn pawn)) return ServiceAvailabilityReport.Available;
            return EligibleCycleKeys(pawn).Any()
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoBiosculpterCycleAvailable");
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) yield break;
            foreach (string key in EligibleCycleKeys(pawn))
            {
                CompProperties_BiosculpterPod_BaseCycle props = BiosculpterCycleService.ResolveCycleProps(key);
                if (props == null) continue;
                yield return new ServiceDisplayOption { key = key, label = props.LabelCap, description = props.description, groupKey = CycleGroupKey };
            }
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) =>
            BuildQuoteLineItems(request, new ServiceBatchAllocationContext());

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request, ServiceBatchAllocationContext batchContext)
        {
            var lineItems = new List<ServiceLineItem>();

            ServiceInputPlan nutritionPlan = batchContext.GetOrCreateInputPlan(request, () => BiosculpterInputPlanning.PlanNutrition(request, batchContext.StockLedger));
            int nutritionCost = BiosculpterInputPlanning.PreviewCost(nutritionPlan);
            if (nutritionCost > 0) lineItems.Add(new ServiceLineItem("SettlementServices.LineItem.BiosculpterNutrition", nutritionCost));

            return lineItems;
        }

        public override List<ServiceStockRequirement> GetDynamicStockRequirements(SettlementServiceRequest request) =>
            new List<ServiceStockRequirement> { BiosculpterInputPlanning.NutritionRequirement() };

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote) =>
            PlanInputs(request, quote, new ServiceBatchAllocationContext());

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote, ServiceBatchAllocationContext batchContext)
        {
            ServiceInputPlan nutritionPlan = batchContext.GetOrCreateInputPlan(request, () => BiosculpterInputPlanning.PlanNutrition(request, batchContext.StockLedger));
            var plan = new ServiceInputPlan();
            plan.stockConsumed.AddRange(nutritionPlan.stockConsumed);
            return plan;
        }

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            if (!(request.target.thing is Pawn pawn)) return null;
            string key = request.selectedOptionKeys.FirstOrDefault();
            if (key == null) return null;
            return EligibleCycleKeys(pawn).Contains(key) ? null : "SettlementServices.Error.NoBiosculpterCycleAvailable";
        }

        public override int? ExpectedDurationTicksFor(SettlementServiceRequest request)
        {
            string key = request.selectedOptionKeys.FirstOrDefault();
            int? baseTicks = BiosculpterCycleService.BaseTicksFor(key);
            if (baseTicks == null) return null;

            float multiplier = BiosculpterQualityService.DurationMultiplier(request.settlement, def.category);
            return Mathf.Max(1, Mathf.RoundToInt(baseTicks.Value * multiplier));
        }

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTarget?.liveThing is Pawn pawn)) return ServiceCompletionResult.Ok();

            string key = ctx.SelectedOptionKeys.FirstOrDefault();
            if (key == BiosculpterCycleService.AgeReversalKey && !BiosculpterCycleService.CanAgeReverse(pawn))
                return ServiceCompletionResult.Ok();

            BiosculpterCycleService.ResolveCycle(key)?.CycleCompleted(pawn);
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static IEnumerable<string> EligibleCycleKeys(Pawn pawn)
        {
            if (BiosculpterCycleService.ResolveCycleProps(BiosculpterCycleService.BioregenerationKey) != null)
                yield return BiosculpterCycleService.BioregenerationKey;
            if (BiosculpterCycleService.ResolveCycleProps(BiosculpterCycleService.AgeReversalKey) != null && BiosculpterCycleService.CanAgeReverse(pawn))
                yield return BiosculpterCycleService.AgeReversalKey;
        }
    }
}

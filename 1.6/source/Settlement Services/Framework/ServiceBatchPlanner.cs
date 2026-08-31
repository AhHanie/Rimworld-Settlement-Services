using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Pricing;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Validation;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Framework
{
    internal class ServiceBatchPlan
    {
        public SettlementServiceQuote Quote;
        public ServiceInputPlan InputPlan;
        public object AcceptedWorkerData;
    }

    internal static class ServiceBatchPlanner
    {
        internal static bool TryBuildUnits(SettlementServiceDef def, SettlementServiceRequest request, out List<SettlementServiceRequest> units, out string errorKey)
        {
            units = null;
            ServiceBatchMode mode = def.EffectiveBatchMode;
            List<ServiceTargetSelection> targets = request.targets ?? new List<ServiceTargetSelection>();

            if (mode == ServiceBatchMode.Quantity)
            {
                if (targets.Count > 0) { errorKey = "SettlementServices.Error.InvalidTarget"; return false; }
                if (request.quantity <= 0) { errorKey = "SettlementServices.Error.InvalidQuantity"; return false; }
                if (def.maxBatchCount > 0 && request.quantity > def.maxBatchCount)
                { errorKey = "SettlementServices.Error.BatchCountExceedsMaximum"; return false; }

                units = Enumerable.Range(0, request.quantity).Select(_ => request.ForIteration()).ToList();
                errorKey = null;
                return true;
            }

            if (mode == ServiceBatchMode.None && targets.Count > 1)
            { errorKey = "SettlementServices.Error.BatchNotSupported"; return false; }

            if (mode == ServiceBatchMode.Targets)
            {
                if (targets.Count == 0) { errorKey = "SettlementServices.Error.NoTargetSelected"; return false; }
                if (def.maxBatchCount > 0 && targets.Count > def.maxBatchCount)
                { errorKey = "SettlementServices.Error.BatchCountExceedsMaximum"; return false; }
            }

            if (def.targetRule == ServiceTargetRule.None && targets.Count > 0)
            { errorKey = "SettlementServices.Error.InvalidTarget"; return false; }
            if (def.targetRule != ServiceTargetRule.None && targets.Count == 0)
            { errorKey = "SettlementServices.Error.NoTargetSelected"; return false; }

            var seen = new HashSet<Thing>();
            foreach (ServiceTargetSelection selection in targets)
            {
                if (selection?.thing == null) { errorKey = "SettlementServices.Error.InvalidTarget"; return false; }
                if (!seen.Add(selection.thing)) { errorKey = "SettlementServices.Error.DuplicateTarget"; return false; }
                if (!SettlementServiceValidator.MatchesTargetRule(selection.kind, def.targetRule))
                { errorKey = "SettlementServices.Error.InvalidTarget"; return false; }
            }

            units = targets.Count > 0
                ? targets.Select((t, i) => request.ForTarget(t, i)).ToList()
                : new List<SettlementServiceRequest> { request.ForIteration() };
            errorKey = null;
            return true;
        }

        internal static bool TryBuildBatchQuote(SettlementServiceDef def, SettlementServiceRequest request, out SettlementServiceQuote quote, out string errorKey)
        {
            bool ok = TryBuildBatchQuoteAndPlan(def, request, includeInputPlan: false, out quote, out _, out _, out errorKey);
            return ok;
        }

        internal static bool TryBuildAcceptancePlan(SettlementServiceDef def, SettlementServiceRequest request, out ServiceBatchPlan plan, out string errorKey)
        {
            plan = null;
            if (!TryBuildBatchQuoteAndPlan(def, request, includeInputPlan: true, out SettlementServiceQuote quote, out ServiceInputPlan inputPlan, out object workerData, out errorKey))
                return false;

            plan = new ServiceBatchPlan { Quote = quote, InputPlan = inputPlan, AcceptedWorkerData = workerData };
            return true;
        }

        private static bool TryBuildBatchQuoteAndPlan(SettlementServiceDef def, SettlementServiceRequest request, bool includeInputPlan,
            out SettlementServiceQuote quote, out ServiceInputPlan inputPlan, out object workerData, out string errorKey)
        {
            quote = null;
            inputPlan = null;
            workerData = null;

            if (!SettlementServiceValidator.Validate(def, request, out errorKey)) return false;
            if (!SettlementServiceOrchestrator.TierGoodwillMet(def, request, out errorKey)) return false;
            if (!TryBuildUnits(def, request, out List<SettlementServiceRequest> units, out errorKey)) return false;

            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            Settlement settlement = request.settlement;

            var unitQuotes = new List<SettlementServiceQuote>();
            var unitQuoteDtos = new List<ServiceUnitQuote>();
            var combinedStockRequirements = new List<ServiceStockRequirement>();
            var stockConsumed = new List<ThingDefCountClass>();
            var playerSuppliedConsumed = new List<ThingDefCountClass>();
            object acceptedWorkerData = null;

            List<ThingDefCountClass> remainingPlayerSupplied = CloneCounts(request.playerSuppliedInputs);
            var batchContext = new ServiceBatchAllocationContext();

            for (int unitIndex = 0; unitIndex < units.Count; unitIndex++)
            {
                SettlementServiceRequest unit = units[unitIndex];
                unit.playerSuppliedInputs = remainingPlayerSupplied;

                var availabilityCtx = new SettlementServiceContext(domain, settlement, null, unit.target?.thing, unit.negotiator?.GetCaravan(), unit.selectedOptionKeys, unit.selectedTierKey);
                ServiceAvailabilityReport report = def.Worker.CanOffer(availabilityCtx);
                if (!report.IsAvailable) { errorKey = report.ErrorKey; return false; }

                string unitError = def.Worker.ValidateUnitRequest(unit);
                if (unitError != null) { errorKey = unitError; return false; }

                var lineItems = new List<ServiceLineItem>(def.Worker.BuildQuoteLineItems(unit, batchContext));
                SettlementServiceQuote unitQuote = ServicePricingEngine.Finalize(def, unit, lineItems);
                unitQuotes.Add(unitQuote);
                unitQuoteDtos.Add(new ServiceUnitQuote
                {
                    unitIndex = unitIndex,
                    targetLabel = unit.target?.thing?.LabelCap,
                    totalCost = unitQuote.totalCost,
                    lineItems = unitQuote.lineItems,
                });

                ServiceInputPlan unitInputPlan = def.Worker.PlanInputs(unit, unitQuote, batchContext);
                if (includeInputPlan)
                {
                    MergeCounts(stockConsumed, unitInputPlan.stockConsumed);
                    MergeCounts(playerSuppliedConsumed, unitInputPlan.playerSuppliedConsumed);
                    acceptedWorkerData = def.Worker.BuildAcceptedData(unit, unitQuote);
                }
                remainingPlayerSupplied = SubtractCounts(remainingPlayerSupplied, unitInputPlan.playerSuppliedConsumed);

                if (!def.stockRequirements.NullOrEmpty()) MergeRequirements(combinedStockRequirements, def.stockRequirements);
                MergeRequirements(combinedStockRequirements, def.Worker.GetDynamicStockRequirements(unit));
            }

            if (settlement != null && combinedStockRequirements.Count > 0)
            {
                StockAvailabilityReport stock = SettlementStockService.CheckAvailability(settlement, combinedStockRequirements, request.playerSuppliedInputs);
                if (!stock.IsAvailable) { errorKey = stock.ErrorKey; return false; }
            }

            quote = AggregateQuotes(def, request, units, unitQuotes, unitQuoteDtos);
            if (includeInputPlan) inputPlan = new ServiceInputPlan { stockConsumed = stockConsumed, playerSuppliedConsumed = playerSuppliedConsumed };
            workerData = acceptedWorkerData;
            errorKey = null;
            return true;
        }

        private static List<ThingDefCountClass> CloneCounts(List<ThingDefCountClass> source) =>
            source.Select(c => new ThingDefCountClass(c.thingDef, c.count)).ToList();

        private static List<ThingDefCountClass> SubtractCounts(List<ThingDefCountClass> list, List<ThingDefCountClass> subtractions)
        {
            var result = CloneCounts(list);
            foreach (ThingDefCountClass subtraction in subtractions)
            {
                ThingDefCountClass existing = result.Find(x => x.thingDef == subtraction.thingDef);
                if (existing != null) existing.count = Mathf.Max(0, existing.count - subtraction.count);
            }
            result.RemoveAll(x => x.count <= 0);
            return result;
        }

        internal static void MergeRequirements(List<ServiceStockRequirement> combined, IEnumerable<ServiceStockRequirement> additions)
        {
            foreach (ServiceStockRequirement addition in additions)
            {
                ServiceStockRequirement existing = combined.Find(r => r.IsExact
                    ? r.thingDefName == addition.thingDefName
                    : !addition.IsExact && r.stockCategoryDefName == addition.stockCategoryDefName);
                if (existing != null)
                {
                    existing.amount += addition.amount;
                    existing.nutritionRequired += addition.nutritionRequired;
                }
                else combined.Add(addition.Clone());
            }
        }

        private static void MergeCounts(List<ThingDefCountClass> list, List<ThingDefCountClass> additions)
        {
            foreach (ThingDefCountClass addition in additions)
            {
                ThingDefCountClass existing = list.Find(x => x.thingDef == addition.thingDef);
                if (existing != null) existing.count += addition.count;
                else list.Add(new ThingDefCountClass(addition.thingDef, addition.count));
            }
        }

        private static SettlementServiceQuote AggregateQuotes(SettlementServiceDef def, SettlementServiceRequest request, List<SettlementServiceRequest> units, List<SettlementServiceQuote> unitQuotes, List<ServiceUnitQuote> unitQuoteDtos)
        {
            var mergedLineItems = new List<ServiceLineItem>();
            int totalCost = 0;
            int duration = 0;
            bool sumDuration = def.EffectiveBatchMode == ServiceBatchMode.Quantity;

            foreach (SettlementServiceQuote unitQuote in unitQuotes)
            {
                totalCost += unitQuote.totalCost;
                duration = sumDuration ? duration + unitQuote.expectedDurationTicks : Mathf.Max(duration, unitQuote.expectedDurationTicks);

                foreach (ServiceLineItem lineItem in unitQuote.lineItems)
                {
                    ServiceLineItem existing = mergedLineItems.Find(li => li.labelKey == lineItem.labelKey && li.labelArgument == lineItem.labelArgument && li.isModifier == lineItem.isModifier);
                    if (existing != null) existing.amount += lineItem.amount;
                    else mergedLineItems.Add(new ServiceLineItem(lineItem.labelKey, lineItem.amount, lineItem.isModifier, lineItem.labelArgument));
                }
            }

            return new SettlementServiceQuote
            {
                lineItems = mergedLineItems,
                totalCost = totalCost,
                selectedTierKey = request.selectedTierKey,
                expectedDurationTicks = duration,
                unitCount = units.Count,
                unitPrice = units.Count > 0 ? Mathf.RoundToInt(totalCost / (float)units.Count) : totalCost,
                perUnitQuotes = unitQuoteDtos,
            };
        }
    }
}

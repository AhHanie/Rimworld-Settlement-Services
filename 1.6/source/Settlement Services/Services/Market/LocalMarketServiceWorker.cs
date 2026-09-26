using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Market
{
    public class LocalMarketServiceWorker : SettlementServiceWorker
    {
        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx) =>
            LocalMarketCatalog.HasEligibleStock(ctx.Settlement)
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoMarketGoodsAvailable");

        public override string ValidateUnitRequest(SettlementServiceRequest request) =>
            TryPlan(request, out _, out string errorKey) ? null : errorKey;

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request)
        {
            var items = new List<ServiceLineItem>();
            if (!TryPlan(request, out MarketPurchasePlan plan, out _)) return items;

            foreach (MarketCartLine line in plan.lines)
            {
                ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(line.thingDefName);
                if (thingDef == null) continue;

                int cost = LocalMarketCatalog.UnitPrice(thingDef) * line.count;
                items.Add(new ServiceLineItem("SettlementServices.LineItem.MarketPurchase", cost, labelArgument: thingDef.LabelCap));
            }

            return items;
        }

        public override List<ServiceStockRequirement> GetDynamicStockRequirements(SettlementServiceRequest request) =>
            TryPlan(request, out MarketPurchasePlan plan, out _) ? ToStockRequirements(plan) : new List<ServiceStockRequirement>();

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote)
        {
            if (!TryPlan(request, out MarketPurchasePlan plan, out _)) return ServiceInputPlan.None;

            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, ToStockRequirements(plan), request.playerSuppliedInputs);
            if (!allocation.Success) return ServiceInputPlan.None;

            return new ServiceInputPlan { stockConsumed = allocation.SettlementSupplied, playerSuppliedConsumed = allocation.PlayerSupplied };
        }

        public override object BuildAcceptedData(SettlementServiceRequest request, SettlementServiceQuote quote) =>
            TryPlan(request, out MarketPurchasePlan plan, out _) ? plan.Clone() : null;

        public override ServiceStartResult Start(ServiceJobContext ctx) =>
            Validate(ctx.Job.marketPurchasePlan, out string errorKey) ? ServiceStartResult.Ok : ServiceStartResult.Fail(errorKey);

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            MarketPurchasePlan plan = ctx.Job.marketPurchasePlan;
            if (!Validate(plan, out string errorKey)) return ServiceCompletionResult.Fail(errorKey);

            var resultThings = new List<Thing>();
            foreach (MarketCartLine line in plan.lines)
            {
                ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(line.thingDefName);
                if (thingDef == null) continue;

                int remaining = line.count;
                int stackLimit = Mathf.Max(1, thingDef.stackLimit);
                while (remaining > 0)
                {
                    int stackCount = Mathf.Min(remaining, stackLimit);
                    Thing thing = ThingMaker.MakeThing(thingDef);
                    thing.stackCount = stackCount;

                    CompQuality compQuality = thing.TryGetComp<CompQuality>();
                    if (compQuality != null) compQuality.SetQuality(QualityCategory.Normal, ArtGenerationContext.Outsider);

                    resultThings.Add(thing);
                    remaining -= stackCount;
                }
            }

            return ServiceCompletionResult.Ok(resultThings: resultThings);
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static List<ServiceStockRequirement> ToStockRequirements(MarketPurchasePlan plan) =>
            plan.lines.Select(l => new ServiceStockRequirement { thingDefName = l.thingDefName, amount = l.count, playerCanSupply = false }).ToList();

        private static bool Validate(MarketPurchasePlan plan, out string errorKey)
        {
            if (plan == null || plan.lines.NullOrEmpty())
            {
                errorKey = "SettlementServices.Error.MarketCartEmpty";
                return false;
            }

            foreach (MarketCartLine line in plan.lines)
            {
                if (line == null || line.count <= 0)
                {
                    errorKey = "SettlementServices.Error.InvalidQuantity";
                    return false;
                }

                ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(line.thingDefName);
                if (thingDef == null || LocalMarketCatalog.GetGroup(thingDef) == null)
                {
                    errorKey = "SettlementServices.Error.MarketItemNoLongerAvailable";
                    return false;
                }
            }

            errorKey = null;
            return true;
        }

        private static bool TryPlan(SettlementServiceRequest request, out MarketPurchasePlan plan, out string errorKey)
        {
            plan = null;

            if (request.marketCartLines.NullOrEmpty())
            {
                errorKey = "SettlementServices.Error.MarketCartEmpty";
                return false;
            }

            var seenNames = new HashSet<string>();
            var lines = new List<MarketCartLine>();
            foreach (MarketCartLine line in request.marketCartLines)
            {
                if (line == null || line.count <= 0)
                {
                    errorKey = "SettlementServices.Error.InvalidQuantity";
                    return false;
                }
                if (!seenNames.Add(line.thingDefName))
                {
                    errorKey = "SettlementServices.Error.DuplicateMarketItem";
                    return false;
                }

                ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(line.thingDefName);
                if (thingDef == null || LocalMarketCatalog.GetGroup(thingDef) == null)
                {
                    errorKey = "SettlementServices.Error.MarketItemNoLongerAvailable";
                    return false;
                }

                lines.Add(line.Clone());
            }

            plan = new MarketPurchasePlan { lines = lines };
            errorKey = null;
            return true;
        }
    }
}

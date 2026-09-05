using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Framework.Compat.VehicleFramework
{
    public class VehicleFuelServiceWorker : SettlementServiceWorker
    {
        private const float FuelMarkupPct = 1.4f;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            Thing vehicle = ctx.SelectedTarget;
            if (vehicle == null) return ServiceAvailabilityReport.Available;
            if (!VehicleFrameworkAdapter.TryGetFuel(vehicle, out float current, out float capacity, out ThingDef fuelDef))
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.VehicleHasNoFuelTank");
            if (fuelDef == null || SettlementStockCatalog.ItemFor(fuelDef) == null)
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.VehicleFuelNotStocked");
            return current < capacity
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.VehicleFuelAlreadyFull");
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request)
        {
            var items = new List<ServiceLineItem>();
            int cost = SettlementSuppliedFuelCost(request);
            if (cost > 0) items.Add(new ServiceLineItem("SettlementServices.LineItem.VehicleFuelTopUp", cost));
            return items;
        }

        public override List<ServiceStockRequirement> GetDynamicStockRequirements(SettlementServiceRequest request)
        {
            var result = new List<ServiceStockRequirement>();
            if (!MissingFuel(request.target.thing, out float missing, out ThingDef fuelDef)) return result;
            if (SettlementStockCatalog.ItemFor(fuelDef) != null)
                result.Add(new ServiceStockRequirement { thingDefName = fuelDef.defName, amount = Mathf.CeilToInt(missing), playerCanSupply = true });
            return result;
        }

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote)
        {
            if (!MissingFuel(request.target.thing, out float missing, out ThingDef fuelDef) || SettlementStockCatalog.ItemFor(fuelDef) == null)
                return ServiceInputPlan.None;

            var requirement = new ServiceStockRequirement { thingDefName = fuelDef.defName, amount = Mathf.CeilToInt(missing), playerCanSupply = true };
            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, new List<ServiceStockRequirement> { requirement }, request.playerSuppliedInputs);
            if (!allocation.Success) return ServiceInputPlan.None;

            return new ServiceInputPlan { stockConsumed = allocation.SettlementSupplied, playerSuppliedConsumed = allocation.PlayerSupplied };
        }

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            Thing vehicle = ctx.CurrentTarget?.liveThing;
            if (vehicle == null || !VehicleFrameworkAdapter.TryGetFuel(vehicle, out float current, out float capacity, out _))
                return ServiceCompletionResult.Fail("SettlementServices.Error.TargetNoLongerExists");

            float missing = capacity - current;
            if (missing > 0f) VehicleFrameworkAdapter.TryRefuel(vehicle, missing);
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static bool MissingFuel(Thing vehicle, out float missing, out ThingDef fuelDef)
        {
            missing = 0f; fuelDef = null;
            if (vehicle == null || !VehicleFrameworkAdapter.TryGetFuel(vehicle, out float current, out float capacity, out fuelDef)) return false;
            missing = capacity - current;
            return missing > 0f && fuelDef != null;
        }

        private static int SettlementSuppliedFuelCost(SettlementServiceRequest request)
        {
            if (!MissingFuel(request.target.thing, out float missing, out ThingDef fuelDef) || SettlementStockCatalog.ItemFor(fuelDef) == null) return 0;

            var requirement = new ServiceStockRequirement { thingDefName = fuelDef.defName, amount = Mathf.CeilToInt(missing), playerCanSupply = true };
            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, new List<ServiceStockRequirement> { requirement }, request.playerSuppliedInputs);
            if (!allocation.Success) return 0;

            float total = allocation.SettlementSupplied.Sum(c => c.count * c.thingDef.BaseMarketValue * FuelMarkupPct);
            return Mathf.RoundToInt(total);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Android
{
    internal static class AndroidMaterialPlanner
    {
        public static List<ServiceStockRequirement> RequirementsFor(ThingDef part)
        {
            var result = new List<ServiceStockRequirement>();
            if (part == null || SettlementStockCatalog.ItemFor(part) == null) return result;

            result.Add(new ServiceStockRequirement { thingDefName = part.defName, amount = 1, playerCanSupply = true });
            return result;
        }

        public static ServiceInputPlan PlanInputs(SettlementServiceRequest request, ThingDef part)
        {
            var plan = new ServiceInputPlan();
            List<ServiceStockRequirement> requirements = RequirementsFor(part);
            if (requirements.Count == 0) return plan;

            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, requirements, request.playerSuppliedInputs);
            if (!allocation.Success) return plan;

            plan.stockConsumed.AddRange(allocation.SettlementSupplied);
            plan.playerSuppliedConsumed.AddRange(allocation.PlayerSupplied);
            return plan;
        }

        public static int SettlementSuppliedCost(SettlementServiceRequest request, ThingDef part, float markupPct)
        {
            List<ServiceStockRequirement> requirements = RequirementsFor(part);
            if (requirements.Count == 0) return 0;

            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, requirements, request.playerSuppliedInputs);
            if (!allocation.Success) return 0;

            float total = allocation.SettlementSupplied.Sum(c => c.count * c.thingDef.BaseMarketValue * markupPct);
            return Mathf.RoundToInt(total);
        }
    }
}

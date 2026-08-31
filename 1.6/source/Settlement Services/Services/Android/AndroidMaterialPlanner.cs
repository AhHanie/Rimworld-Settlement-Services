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
        public static List<ServiceStockRequirement> RequirementsFor(List<ThingDefCountClass> costList)
        {
            var result = new List<ServiceStockRequirement>();
            if (costList == null) return result;

            foreach (ThingDefCountClass cost in costList)
            {
                if (SettlementStockCatalog.ItemFor(cost.thingDef) == null) continue;

                ServiceStockRequirement existing = result.Find(r => r.thingDefName == cost.thingDef.defName);
                if (existing != null) existing.amount += cost.count;
                else result.Add(new ServiceStockRequirement { thingDefName = cost.thingDef.defName, amount = cost.count, playerCanSupply = true });
            }
            return result;
        }

        public static ServiceInputPlan PlanInputs(SettlementServiceRequest request, List<ThingDefCountClass> costList)
        {
            var plan = new ServiceInputPlan();
            List<ServiceStockRequirement> requirements = RequirementsFor(costList);
            if (requirements.Count == 0) return plan;

            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, requirements, request.playerSuppliedInputs);
            if (!allocation.Success) return plan;

            plan.stockConsumed.AddRange(allocation.SettlementSupplied);
            plan.playerSuppliedConsumed.AddRange(allocation.PlayerSupplied);
            return plan;
        }

        public static int SettlementSuppliedCost(SettlementServiceRequest request, List<ThingDefCountClass> costList, float markupPct)
        {
            List<ServiceStockRequirement> requirements = RequirementsFor(costList);
            if (requirements.Count == 0) return 0;

            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, requirements, request.playerSuppliedInputs);
            if (!allocation.Success) return 0;

            float total = allocation.SettlementSupplied.Sum(c => c.count * c.thingDef.BaseMarketValue * markupPct);
            return Mathf.RoundToInt(total);
        }
    }
}

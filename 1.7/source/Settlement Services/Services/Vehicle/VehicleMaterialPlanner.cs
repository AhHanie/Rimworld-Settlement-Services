using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Vehicle
{
    internal static class VehicleMaterialPlanner
    {
        public static List<ServiceStockRequirement> RequirementsFor(List<ThingDefCountClass> ingredients)
        {
            var result = new List<ServiceStockRequirement>();
            if (ingredients == null) return result;

            foreach (ThingDefCountClass ingredient in ingredients)
            {
                if (SettlementStockCatalog.ItemFor(ingredient.thingDef) == null) continue;

                ServiceStockRequirement existing = result.Find(r => r.thingDefName == ingredient.thingDef.defName);
                if (existing != null) existing.amount += ingredient.count;
                else result.Add(new ServiceStockRequirement { thingDefName = ingredient.thingDef.defName, amount = ingredient.count, playerCanSupply = true });
            }
            return result;
        }

        public static ServiceInputPlan PlanInputs(SettlementServiceRequest request, List<ThingDefCountClass> ingredients)
        {
            var plan = new ServiceInputPlan();
            List<ServiceStockRequirement> requirements = RequirementsFor(ingredients);
            if (requirements.Count == 0) return plan;

            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, requirements, request.playerSuppliedInputs);
            if (!allocation.Success) return plan;

            plan.stockConsumed.AddRange(allocation.SettlementSupplied);
            plan.playerSuppliedConsumed.AddRange(allocation.PlayerSupplied);
            return plan;
        }

        public static int SettlementSuppliedCost(SettlementServiceRequest request, List<ThingDefCountClass> ingredients, float markupPct)
        {
            List<ServiceStockRequirement> requirements = RequirementsFor(ingredients);
            if (requirements.Count == 0) return 0;

            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, requirements, request.playerSuppliedInputs);
            if (!allocation.Success) return 0;

            float total = allocation.SettlementSupplied.Sum(c => c.count * c.thingDef.BaseMarketValue * markupPct);
            return Mathf.RoundToInt(total);
        }
    }
}

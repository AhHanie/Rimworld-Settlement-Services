using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Genetics
{
    internal static class BiosculpterInputPlanning
    {
        private const string FoodStockCategory = "SettlementStock_Food";

        public static ServiceStockRequirement NutritionRequirement() =>
            new ServiceStockRequirement { stockCategoryDefName = FoodStockCategory, amount = 0, nutritionRequired = BiosculpterCycleService.NutritionRequired, playerCanSupply = false };

        public static ServiceInputPlan PlanNutrition(SettlementServiceRequest request, StockAllocationLedger ledger)
        {
            var plan = new ServiceInputPlan();
            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, new List<ServiceStockRequirement> { NutritionRequirement() }, request.playerSuppliedInputs, ledger);
            if (!allocation.Success) return plan;

            plan.stockConsumed.AddRange(allocation.SettlementSupplied);
            return plan;
        }

        public static int PreviewCost(ServiceInputPlan plan) =>
            plan.stockConsumed.Sum(c => Mathf.RoundToInt(c.thingDef.BaseMarketValue * c.count));
    }
}

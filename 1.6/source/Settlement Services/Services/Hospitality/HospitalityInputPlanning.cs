using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Hospitality
{
    internal static class HospitalityInputPlanning
    {
        private const string FoodStockCategory = "SettlementStock_Food";

        public static float RequiredNutrition(SettlementServiceRequest request, float nights)
        {
            if (!(request.target.thing is Pawn pawn)) return 0f;

            Need_Food food = pawn.needs?.food;
            if (food == null) return 0f;

            return food.MaxLevel + food.FoodFallPerTickAssumingCategory(HungerCategory.Fed) * GenDate.TicksPerDay * nights;
        }

        public static ServiceStockRequirement FoodRequirement(SettlementServiceRequest request, float nights)
        {
            float nutrition = RequiredNutrition(request, nights);
            if (nutrition <= 0f) return null;

            return new ServiceStockRequirement { stockCategoryDefName = FoodStockCategory, amount = 0, nutritionRequired = nutrition, playerCanSupply = false };
        }

        public static ServiceInputPlan PlanFood(SettlementServiceRequest request, float nights, StockAllocationLedger ledger)
        {
            var plan = new ServiceInputPlan();
            ServiceStockRequirement requirement = FoodRequirement(request, nights);
            if (requirement == null) return plan;

            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, new List<ServiceStockRequirement> { requirement }, request.playerSuppliedInputs, ledger);
            if (!allocation.Success) return plan;

            plan.stockConsumed.AddRange(allocation.SettlementSupplied);
            plan.playerSuppliedConsumed.AddRange(allocation.PlayerSupplied);
            return plan;
        }

        public static int PreviewCost(ServiceInputPlan plan) =>
            plan.stockConsumed.Concat(plan.playerSuppliedConsumed).Sum(c => Mathf.RoundToInt(c.thingDef.BaseMarketValue * c.count));
    }
}

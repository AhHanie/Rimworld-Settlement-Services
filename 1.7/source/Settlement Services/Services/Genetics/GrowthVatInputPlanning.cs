using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Genetics
{
    internal static class GrowthVatInputPlanning
    {
        public const string NutritionLineItemKey = "SettlementServices.LineItem.GrowthVatNutrition";

        private const string FoodStockCategory = "SettlementStock_Food";

        public static ServiceStockRequirement NutritionRequirement(float nutrition) =>
            new ServiceStockRequirement { stockCategoryDefName = FoodStockCategory, amount = 0, nutritionRequired = nutrition, playerCanSupply = false };

        public static ServiceInputPlan PlanNutrition(Settlement settlement, float nutrition, StockAllocationLedger ledger)
        {
            var plan = new ServiceInputPlan();
            if (settlement == null || nutrition <= 0f) return plan;

            StockAllocationResult allocation = SettlementStockService.TryAllocate(settlement, new List<ServiceStockRequirement> { NutritionRequirement(nutrition) }, null, ledger);
            if (!allocation.Success) return plan;

            plan.stockConsumed.AddRange(allocation.SettlementSupplied);
            return plan;
        }

        public static int PreviewCost(ServiceInputPlan plan) =>
            plan.stockConsumed.Sum(c => ValueOf(c.thingDef, c.count));

        public static float AllocatedNutrition(ServiceInputPlan plan) =>
            plan.stockConsumed.Sum(c => NutritionOf(c.thingDef) * c.count);

        public static int ValueOf(ThingDef thingDef, int count) => Mathf.RoundToInt(thingDef.BaseMarketValue * count);

        public static float NutritionOf(ThingDef thingDef) => thingDef.GetStatValueAbstract(StatDefOf.Nutrition);
    }
}

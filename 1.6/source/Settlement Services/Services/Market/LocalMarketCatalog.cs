using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Stock;

namespace Settlement_Services.Services.Market
{
    public enum MarketItemGroup
    {
        Drinks,
        Meat,
        Produce,
        Food,
    }

    public class LocalMarketCatalogRow
    {
        public ThingDef thingDef;
        public MarketItemGroup group;
        public int availableStock;
        public int unitPrice;
    }

    public static class LocalMarketCatalog
    {
        private const float RetailMarkupFactor = 1.4f;

        private static HashSet<string> _overrideDrinkDefNames;

        private static HashSet<string> OverrideDrinkDefNames
        {
            get
            {
                if (_overrideDrinkDefNames == null)
                {
                    _overrideDrinkDefNames = new HashSet<string>();
                    foreach (LocalMarketDisplayOverridesDef def in DefDatabase<LocalMarketDisplayOverridesDef>.AllDefsListForReading)
                        foreach (string name in def.drinkDefNames)
                            if (!name.NullOrEmpty()) _overrideDrinkDefNames.Add(name);
                }
                return _overrideDrinkDefNames;
            }
        }

        public static MarketItemGroup? GetGroup(ThingDef thingDef)
        {
            if (thingDef == null || thingDef.category != ThingCategory.Item) return null;
            if (!thingDef.tradeability.TraderCanSell()) return null;
            if (thingDef.BaseMarketValue <= 0f) return null;
            if (thingDef.ingestible == null || !thingDef.ingestible.HumanEdible) return null;

            if (MatchesDrinkRule(thingDef)) return MarketItemGroup.Drinks;
            if (thingDef.IsDrug) return null;
            if (!thingDef.IsNutritionGivingIngestible) return null;

            FoodTypeFlags foodType = thingDef.ingestible.foodType;
            if ((foodType & FoodTypeFlags.Meat) != 0) return MarketItemGroup.Meat;
            if ((foodType & FoodTypeFlags.VegetableOrFruit) != 0) return MarketItemGroup.Produce;
            if (thingDef.IsWithinCategory(ThingCategoryDefOf.MeatRaw)) return MarketItemGroup.Meat;
            if (thingDef.IsWithinCategory(ThingCategoryDefOf.PlantFoodRaw)) return MarketItemGroup.Produce;
            return MarketItemGroup.Food;
        }

        private static bool MatchesDrinkRule(ThingDef thingDef)
        {
            if (thingDef.ingestible.drugCategory == DrugCategory.Medical) return false;
            if (OverrideDrinkDefNames.Contains(thingDef.defName)) return true;

            FoodTypeFlags foodType = thingDef.ingestible.foodType;
            return (foodType & FoodTypeFlags.Fluid) != 0 || (foodType & FoodTypeFlags.Liquor) != 0;
        }

        public static int UnitPrice(ThingDef thingDef) =>
            Mathf.Max(1, Mathf.RoundToInt(thingDef.BaseMarketValue * RetailMarkupFactor));

        public static IEnumerable<LocalMarketCatalogRow> EligibleRows(Settlement settlement)
        {
            if (settlement == null) yield break;

            foreach (ThingDef thingDef in SettlementStockService.AllStockedThingDefsFor(settlement))
            {
                MarketItemGroup? group = GetGroup(thingDef);
                if (group == null) continue;

                int available = SettlementStockService.GetAvailableStock(settlement, thingDef);
                if (available <= 0) continue;

                yield return new LocalMarketCatalogRow
                {
                    thingDef = thingDef,
                    group = group.Value,
                    availableStock = available,
                    unitPrice = UnitPrice(thingDef),
                };
            }
        }

        public static List<LocalMarketCatalogRow> SortedEligibleRows(Settlement settlement) =>
            EligibleRows(settlement)
                .OrderBy(r => (int)r.group)
                .ThenBy(r => r.thingDef.label, StringComparer.Ordinal)
                .ThenBy(r => r.thingDef.defName, StringComparer.Ordinal)
                .ToList();

        public static bool HasEligibleStock(Settlement settlement) => EligibleRows(settlement).Any();
    }
}

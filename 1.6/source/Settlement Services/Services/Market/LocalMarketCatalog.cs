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
        Ingredients,
        Food,
    }

    public static class LocalMarketCatalog
    {
        private const float RetailMarkupFactor = 1.4f;

        private static HashSet<string> _overrideDrinkDefNames;
        private static HashSet<string> _overridePlayerSellOnlyDefNames;
        private static HashSet<string> _overridePantryIngredientDefNames;
        private static HashSet<string> _overrideMeatDisplayDefNames;

        private static HashSet<string> OverrideDrinkDefNames
        {
            get
            {
                EnsureOverrides();
                return _overrideDrinkDefNames;
            }
        }

        private static void EnsureOverrides()
        {
            if (_overrideDrinkDefNames != null) return;

            _overrideDrinkDefNames = new HashSet<string>();
            _overridePlayerSellOnlyDefNames = new HashSet<string>();
            _overridePantryIngredientDefNames = new HashSet<string>();
            _overrideMeatDisplayDefNames = new HashSet<string>();

            foreach (LocalMarketDisplayOverridesDef def in DefDatabase<LocalMarketDisplayOverridesDef>.AllDefsListForReading)
            {
                AddNames(_overrideDrinkDefNames, def.drinkDefNames);
                AddNames(_overridePlayerSellOnlyDefNames, def.playerSellOnlyDefNames);
                AddNames(_overridePantryIngredientDefNames, def.pantryIngredientDefNames);
                AddNames(_overrideMeatDisplayDefNames, def.meatDisplayDefNames);
            }
        }

        private static void AddNames(HashSet<string> target, List<string> names)
        {
            if (names == null) return;
            foreach (string name in names)
                if (!name.NullOrEmpty()) target.Add(name);
        }

        private static bool CanBeSold(ThingDef thingDef)
        {
            if (thingDef.tradeability.TraderCanSell()) return true;
            if (thingDef.tradeability != Tradeability.Sellable) return false;
            EnsureOverrides();
            return _overridePlayerSellOnlyDefNames.Contains(thingDef.defName);
        }

        public static MarketItemGroup? GetGroup(ThingDef thingDef)
        {
            if (thingDef == null || thingDef.category != ThingCategory.Item) return null;
            if (!CanBeSold(thingDef)) return null;
            if (thingDef.BaseMarketValue <= 0f) return null;

            EnsureOverrides();
            if (_overridePantryIngredientDefNames.Contains(thingDef.defName))
                return thingDef.IsDrug ? (MarketItemGroup?)null : MarketItemGroup.Ingredients;

            if (thingDef.ingestible == null || !thingDef.ingestible.HumanEdible) return null;

            if (MatchesDrinkRule(thingDef)) return MarketItemGroup.Drinks;
            if (thingDef.IsDrug) return null;
            if (!thingDef.IsNutritionGivingIngestible) return null;

            if (_overrideMeatDisplayDefNames.Contains(thingDef.defName)) return MarketItemGroup.Meat;

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

        public static string GroupLabelKey(MarketItemGroup group) => "SettlementServices.Label.MarketGroup" + group;

        public static IEnumerable<MarketCatalogRow> EligibleRows(Settlement settlement)
        {
            if (settlement == null) yield break;

            foreach (ThingDef thingDef in SettlementStockService.AllStockedThingDefsFor(settlement))
            {
                MarketItemGroup? group = GetGroup(thingDef);
                if (group == null) continue;

                int available = SettlementStockService.GetAvailableStock(settlement, thingDef);
                if (available <= 0) continue;

                yield return new MarketCatalogRow
                {
                    thingDef = thingDef,
                    groupOrder = (int)group.Value,
                    groupLabelKey = GroupLabelKey(group.Value),
                    availableStock = available,
                    unitPrice = UnitPrice(thingDef),
                };
            }
        }

        public static List<MarketCatalogRow> SortedEligibleRows(Settlement settlement) =>
            EligibleRows(settlement)
                .OrderBy(r => r.groupOrder)
                .ThenBy(r => r.thingDef.label, StringComparer.Ordinal)
                .ThenBy(r => r.thingDef.defName, StringComparer.Ordinal)
                .ToList();
    }
}

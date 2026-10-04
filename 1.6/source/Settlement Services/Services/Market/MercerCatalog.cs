using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Stock;

namespace Settlement_Services.Services.Market
{
    public enum MercerItemGroup
    {
        Wood,
        Stone,
        Metal,
        Fabric,
        Leather,
    }

    public static class MercerCatalog
    {
        private static readonly Dictionary<string, MercerItemGroup> GroupByDefName = new Dictionary<string, MercerItemGroup>
        {
            { "WoodLog", MercerItemGroup.Wood },
            { "BlocksSandstone", MercerItemGroup.Stone },
            { "BlocksGranite", MercerItemGroup.Stone },
            { "BlocksLimestone", MercerItemGroup.Stone },
            { "BlocksSlate", MercerItemGroup.Stone },
            { "BlocksMarble", MercerItemGroup.Stone },
            { "DankPyon_BlocksClay", MercerItemGroup.Stone },
            { "Steel", MercerItemGroup.Metal },
            { "DankPyon_IronIngot", MercerItemGroup.Metal },
            { "Cloth", MercerItemGroup.Fabric },
            { "DankPyon_Linen", MercerItemGroup.Fabric },
        };

        public static MercerItemGroup? GetGroup(ThingDef thingDef)
        {
            if (thingDef == null || thingDef.category != ThingCategory.Item) return null;
            if (!thingDef.tradeability.TraderCanSell()) return null;
            if (thingDef.BaseMarketValue <= 0f) return null;
            if (GroupByDefName.TryGetValue(thingDef.defName, out MercerItemGroup group)) return group;
            return thingDef.IsWithinCategory(ThingCategoryDefOf.Leathers) ? MercerItemGroup.Leather : (MercerItemGroup?)null;
        }

        public static string GroupLabelKey(MercerItemGroup group) => "SettlementServices.Label.MercerGroup" + group;

        public static IEnumerable<MarketCatalogRow> EligibleRows(Settlement settlement)
        {
            if (settlement == null) yield break;

            foreach (ThingDef thingDef in SettlementStockService.AllStockedThingDefsFor(settlement))
            {
                MercerItemGroup? group = GetGroup(thingDef);
                if (group == null) continue;

                int available = SettlementStockService.GetAvailableStock(settlement, thingDef);
                if (available <= 0) continue;

                yield return new MarketCatalogRow
                {
                    thingDef = thingDef,
                    groupOrder = (int)group.Value,
                    groupLabelKey = GroupLabelKey(group.Value),
                    availableStock = available,
                    unitPrice = LocalMarketCatalog.UnitPrice(thingDef),
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

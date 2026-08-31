using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Framework.Stock
{
    public static class SettlementStockCatalog
    {
        private static Dictionary<ThingDef, SettlementStockItemReference> byThing;
        private static Dictionary<string, SettlementStockItemReference> byThingDefName;
        private static Dictionary<SettlementStockCategoryDef, List<SettlementStockItemReference>> byCategory;
        private static List<ThingDef> allStockedThingDefs;

        public static SettlementStockItemReference ItemFor(ThingDef thingDef)
        {
            EnsureBuilt();
            return thingDef != null && byThing.TryGetValue(thingDef, out SettlementStockItemReference reference) ? reference : null;
        }

        public static SettlementStockItemReference ItemFor(string thingDefName)
        {
            EnsureBuilt();
            return thingDefName != null && byThingDefName.TryGetValue(thingDefName, out SettlementStockItemReference reference) ? reference : null;
        }

        public static SettlementStockCategoryDef CategoryFor(ThingDef thingDef) => ItemFor(thingDef)?.category;

        public static IReadOnlyList<SettlementStockItemReference> ItemsFor(SettlementStockCategoryDef category)
        {
            EnsureBuilt();
            return category != null && byCategory.TryGetValue(category, out List<SettlementStockItemReference> refs)
                ? refs
                : (IReadOnlyList<SettlementStockItemReference>)Array.Empty<SettlementStockItemReference>();
        }

        public static IReadOnlyList<ThingDef> AllStockedThingDefs()
        {
            EnsureBuilt();
            return allStockedThingDefs;
        }

        private static void EnsureBuilt()
        {
            if (byThing != null) return;

            byThing = new Dictionary<ThingDef, SettlementStockItemReference>();
            byThingDefName = new Dictionary<string, SettlementStockItemReference>();
            byCategory = new Dictionary<SettlementStockCategoryDef, List<SettlementStockItemReference>>();

            foreach (SettlementStockCategoryDef category in DefDatabase<SettlementStockCategoryDef>.AllDefsListForReading)
            {
                var refs = new List<SettlementStockItemReference>();
                byCategory[category] = refs;

                foreach (SettlementStockItem item in category.stockItems)
                {
                    ThingDef thing = DefDatabase<ThingDef>.GetNamedSilentFail(item.thingDefName);
                    if (thing == null) continue;

                    if (byThing.ContainsKey(thing))
                    {
                        Settlement_Services.SupportLog.Error($"{item.thingDefName} is stocked by more than one SettlementStockCategoryDef; ignoring the entry in {category.defName}.");
                        continue;
                    }

                    var reference = new SettlementStockItemReference(thing, category, item);
                    byThing[thing] = reference;
                    byThingDefName[item.thingDefName] = reference;
                    refs.Add(reference);
                }
            }

            allStockedThingDefs = byThing.Keys.ToList();
        }
    }
}

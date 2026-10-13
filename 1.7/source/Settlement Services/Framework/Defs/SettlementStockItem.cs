using System.Collections.Generic;
using RimWorld;

namespace Settlement_Services.Framework.Defs
{
    public class SettlementStockItem
    {
        public string thingDefName;
        public int capacity = 10;
        public int refreshAmount = 1;
        public int refreshIntervalTicks = 60000;
        public TechLevel minFactionTechLevel = TechLevel.Undefined;
        public List<SettlementStockItemTier> techLevelTiers;
    }
}

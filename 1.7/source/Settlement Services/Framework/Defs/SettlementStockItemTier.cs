using RimWorld;

namespace Settlement_Services.Framework.Defs
{
    public class SettlementStockItemTier
    {
        public TechLevel minFactionTechLevel = TechLevel.Undefined;
        public int capacity = 10;
        public int refreshAmount = 1;
        public int refreshIntervalTicks = 60000;
    }
}

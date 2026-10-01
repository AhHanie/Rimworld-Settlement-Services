using RimWorld;

namespace Settlement_Services.Framework.Defs
{
    public class SettlementDynamicStockPoolTier
    {
        public TechLevel minFactionTechLevel = TechLevel.Undefined;
        public int minDifferentGoods = 1;
        public int maxDifferentGoods = 1;
        public int minCapacity = 10;
        public int maxCapacity = 10;
        public int minRefreshAmount = 1;
        public int maxRefreshAmount = 1;
        public int refreshIntervalTicks = 60000;
        public int replaceCount = 1;
    }
}

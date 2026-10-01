using RimWorld;
using Verse;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Framework.Stock
{
    public struct SettlementDynamicStockEffectiveSettings
    {
        public readonly TechLevel selectedThreshold;
        public readonly int minDifferentGoods;
        public readonly int maxDifferentGoods;
        public readonly int minCapacity;
        public readonly int maxCapacity;
        public readonly int minRefreshAmount;
        public readonly int maxRefreshAmount;
        public readonly int refreshIntervalTicks;
        public readonly int replaceCount;

        private SettlementDynamicStockEffectiveSettings(TechLevel selectedThreshold, int minDifferentGoods, int maxDifferentGoods, int minCapacity, int maxCapacity,
            int minRefreshAmount, int maxRefreshAmount, int refreshIntervalTicks, int replaceCount)
        {
            this.selectedThreshold = selectedThreshold;
            this.minDifferentGoods = minDifferentGoods;
            this.maxDifferentGoods = maxDifferentGoods;
            this.minCapacity = minCapacity;
            this.maxCapacity = maxCapacity;
            this.minRefreshAmount = minRefreshAmount;
            this.maxRefreshAmount = maxRefreshAmount;
            this.refreshIntervalTicks = refreshIntervalTicks;
            this.replaceCount = replaceCount;
        }

        public static bool IsEligible(SettlementDynamicStockPool pool, TechLevel factionTechLevel) =>
            pool.minFactionTechLevel == TechLevel.Undefined || (int)pool.minFactionTechLevel <= (int)factionTechLevel;

        public static SettlementDynamicStockEffectiveSettings For(SettlementDynamicStockPool pool, TechLevel factionTechLevel)
        {
            SettlementDynamicStockPoolTier bestTier = null;

            if (!pool.techLevelTiers.NullOrEmpty())
            {
                foreach (SettlementDynamicStockPoolTier tier in pool.techLevelTiers)
                {
                    if ((int)tier.minFactionTechLevel > (int)factionTechLevel) continue;
                    if (bestTier == null || (int)tier.minFactionTechLevel > (int)bestTier.minFactionTechLevel)
                        bestTier = tier;
                }
            }

            return bestTier != null
                ? new SettlementDynamicStockEffectiveSettings(bestTier.minFactionTechLevel, bestTier.minDifferentGoods, bestTier.maxDifferentGoods, bestTier.minCapacity, bestTier.maxCapacity,
                    bestTier.minRefreshAmount, bestTier.maxRefreshAmount, bestTier.refreshIntervalTicks, bestTier.replaceCount)
                : new SettlementDynamicStockEffectiveSettings(pool.minFactionTechLevel, pool.minDifferentGoods, pool.maxDifferentGoods, pool.minCapacity, pool.maxCapacity,
                    pool.minRefreshAmount, pool.maxRefreshAmount, pool.refreshIntervalTicks, pool.replaceCount);
        }
    }
}

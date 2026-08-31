using RimWorld;
using Verse;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Framework.Stock
{
    public struct SettlementStockEffectiveSettings
    {
        public readonly int capacity;
        public readonly int refreshAmount;
        public readonly int refreshIntervalTicks;
        public readonly TechLevel selectedThreshold;

        private SettlementStockEffectiveSettings(int capacity, int refreshAmount, int refreshIntervalTicks, TechLevel selectedThreshold)
        {
            this.capacity = capacity;
            this.refreshAmount = refreshAmount;
            this.refreshIntervalTicks = refreshIntervalTicks;
            this.selectedThreshold = selectedThreshold;
        }

        public static SettlementStockEffectiveSettings For(SettlementStockItemReference reference, TechLevel factionTechLevel)
        {
            SettlementStockItem item = reference.item;
            SettlementStockItemTier bestTier = null;

            if (!item.techLevelTiers.NullOrEmpty())
            {
                foreach (SettlementStockItemTier tier in item.techLevelTiers)
                {
                    if ((int)tier.minFactionTechLevel > (int)factionTechLevel) continue;
                    if (bestTier == null || (int)tier.minFactionTechLevel > (int)bestTier.minFactionTechLevel)
                        bestTier = tier;
                }
            }

            return bestTier != null
                ? new SettlementStockEffectiveSettings(bestTier.capacity, bestTier.refreshAmount, bestTier.refreshIntervalTicks, bestTier.minFactionTechLevel)
                : new SettlementStockEffectiveSettings(item.capacity, item.refreshAmount, item.refreshIntervalTicks, item.minFactionTechLevel);
        }
    }
}

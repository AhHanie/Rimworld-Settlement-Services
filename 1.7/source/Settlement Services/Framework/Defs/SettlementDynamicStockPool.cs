using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Settlement_Services.Framework.Defs
{
    public class SettlementDynamicStockPool
    {
        public string id;
        public string thingCategoryDefName;
        public List<string> thingDefNames = new List<string>();
        public List<string> excludeThingDefNames = new List<string>();
        public bool requireHumanEdible;
        public bool requireNutritionGiving;
        public bool excludeDrugs;
        public bool allowPlayerSellOnlyItems;
        public TechLevel minFactionTechLevel = TechLevel.Undefined;
        public int minDifferentGoods = 1;
        public int maxDifferentGoods = 1;
        public int minCapacity = 10;
        public int maxCapacity = 10;
        public int minRefreshAmount = 1;
        public int maxRefreshAmount = 1;
        public int refreshIntervalTicks = 60000;
        public int replaceCount = 1;
        public List<SettlementDynamicStockPoolTier> techLevelTiers;

        public IEnumerable<string> SettingsErrors(string owner)
        {
            if (id.NullOrEmpty())
                yield return $"{owner} has a dynamic stock pool with an empty id.";

            if (thingCategoryDefName.NullOrEmpty() && thingDefNames.NullOrEmpty())
                yield return $"{owner} must set thingCategoryDefName or thingDefNames.";

            if (minFactionTechLevel == TechLevel.Animal)
                yield return $"{owner} has minFactionTechLevel Animal, which is not a valid stock threshold.";

            foreach (string e in RangeErrors(owner, minDifferentGoods, maxDifferentGoods, minCapacity, maxCapacity, minRefreshAmount, maxRefreshAmount, refreshIntervalTicks, replaceCount))
                yield return e;

            if (techLevelTiers.NullOrEmpty()) yield break;

            int baseFloor = minFactionTechLevel == TechLevel.Undefined
                ? (int)TechLevel.Animal
                : (int)minFactionTechLevel;
            bool havePreviousThreshold = false;
            int previousThreshold = 0;

            for (int i = 0; i < techLevelTiers.Count; i++)
            {
                SettlementDynamicStockPoolTier tier = techLevelTiers[i];
                string tierOwner = $"{owner} techLevelTiers[{i}]";
                if (tier == null)
                {
                    yield return $"{tierOwner} is null.";
                    continue;
                }

                if (tier.minFactionTechLevel == TechLevel.Undefined || tier.minFactionTechLevel == TechLevel.Animal)
                    yield return $"{tierOwner} has minFactionTechLevel {tier.minFactionTechLevel}, which is not a valid tier threshold.";
                else if ((int)tier.minFactionTechLevel <= baseFloor)
                    yield return $"{tierOwner} minFactionTechLevel {tier.minFactionTechLevel} must be strictly greater than the base minFactionTechLevel.";
                else if (havePreviousThreshold && (int)tier.minFactionTechLevel <= previousThreshold)
                    yield return $"{tierOwner} minFactionTechLevel {tier.minFactionTechLevel} must be strictly greater than the previous tier's threshold.";

                foreach (string e in RangeErrors(tierOwner, tier.minDifferentGoods, tier.maxDifferentGoods, tier.minCapacity, tier.maxCapacity, tier.minRefreshAmount, tier.maxRefreshAmount, tier.refreshIntervalTicks, tier.replaceCount))
                    yield return e;

                previousThreshold = (int)tier.minFactionTechLevel;
                havePreviousThreshold = true;
            }
        }

        public IEnumerable<string> SourceErrors(string owner)
        {
            if (!thingCategoryDefName.NullOrEmpty() && DefDatabase<ThingCategoryDef>.GetNamedSilentFail(thingCategoryDefName) == null)
                yield return $"{owner} references unknown ThingCategoryDef {thingCategoryDefName}.";

            if (!excludeThingDefNames.NullOrEmpty() && excludeThingDefNames.Any(n => n.NullOrEmpty()))
                yield return $"{owner} excludeThingDefNames has an empty entry.";

            if (thingDefNames.NullOrEmpty()) yield break;

            var seenNames = new HashSet<string>();
            foreach (string name in thingDefNames)
            {
                if (name.NullOrEmpty())
                {
                    yield return $"{owner} thingDefNames has an empty entry.";
                    continue;
                }
                if (!seenNames.Add(name))
                    yield return $"{owner} thingDefNames has a duplicate entry {name}.";
                if (DefDatabase<ThingDef>.GetNamedSilentFail(name) == null)
                    yield return $"{owner} thingDefNames references unknown ThingDef {name}.";
            }
        }

        private static IEnumerable<string> RangeErrors(string owner, int minGoods, int maxGoods, int minCap, int maxCap, int minRefresh, int maxRefresh, int intervalTicks, int replace)
        {
            if (minGoods < 1)
                yield return $"{owner} has an invalid minDifferentGoods.";
            if (maxGoods < minGoods)
                yield return $"{owner} has maxDifferentGoods below minDifferentGoods.";
            if (minCap <= 0)
                yield return $"{owner} has an invalid minCapacity.";
            if (maxCap < minCap)
                yield return $"{owner} has maxCapacity below minCapacity.";
            if (minRefresh < 0)
                yield return $"{owner} has an invalid minRefreshAmount.";
            if (maxRefresh < minRefresh)
                yield return $"{owner} has maxRefreshAmount below minRefreshAmount.";
            if (intervalTicks <= 0)
                yield return $"{owner} has an invalid refreshIntervalTicks.";
            if (replace < 0)
                yield return $"{owner} has an invalid replaceCount.";
        }
    }
}

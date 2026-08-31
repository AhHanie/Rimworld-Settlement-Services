using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Settlement_Services.Framework.Defs
{
    public class SettlementStockCategoryDef : Def
    {
        public List<SettlementStockItem> stockItems = new List<SettlementStockItem>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;

            if (stockItems.NullOrEmpty())
            {
                yield return "stockItems must not be empty.";
                yield break;
            }

            var seenNames = new HashSet<string>();
            foreach (SettlementStockItem item in stockItems)
            {
                if (item.thingDefName.NullOrEmpty())
                {
                    yield return "stockItems has an entry with an empty thingDefName.";
                    continue;
                }
                if (!seenNames.Add(item.thingDefName))
                    yield return $"stockItems has a duplicate thingDefName {item.thingDefName}.";
                if (DefDatabase<ThingDef>.GetNamedSilentFail(item.thingDefName) == null)
                    yield return $"stockItems references unknown ThingDef {item.thingDefName}.";
                if (item.capacity <= 0)
                    yield return $"stockItems entry {item.thingDefName} has an invalid capacity.";
                if (item.refreshAmount < 0)
                    yield return $"stockItems entry {item.thingDefName} has an invalid refreshAmount.";
                if (item.refreshIntervalTicks <= 0)
                    yield return $"stockItems entry {item.thingDefName} has an invalid refreshIntervalTicks.";
                if (item.minFactionTechLevel == TechLevel.Animal)
                    yield return $"stockItems entry {item.thingDefName} has minFactionTechLevel Animal, which is not a valid stock threshold.";

                if (item.techLevelTiers.NullOrEmpty()) continue;

                int baseFloor = item.minFactionTechLevel == TechLevel.Undefined
                    ? (int)TechLevel.Animal
                    : (int)item.minFactionTechLevel;
                bool havePreviousThreshold = false;
                int previousThreshold = 0;

                for (int i = 0; i < item.techLevelTiers.Count; i++)
                {
                    SettlementStockItemTier tier = item.techLevelTiers[i];
                    if (tier == null)
                    {
                        yield return $"stockItems entry {item.thingDefName} techLevelTiers[{i}] is null.";
                        continue;
                    }

                    if (tier.minFactionTechLevel == TechLevel.Undefined || tier.minFactionTechLevel == TechLevel.Animal)
                    {
                        yield return $"stockItems entry {item.thingDefName} techLevelTiers[{i}] has minFactionTechLevel {tier.minFactionTechLevel}, which is not a valid tier threshold.";
                    }
                    else if ((int)tier.minFactionTechLevel <= baseFloor)
                    {
                        yield return $"stockItems entry {item.thingDefName} techLevelTiers[{i}] minFactionTechLevel {tier.minFactionTechLevel} must be strictly greater than the base minFactionTechLevel.";
                    }
                    else if (havePreviousThreshold && (int)tier.minFactionTechLevel <= previousThreshold)
                    {
                        yield return $"stockItems entry {item.thingDefName} techLevelTiers[{i}] minFactionTechLevel {tier.minFactionTechLevel} must be strictly greater than the previous tier's threshold.";
                    }

                    if (tier.capacity <= 0)
                        yield return $"stockItems entry {item.thingDefName} techLevelTiers[{i}] has an invalid capacity.";
                    if (tier.refreshAmount < 0)
                        yield return $"stockItems entry {item.thingDefName} techLevelTiers[{i}] has an invalid refreshAmount.";
                    if (tier.refreshIntervalTicks <= 0)
                        yield return $"stockItems entry {item.thingDefName} techLevelTiers[{i}] has an invalid refreshIntervalTicks.";

                    previousThreshold = (int)tier.minFactionTechLevel;
                    havePreviousThreshold = true;
                }
            }
        }
    }
}

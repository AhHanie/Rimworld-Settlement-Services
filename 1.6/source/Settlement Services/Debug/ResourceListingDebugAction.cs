using System;
using System.Linq;
using LudeonTK;
using Verse;

namespace Settlement_Services.Debug
{
    public static class ResourceListingDebugAction
    {
        [DebugAction("Settlement Services", "Log all loaded resource defs", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        private static void LogResourceDefs()
        {
            var resourceDefs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def.CountAsResource)
                .OrderByDescending(def => def.resourceReadoutPriority)
                .ThenBy(def => def.defName, StringComparer.Ordinal)
                .ToList();

            Logger.Message($"{resourceDefs.Count} loaded resource ThingDef(s):");
            foreach (ThingDef def in resourceDefs)
            {
                string categories = def.thingCategories.NullOrEmpty()
                    ? "none"
                    : string.Join(",", def.thingCategories.Select(c => c.defName));
                string source = def.modContentPack?.PackageId ?? (def.generated ? "generated" : "unknown");

                Logger.Message($"{def.defName} | label={def.label} | priority={def.resourceReadoutPriority} | categories=[{categories}] | source={source}");
            }
        }
    }
}

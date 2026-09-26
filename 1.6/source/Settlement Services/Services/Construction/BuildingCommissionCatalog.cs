using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Services.Crafting;

namespace Settlement_Services.Services.Construction
{
    public static class BuildingCommissionCatalog
    {
        private static HashSet<string> _excludedBuildingDefNames;

        private static HashSet<string> ExcludedBuildingDefNames
        {
            get
            {
                if (_excludedBuildingDefNames == null)
                {
                    _excludedBuildingDefNames = new HashSet<string>();
                    foreach (BuildingCommissionExclusionsDef def in DefDatabase<BuildingCommissionExclusionsDef>.AllDefsListForReading)
                    {
                        foreach (string name in def.excludedBuildingDefNames)
                        {
                            if (!name.NullOrEmpty()) _excludedBuildingDefNames.Add(name);
                        }
                    }
                }
                return _excludedBuildingDefNames;
            }
        }

        public static bool IsEligibleBuildingDef(ThingDef building) =>
            building != null
            && building.category == ThingCategory.Building
            && building.BuildableByPlayer
            && building.Minifiable
            && !ExcludedBuildingDefNames.Contains(building.defName);

        public static bool IsEligibleAtSettlement(ThingDef building, Settlement settlement)
        {
            if (!IsEligibleBuildingDef(building) || settlement?.Faction?.def == null) return false;
            return (int)building.techLevel <= (int)settlement.Faction.def.techLevel;
        }

        public static IEnumerable<ThingDef> EligibleBuildings(Settlement settlement)
        {
            if (settlement?.Faction == null) yield break;

            foreach (ThingDef building in DefDatabase<ThingDef>.AllDefsListForReading
                         .Where(d => IsEligibleAtSettlement(d, settlement))
                         .OrderBy(d => d.label, StringComparer.Ordinal)
                         .ThenBy(d => d.defName, StringComparer.Ordinal))
            {
                yield return building;
            }
        }

        public static IEnumerable<ThingDef> EligibleStuffs(ThingDef building, Settlement settlement) =>
            settlement?.Faction?.def == null
                ? Enumerable.Empty<ThingDef>()
                : CraftingMaterialPlanner.EligibleStuffs(building, settlement.Faction.def.techLevel, settlement);
    }
}

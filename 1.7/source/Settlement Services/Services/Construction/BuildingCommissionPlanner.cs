using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.Services.Construction
{
    internal static class BuildingCommissionPlanner
    {
        private const float WorkDurationMultiplier = 2.5f;
        private const string UnavailableKey = "SettlementServices.Error.BuildingNoLongerAvailable";

        public static bool TryPlan(Settlement settlement, List<BuildingCommissionLine> lines, out BuildingCommissionPlan plan, out string errorKey)
        {
            plan = null;

            if (lines.NullOrEmpty())
            {
                errorKey = "SettlementServices.Error.NoConstructionItemsSelected";
                return false;
            }

            var aggregatedMaterials = new Dictionary<ThingDef, int>();
            var frozenLines = new List<BuildingCommissionLine>();
            float totalWork = 0f;

            foreach (BuildingCommissionLine line in lines)
            {
                if (line == null || line.count <= 0)
                {
                    errorKey = "SettlementServices.Error.InvalidQuantity";
                    return false;
                }

                ThingDef building = DefDatabase<ThingDef>.GetNamedSilentFail(line.buildingDefName);
                if (!BuildingCommissionCatalog.IsEligibleAtSettlement(building, settlement))
                {
                    errorKey = UnavailableKey;
                    return false;
                }

                ThingDef stuff = null;
                if (building.MadeFromStuff)
                {
                    stuff = line.stuffDefName != null ? DefDatabase<ThingDef>.GetNamedSilentFail(line.stuffDefName) : null;
                    if (stuff == null || !BuildingCommissionCatalog.EligibleStuffs(building, settlement).Contains(stuff))
                    {
                        errorKey = UnavailableKey;
                        return false;
                    }
                }
                else if (line.stuffDefName != null)
                {
                    errorKey = UnavailableKey;
                    return false;
                }

                foreach (ThingDefCountClass cost in building.CostListAdjusted(stuff))
                {
                    aggregatedMaterials.TryGetValue(cost.thingDef, out int existing);
                    aggregatedMaterials[cost.thingDef] = existing + cost.count * line.count;
                }

                totalWork += building.GetStatValueAbstract(StatDefOf.WorkToBuild, stuff) * line.count;
                frozenLines.Add(line.Clone());
            }

            plan = new BuildingCommissionPlan
            {
                lines = frozenLines,
                rawMaterials = aggregatedMaterials
                    .Select(kv => new CraftingMaterialRequirement(kv.Key.defName, kv.Value, true))
                    .OrderBy(m => m.thingDefName, System.StringComparer.Ordinal)
                    .ToList(),
                workTicks = Mathf.Max(1, Mathf.CeilToInt(totalWork * WorkDurationMultiplier)),
            };
            errorKey = null;
            return true;
        }

        public static List<ServiceStockRequirement> ToStockRequirements(BuildingCommissionPlan plan) =>
            plan.rawMaterials
                .Select(m => new ServiceStockRequirement { thingDefName = m.thingDefName, amount = m.amount, playerCanSupply = m.playerCanSupply })
                .ToList();
    }
}

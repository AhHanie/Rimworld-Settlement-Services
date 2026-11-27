using System;
using System.Linq;
using RimWorld;
using Verse;

namespace Settlement_Services.Services.Land
{
    public sealed class GenStep_SettlementLand : GenStep
    {
        public override int SeedPart => 162961209;

        public override void Generate(Map map, GenStepParams parms)
        {
            TerrainDef paving = DefDatabase<TerrainDef>.GetNamed("PavedTile");
            CellRect arrival = SettlementLandMapParent.ArrivalArea(map);
            int xRadius = Math.Max(2, map.Size.x / 5);
            int zRadius = Math.Max(2, map.Size.z / 5);
            CellRect clearing = CellRect.CenteredOn(map.Center, 2 * xRadius + 1, 2 * zRadius + 1).ClipInsideMap(map);
            TerrainDef dryTerrain = map.Biome.terrainsByFertility.Select(threshold => threshold.terrain)
                .FirstOrDefault(Buildable) ?? TerrainDefOf.Soil;

            // Natural hills and wetlands can cover a small map. Keep an open building site around the arrival cells.
            foreach (IntVec3 cell in clearing)
                map.roofGrid.SetRoof(cell, null);
            foreach (IntVec3 cell in clearing)
            {
                bool reserved = arrival.Contains(cell);
                foreach (Thing thing in cell.GetThingList(map).ToList())
                    if (thing.def.passability != Traversability.Standable || (reserved && thing is Plant))
                        thing.Destroy(DestroyMode.Vanish);
                if (map.terrainGrid.TempTerrainAt(cell) != null)
                    map.terrainGrid.RemoveTempTerrain(cell, preventDestroyEffects: true);
                if (!Buildable(map.terrainGrid.TerrainAt(cell)))
                    map.terrainGrid.SetTerrain(cell, dryTerrain);
                if (reserved)
                    map.terrainGrid.SetTerrain(cell, paving);
            }

            map.fogGrid.ClearAllFog();
            MapGenerator.PlayerStartSpot = map.Center;
            SettlementLandMapParent.For(map).terrainGenerated = true;
        }

        private static bool Buildable(TerrainDef terrain) => terrain != null && !terrain.IsWater
            && terrain.passability == Traversability.Standable && terrain.affordances.Contains(TerrainAffordanceDefOf.Heavy);
    }
}

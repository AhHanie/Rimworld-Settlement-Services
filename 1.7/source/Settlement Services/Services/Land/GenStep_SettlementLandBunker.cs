using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Settlement_Services.Services.Land
{
    public sealed class GenStep_SettlementLandBunker : GenStep
    {
        public override int SeedPart => 194036739;

        public override void Generate(Map map, GenStepParams parms)
        {
            CellRect arrival = SettlementLandMapParent.ArrivalArea(map);
            TerrainDef stone = ThingDefOf.Granite.building.leaveTerrain;
            CellRect interior = CellRect.WholeMap(map).ContractedBy(1);
            foreach (IntVec3 cell in map.AllCells)
            {
                map.terrainGrid.SetTerrain(cell, stone);
                if (interior.Contains(cell))
                    map.terrainGrid.SetTerrain(cell, arrival.Contains(cell) ? TerrainDefOf.PavedTile : TerrainDefOf.Concrete);
            }

            foreach (IntVec3 cell in CellRect.WholeMap(map).EdgeCells)
                SpawnRoofSupport(ThingMaker.MakeThing(ThingDefOf.Granite), cell, map);

            // The rim alone supports square plots up to 14 cells wide. With a nine-cell
            // spacing, the farthest cell between columns is within the 6.9-cell support radius.
            if (map.Size.x > 14 || map.Size.z > 14)
            {
                foreach (int x in SupportCoordinates(map.Center.x, map.Size.x))
                {
                    foreach (int z in SupportCoordinates(map.Center.z, map.Size.z))
                    {
                        Thing column = ThingMaker.MakeThing(ThingDefOf.Column, ThingDefOf.BlocksGranite);
                        column.SetFaction(Faction.OfPlayer);
                        SpawnRoofSupport(column, new IntVec3(x, 0, z), map);
                    }
                }
            }

            map.fogGrid.ClearAllFog();
            MapGenerator.PlayerStartSpot = map.Center;
            foreach (IntVec3 cell in arrival)
            {
                if (!cell.Standable(map) || !cell.Roofed(map) || cell.GetTerrain(map) != TerrainDefOf.PavedTile)
                    throw new InvalidOperationException($"Bunker arrival cell {cell} is incomplete.");
            }
            SettlementLandMapParent.For(map).terrainGenerated = true;
        }

        private static IEnumerable<int> SupportCoordinates(int center, int size)
        {
            for (int offset = 4; center - offset >= 3 || center + offset <= size - 4; offset += 9)
            {
                if (center - offset >= 3) yield return center - offset;
                if (center + offset <= size - 4) yield return center + offset;
            }
        }

        private static void SpawnRoofSupport(Thing support, IntVec3 cell, Map map)
        {
            GenSpawn.Spawn(support, cell, map);
            if (cell.GetEdifice(map) != support || !support.def.holdsRoof)
                throw new InvalidOperationException($"Bunker roof support at {cell} did not spawn.");
        }
    }
}

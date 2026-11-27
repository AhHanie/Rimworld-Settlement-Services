using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Settlement_Services.Services.Land
{
    internal static class LandPlotEnvironment
    {
        private static bool IsBunker(Map map) => map?.generatorDef?.defName == LandPlotService.BunkerGeneratorName
            && SettlementLandMapParent.IsPlot(map);

        internal static void Prepare(Map map)
        {
            var parent = SettlementLandMapParent.For(map);
            if (parent == null) return;
            if (IsBunker(map))
            {
                map.pocketTileInfo ??= new Tile { PrimaryBiome = map.generatorDef.pocketMapProperties.biome };
                return;
            }
            if (Find.WorldGrid == null) return;

            PlanetTile tile = parent.departureTile;
            if (!LandPlotTravel.TileExists(tile))
            {
                map.pocketTileInfo = new Tile { PrimaryBiome = map.generatorDef.pocketMapProperties.biome };
                return;
            }

            Tile source = Find.WorldGrid[tile];
            // The pocket owns this snapshot. Its map and world object keep their hidden tile.
            map.pocketTileInfo = new SurfaceTile(tile)
            {
                PrimaryBiome = source.PrimaryBiome,
                elevation = source.elevation,
                hilliness = source.HillinessForElevationGen,
                temperature = source.temperature,
                rainfall = source.rainfall,
                swampiness = source.swampiness
            };
        }

        private static bool TryGetTile(Map map, out PlanetTile tile)
        {
            tile = SettlementLandMapParent.For(map)?.departureTile ?? PlanetTile.Invalid;
            return LandPlotTravel.TileExists(tile);
        }

        internal static void InstallTileHooks(Harmony harmony, List<string> failures)
        {
            var tileGetter = new InnerTarget(AccessTools.Property(typeof(Map), nameof(Map.Tile)), InnerTargetKind.Getter);
            var tilePatch = new HarmonyMethod(typeof(LandPlotEnvironment), nameof(UseClimateTile)) { innerTarget = tileGetter };

            void PatchTileRead(Type type, string name, params Type[] parameters)
            {
                try
                {
                    var original = AccessTools.Method(type, name, parameters) ?? throw new MissingMethodException(type.FullName, name);
                    harmony.CreateProcessor(original).AddInnerPostfix(tilePatch).Patch();
                }
                catch (Exception ex)
                {
                    failures.Add(type.Name + "." + name);
                    SupportLog.Error($"Settlement land could not redirect the climate tile in {type.Name}.{name}: {ex}");
                }
            }

            PatchTileRead(typeof(RockNoises), nameof(RockNoises.Init), typeof(Map));
            PatchTileRead(typeof(GenStep_RockChunks), "GrowLowRockFormationFrom", typeof(IntVec3), typeof(Map));
            PatchTileRead(typeof(GenStep_Snow), nameof(GenStep_Snow.Generate), typeof(Map), typeof(GenStepParams));
            PatchTileRead(typeof(MapPlantGrowthRateCalculator), nameof(MapPlantGrowthRateCalculator.BuildFor), typeof(Map));
            PatchTileRead(typeof(GenCelestial), nameof(GenCelestial.CelestialSunGlow), typeof(Map), typeof(int));
            PatchTileRead(typeof(PlantFallColors), nameof(PlantFallColors.SetFallShaderGlobals), typeof(Map));
            PatchTileRead(typeof(MapTemperature), nameof(MapTemperature.LocalSeasonsAreMeaningful));
            foreach (string name in new[]
            {
                nameof(GenLocalDate.DayOfYear), nameof(GenLocalDate.HourOfDay), nameof(GenLocalDate.DayOfTwelfth),
                nameof(GenLocalDate.Twelfth), nameof(GenLocalDate.Season), nameof(GenLocalDate.Year),
                nameof(GenLocalDate.DayOfSeason), nameof(GenLocalDate.DayOfQuadrum), nameof(GenLocalDate.DayTick),
                nameof(GenLocalDate.DayPercent), nameof(GenLocalDate.YearPercent), nameof(GenLocalDate.HourInteger),
                nameof(GenLocalDate.HourFloat)
            })
                PatchTileRead(typeof(GenLocalDate), name, typeof(Map));
        }

        private static void UseClimateTile(Map __instance, ref PlanetTile __result)
        {
            if (TryGetTile(__instance, out PlanetTile tile)) __result = tile;
        }

        internal static void OutdoorTemperature(Map map, ref float result)
        {
            if (!IsBunker(map) && TryGetTile(map, out PlanetTile tile))
                result = map.Biome.constantOutdoorTemperature ?? Find.World.tileTemperatures.GetOutdoorTemp(tile);
        }

        internal static void SeasonalTemperature(Map map, ref float result)
        {
            if (!IsBunker(map) && TryGetTile(map, out PlanetTile tile))
                result = map.Biome.constantOutdoorTemperature ?? Find.World.tileTemperatures.GetSeasonalTemp(tile);
        }

        internal static void ThingLocation(Thing thing, ref Vector2 result)
        {
            if (TryGetTile(thing.MapHeld, out PlanetTile tile)) result = Find.WorldGrid.LongLatOf(tile);
        }
    }
}

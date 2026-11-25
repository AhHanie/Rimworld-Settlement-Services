using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain;
using Settlement_Services.UI;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Settlement_Services.Services.Land
{
    internal static class LandPlotPatches
    {
        private static AccessTools.FieldRef<Dialog_FormCaravan, Map> dialogMap;
        private static AccessTools.FieldRef<Plant, int> leaflessSince;
        private static Func<Dialog_FormCaravan, bool> reformCaravan;
        internal static bool Ready { get; private set; }
        internal static bool CanPack { get; private set; }

        internal static void Install()
        {
            var harmony = new Harmony("sk.settlementservices.land");
            var failures = new List<string>();
            void Patch(Type type, string target, string patch, bool postfix = false, Type[] signature = null)
            {
                try
                {
                    MethodInfo original = AccessTools.Method(type, target, signature)
                        ?? throw new MissingMethodException(type.FullName, target);
                    var method = new HarmonyMethod(typeof(LandPlotPatches), patch);
                    if (postfix) harmony.Patch(original, postfix: method);
                    else harmony.Patch(original, prefix: method);
                }
                catch (Exception ex)
                {
                    failures.Add(type.Name + "." + target);
                    SupportLog.Error($"Settlement land could not patch {type.Name}.{target}: {ex}");
                }
            }

            Patch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls), nameof(ShowDeparture), true);
            Patch(typeof(Caravan), nameof(Caravan.GetGizmos), nameof(ShowRecovery), true);
            InstallUiInfixes(harmony);
            try
            {
                dialogMap = AccessTools.FieldRefAccess<Dialog_FormCaravan, Map>("map");
                reformCaravan = AccessTools.Method(typeof(Dialog_FormCaravan), "TryReformCaravan")
                    .CreateDelegate<Func<Dialog_FormCaravan, bool>>();
            }
            catch (Exception ex)
            {
                failures.Add("Dialog_FormCaravan");
                SupportLog.Error($"Settlement land could not bind the caravan packing dialog: {ex}");
            }
            Patch(typeof(Dialog_FormCaravan), "get_CurrentTile", nameof(DepartureTile), true);
            Patch(typeof(Dialog_FormCaravan), nameof(Dialog_FormCaravan.PostOpen), nameof(PrepareDeparture));
            Patch(typeof(WorldRoutePlanner), nameof(WorldRoutePlanner.Start), nameof(ChooseRoute),
                signature: new[] { typeof(Dialog_FormCaravan) });
            Patch(typeof(Dialog_FormCaravan), "TryReformCaravan", nameof(CheckDeparture));
            Patch(typeof(Dialog_FormCaravan), "DebugTryFormCaravanInstantly", nameof(DebugDeparture));
            Patch(typeof(CaravanExitMapUtility), nameof(CaravanExitMapUtility.ExitMapAndCreateCaravan), nameof(PreserveCarriedThings),
                signature: new[] { typeof(IEnumerable<Pawn>), typeof(Faction), typeof(PlanetTile), typeof(PlanetTile),
                    typeof(PlanetTile), typeof(bool) });
            CanPack = failures.Count == 0;

            Patch(typeof(Map), nameof(Map.MapPreTick), nameof(MapTick));
            Patch(typeof(Map), nameof(Map.MapPostTick), nameof(MapTick));
            Patch(typeof(Thing), nameof(Thing.DoTick), nameof(ThingTick));
            Patch(typeof(Plant), nameof(Plant.TickLong), nameof(ThingTick));
            try
            {
                leaflessSince = AccessTools.FieldRefAccess<Plant, int>("madeLeaflessTick");
            }
            catch (Exception ex)
            {
                failures.Add("Plant.madeLeaflessTick");
                SupportLog.Error($"Settlement land could not bind the plant recovery timer: {ex}");
            }
            Patch(typeof(Plant), "get_LeaflessNow", nameof(FreezeLeafless), true);
            Patch(typeof(Pawn), nameof(Pawn.SpawnSetup), nameof(InvalidateSpawn), true);
            Patch(typeof(Pawn), nameof(Pawn.DeSpawn), nameof(InvalidatePawn));
            Patch(typeof(Pawn), nameof(Pawn.Kill), nameof(InvalidatePawn));
            Patch(typeof(ThingOwner), "NotifyAdded", nameof(InvalidateHolder), true);
            Patch(typeof(ThingOwner), "NotifyRemoved", nameof(InvalidateHolder), true);
            Patch(typeof(Storyteller), "get_AllIncidentTargets", nameof(FilterIncidentTargets), true);
            Patch(typeof(IncidentWorker), nameof(IncidentWorker.CanFireNow), nameof(AllowIncident));
            Patch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute), nameof(AllowIncident));
            Patch(typeof(StorytellerUtility), nameof(StorytellerUtility.DefaultThreatPointsNow), nameof(AllowThreatPoints));
            Patch(typeof(GameCondition), nameof(GameCondition.CanApplyOnMap), nameof(AllowMapEffect));
            Patch(typeof(GameConditionManager), nameof(GameConditionManager.RegisterCondition), nameof(AllowCondition));
            Patch(typeof(WildAnimalSpawner), nameof(WildAnimalSpawner.WildAnimalSpawnerTick), nameof(AllowMapField));
            Patch(typeof(WildPlantSpawner), nameof(WildPlantSpawner.WildPlantSpawnerTick), nameof(AllowMapField));
            Patch(typeof(Map), nameof(Map.ConstructComponents), nameof(PrepareClimate));
            Patch(typeof(Map), nameof(Map.FinalizeLoading), nameof(PrepareClimate));
            Patch(typeof(MapTemperature), "get_OutdoorTemp", nameof(OutdoorTemperature), true);
            Patch(typeof(MapTemperature), "get_SeasonalTemp", nameof(SeasonalTemperature), true);
            Patch(typeof(GenLocalDate), "LocationForDate", nameof(ThingLocation), true);
            LandPlotEnvironment.InstallTileHooks(harmony, failures);
            Patch(typeof(Scenario), nameof(Scenario.PostMapGenerate), nameof(AllowMapEffect));
            Patch(typeof(MapDrawer), nameof(MapDrawer.Dispose), nameof(DisposeMapDrawer));
            Patch(typeof(PassingShipManager), nameof(PassingShipManager.AddShip), nameof(AllowMapField));
            Patch(typeof(GenGrid), nameof(GenGrid.InNoZoneEdgeArea), nameof(AllowSmallZones), true);
            Patch(typeof(GenConstruct), nameof(GenConstruct.CanPlaceBlueprintAt_NewTemp), nameof(CheckBlueprint));
            Patch(typeof(GenConstruct), nameof(GenConstruct.CanConstruct), nameof(AllowConstruction),
                signature: new[] { typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool), typeof(JobDef) });
            Patch(typeof(JobDriver), nameof(JobDriver.DriverTick), nameof(CheckConstructionJob));
            Patch(typeof(JobDriver), nameof(JobDriver.DriverTickInterval), nameof(CheckConstructionJob));
            foreach (Type type in new[] { typeof(Designator_ZoneAdd), typeof(Designator_AreaBuildRoof),
                typeof(Designator_AreaNoRoof), typeof(Designator_RemoveFloor), typeof(Designator_Smooth) })
                Patch(type, nameof(Designator.CanDesignateCell), nameof(CheckDesignation));
            Patch(typeof(TickManager), nameof(TickManager.DoSingleTick), nameof(ProcessTransitions), true);

            Ready = failures.Count == 0;
            if (!Ready)
                Log.Error("[Settlement Services] Settlement land cannot sell or open plots because required patches failed: "
                    + string.Join(", ", failures));
        }

        private static void InstallUiInfixes(Harmony harmony)
        {
            Install(typeof(ExpandableWorldObjectsUtility), nameof(ExpandableWorldObjectsUtility.ExpandableWorldObjectsOnGUI),
                nameof(HighlightOccupiedPlot), () => new InnerTarget(
                    AccessTools.Property(typeof(MapParent), nameof(MapParent.HasMap)), InnerTargetKind.Getter));
            Install(typeof(ColonistBar), "CheckRecacheEntries", nameof(HideVacantPlots),
                () => new InnerTarget(AccessTools.Method(typeof(List<Map>), nameof(List<Map>.AddRange))));

            void Install(Type type, string name, string postfix, Func<InnerTarget> target)
            {
                try
                {
                    var original = AccessTools.Method(type, name);
                    var patch = new HarmonyMethod(typeof(LandPlotPatches), postfix) { innerTarget = target() };
                    harmony.CreateProcessor(original).AddInnerPostfix(patch).Patch();
                }
                catch (Exception ex)
                {
                    SupportLog.Error($"Settlement land could not patch {type.Name}.{name}: {ex}");
                }
            }
        }

        private static void HideVacantPlots(List<Map> __instance) =>
            __instance.RemoveAll(SettlementLandMapParent.Frozen);

        private static void HighlightOccupiedPlot(MapParent __instance, ref bool __result)
        {
            if (!__result && __instance is Settlement host && LandPlotCommands.ViewableMap(host) is Map plot)
                __result = !SettlementLandMapParent.Frozen(plot);
        }

        internal static bool IsPacking(Map map) => map != null && dialogMap != null
            && Find.WindowStack?.Windows.OfType<Dialog_FormCaravan>().Any(dialog => dialogMap(dialog) == map) == true;

        private static void ShowDeparture(WidgetRow row, bool worldView)
        {
            Map map = Find.CurrentMap;
            if (worldView || !SettlementLandMapParent.IsPlot(map)) return;
            string tooltip = (LandPlotService.Key + "LeaveDesc").Translate();
            if (row.ButtonIcon(ServiceUITextures.LandExit, tooltip)) LandPlotCommands.OpenDeparture(map);
        }

        private static IEnumerable<Gizmo> ShowRecovery(IEnumerable<Gizmo> __result, Caravan __instance)
        {
            foreach (Gizmo gizmo in __result) yield return gizmo;
            foreach (Gizmo gizmo in LandPlotCommands.RecoveryGizmos(__instance)) yield return gizmo;
        }

        private static void DepartureTile(Map ___map, ref PlanetTile __result)
        {
            var parent = SettlementLandMapParent.For(___map);
            if (parent != null) __result = LandPlotTravel.RecoveryTile(parent.departureTile);
        }

        private static void PrepareDeparture(Map ___map, ref bool ___canChooseRoute)
        {
            if (SettlementLandMapParent.IsPlot(___map)) ___canChooseRoute = false;
        }

        private static bool ChooseRoute(Dialog_FormCaravan formCaravanDialog) =>
            dialogMap == null || !SettlementLandMapParent.IsPlot(dialogMap(formCaravanDialog));

        private static bool CheckDeparture(Dialog_FormCaravan __instance, Map ___map)
        {
            if (!SettlementLandMapParent.IsPlot(___map)) return true;
            if (!__instance.CurrentTile.Valid)
            {
                LandPlotService.Reject(LandPlotService.Key + "NoDepartureTile");
                return false;
            }
            if (__instance.MassUsage <= __instance.MassCapacity) return true;
            LandPlotService.Reject("TooBigCaravanMassUsage");
            return false;
        }

        private static bool DebugDeparture(Dialog_FormCaravan __instance, Map ___map, ref bool __result)
        {
            if (!SettlementLandMapParent.IsPlot(___map)) return true;
            __result = reformCaravan != null && reformCaravan(__instance);
            return false;
        }

        private static void PreserveCarriedThings(IEnumerable<Pawn> pawns)
        {
            if (pawns is not List<Pawn> party) return;
            foreach (Pawn pawn in party)
            {
                if (!SettlementLandMapParent.IsPlot(pawn.MapHeld)) continue;
                Thing carried = pawn.carryTracker?.CarriedThing;
                if (carried == null || (carried is Pawn dependent && party.Contains(dependent))) continue;
                if (!pawn.carryTracker.TryDropCarriedThing(pawn.PositionHeld, ThingPlaceMode.Near, out _) && carried is not Pawn)
                    pawn.inventory.innerContainer.TryAddOrTransfer(carried, false);
            }
        }

        private static bool MapTick(Map __instance) => !SettlementLandMapParent.Frozen(__instance);
        private static bool ThingTick(Thing __instance) => !SettlementLandMapParent.Frozen(__instance.MapHeld);
        private static void FreezeLeafless(Plant __instance, int ___madeLeaflessTick, ref bool __result)
        {
            var parent = SettlementLandMapParent.For(__instance.MapHeld);
            if (parent != null) __result = (long)parent.PlantClock(Find.TickManager.TicksGame) - ___madeLeaflessTick < GenDate.TicksPerDay;
        }

        internal static void ResumeLeaflessPlants(Map map, int vacantSince, int now)
        {
            if (leaflessSince == null || now <= vacantSince) return;
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant))
            {
                if (thing is not Plant plant) continue;
                ref int since = ref leaflessSince(plant);
                if (since >= 0 && (long)vacantSince - since < GenDate.TicksPerDay) since += now - vacantSince;
            }
        }

        private static void InvalidateSpawn(Map map) => SettlementLandMapParent.For(map)?.InvalidateOccupancy();
        private static void InvalidatePawn(Pawn __instance) => InvalidateSpawn(__instance.MapHeld);
        private static void InvalidateHolder(ThingOwner __instance, Thing item)
        {
            if (item is not Pawn && item is not IThingHolder) return;
            if (__instance.Owner is not Map)
                InvalidateSpawn(ThingOwnerUtility.GetRootMap(__instance.Owner));
        }

        private static void FilterIncidentTargets(List<IIncidentTarget> __result) =>
            __result.RemoveAll(IsPlotTarget);

        private static bool IsPlotTarget(IIncidentTarget target) =>
            target is Map map && SettlementLandMapParent.IsPlot(map);

        private static bool AllowIncident(IncidentParms parms) => !IsPlotTarget(parms?.target);
        private static bool AllowThreatPoints(IIncidentTarget target) => !IsPlotTarget(target);
        private static bool AllowMapEffect(Map map) => !SettlementLandMapParent.IsPlot(map);
        private static bool AllowCondition(GameConditionManager __instance) => AllowMapEffect(__instance.ownerMap);
        private static bool AllowMapField(Map ___map) => AllowMapEffect(___map);
        private static void PrepareClimate(Map __instance) => LandPlotEnvironment.Prepare(__instance);
        private static void OutdoorTemperature(Map ___map, ref float __result) =>
            LandPlotEnvironment.OutdoorTemperature(___map, ref __result);
        private static void SeasonalTemperature(Map ___map, ref float __result) =>
            LandPlotEnvironment.SeasonalTemperature(___map, ref __result);
        private static void ThingLocation(Thing thing, ref Vector2 __result) =>
            LandPlotEnvironment.ThingLocation(thing, ref __result);

        private static bool DisposeMapDrawer(Map ___map, Section[,] ___sections) =>
            ___sections != null || !SettlementLandMapParent.IsPlot(___map);

        private static void AllowSmallZones(Map map, ref bool __result)
        {
            if (SettlementLandMapParent.IsPlot(map)) __result = false;
        }

        private static bool CheckBlueprint(BuildableDef entDef, IntVec3 center, Rot4 rot, Map map, ref AcceptanceReport __result)
        {
            if (!SettlementLandMapParent.IsPlot(map)) return true;
            if (SettlementLandMapParent.RecoveryOrdersOnly(map))
            {
                __result = (LandPlotService.Key + "RecoveryOrders").Translate();
                return false;
            }
            if (entDef is ThingDef def && def.passability != Traversability.Standable
                && GenAdj.OccupiedRect(center, rot, def.Size).Overlaps(SettlementLandMapParent.ArrivalArea(map)))
            {
                __result = (LandPlotService.Key + "KeepArrivalClear").Translate();
                return false;
            }
            return true;
        }

        private static bool AllowConstruction(Thing t) => !SettlementLandMapParent.RecoveryOrdersOnly(t.MapHeld);

        private static bool CheckDesignation(Designator __instance, ref AcceptanceReport __result)
        {
            if (!SettlementLandMapParent.RecoveryOrdersOnly(Find.CurrentMap)) return true;
            __result = (LandPlotService.Key + "RecoveryOrders").Translate();
            return false;
        }

        private static bool CheckConstructionJob(JobDriver __instance)
        {
            JobDef def = __instance.job?.def;
            if (def == null || (def != JobDefOf.FinishFrame && def != JobDefOf.PlaceNoCostFrame
                && def != JobDefOf.BuildRoof && def != JobDefOf.RemoveRoof && def != JobDefOf.RemoveFloor
                && def != JobDefOf.SmoothFloor && def != JobDefOf.SmoothWall)
                || !SettlementLandMapParent.RecoveryOrdersOnly(__instance.pawn.MapHeld)) return true;
            if (__instance.pawn.jobs.curDriver == __instance)
                __instance.pawn.jobs.EndCurrentJob(JobCondition.Incompletable, startNewJob: false);
            return false;
        }

        private static void ProcessTransitions() =>
            SettlementServicesWorldComponent.Current?.ProcessLandTransitions();
    }
}

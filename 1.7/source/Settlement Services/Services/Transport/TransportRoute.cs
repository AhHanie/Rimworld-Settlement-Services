using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Framework.Delivery;
using Settlement_Services.Framework.Pricing;
using UnityEngine;
using Verse;

namespace Settlement_Services.Services.Transport
{
    internal enum TransportMode { Pod, Shuttle }

    internal readonly record struct TransportDestination(PlanetTile Tile, int MapParentId, IntVec3 Cell)
    {
        internal static TransportDestination None => World(PlanetTile.Invalid);
        internal static TransportDestination World(PlanetTile tile) => new TransportDestination(tile, -1, IntVec3.Invalid);
        internal bool IsLocal => MapParentId >= 0;
    }

    internal sealed record TransportSpecs(int BaseFare, float Capacity, float FuelCapacity, float FuelRate,
        float MinimumFuel, int FixedRange, WorldObjectDef Flight, WorldObjectDef Marker, ThingDef Hull,
        ThingDef Incoming, TransportShipDef Ship, ShipJobDef Unload);

    internal readonly record struct TransportLeg(int Distance, int Range, float Fuel, float LayerFactor);

    internal readonly record struct TransportFare(int Distance, int Range, int BaseFare, int MaterialFare, int WealthFare,
        float Fuel, int FuelFare, float FuelPrice, float LayerFactor)
    {
        internal int Total => BaseFare + MaterialFare + WealthFare + FuelFare;
    }

    internal static class TransportRoute
    {
        private const string Error = TransportBooking.Key + "Error.";
        internal const string InvalidDestination = Error + "Destination";
        internal const string LandingBlocked = Error + "Landing";
        private const int PodBaseFare = 50;
        private const int ShuttleBaseFare = 300;
        private const int MinimumShuttlePremium = 50;
        internal const float MassTolerance = 0.001f;
        internal static readonly Rot4 LandingRotation = Rot4.North;

        internal static string ResolveSpecs(TransportMode mode, TechLevel tech, out TransportSpecs specs)
        {
            specs = null;
            if (mode is not (TransportMode.Pod or TransportMode.Shuttle)) return Error + "VehicleDefs";
            bool shuttle = mode == TransportMode.Shuttle;
            if (shuttle && tech < TechLevel.Spacer) return Error + "ShuttleTech";
            if (shuttle && !ModsConfig.OdysseyActive) return Error + "Odyssey";

            TransportShipDef ship = shuttle ? DefDatabase<TransportShipDef>.GetNamedSilentFail("Ship_PassengerShuttle") : null;
            var charter = shuttle ? DefDatabase<TransportShipDef>.GetNamedSilentFail("SettlementTransport_CharteredShuttle") : null;
            ThingDef hull = shuttle ? ship?.shipThing : ThingDefOf.TransportPod;
            ThingDef fuelSource = shuttle ? hull : DefDatabase<ThingDef>.GetNamedSilentFail("PodLauncher");
            var transporter = hull?.GetCompProperties<CompProperties_Transporter>();
            var launchable = hull?.GetCompProperties<CompProperties_Launchable>();
            var refuelable = fuelSource?.GetCompProperties<CompProperties_Refuelable>();
            string prefix = shuttle ? "SettlementTransport_Shuttle" : "SettlementTransport_Pod";
            var flight = DefDatabase<WorldObjectDef>.GetNamedSilentFail(prefix + "Flight");
            var marker = DefDatabase<WorldObjectDef>.GetNamedSilentFail(prefix + "Marker");
            var incoming = shuttle ? ship?.arrivingSkyfaller : DefDatabase<ThingDef>.GetNamedSilentFail("SettlementTransport_IncomingPod");
            var unload = shuttle ? DefDatabase<ShipJobDef>.GetNamedSilentFail("SettlementTransport_Unload") : null;
            var pod = shuttle ? null : DefDatabase<ThingDef>.GetNamedSilentFail("SettlementTransport_ActivePod");

            if (transporter == null || launchable == null || refuelable == null || flight?.worldObjectClass != typeof(SettlementTransportFlight)
                || marker?.worldObjectClass != typeof(SettlementTransportMarker) || !Positive(transporter.massCapacity)
                || !Positive(refuelable.fuelCapacity) || !Positive(launchable.fuelPerTile) || !float.IsFinite(launchable.minFuelCost)
                || refuelable.fuelCapacity < Mathf.Max(1f, launchable.minFuelCost)) return Error + "VehicleDefs";
            if (shuttle && (hull.thingClass != typeof(Building_PassengerShuttle) || hull.GetCompProperties<CompProperties_Shuttle>() == null
                || incoming?.thingClass != typeof(PassengerShuttleIncoming) || ship.leavingSkyfaller?.thingClass != typeof(PassengerShuttleLeaving)
                || !ship.playerShuttle || ship.worldObject == null || unload?.jobClass != typeof(ShipJob_SettlementTransportUnload)
                || charter?.shipThing != hull || charter?.arrivingSkyfaller?.thingClass != typeof(SettlementTransportIncomingShuttle)
                || charter.leavingSkyfaller != ship.leavingSkyfaller || !charter.playerShuttle)) return Error + "VehicleDefs";
            if (!shuttle && (incoming?.thingClass != typeof(SettlementTransportIncomingPod) || pod?.thingClass != typeof(SettlementTransportPod)))
                return Error + "VehicleDefs";

            specs = new TransportSpecs(shuttle ? ShuttleBaseFare : PodBaseFare, transporter.massCapacity, refuelable.fuelCapacity, launchable.fuelPerTile,
                Mathf.Max(1f, launchable.minFuelCost), launchable.fixedLaunchDistanceMax, flight, marker, hull,
                shuttle ? charter.arrivingSkyfaller : incoming, charter, unload);
            return null;
        }

        internal static bool TryGetLayer(PlanetTile tile, out PlanetLayer layer)
        {
            layer = null;
            if (!tile.Valid || Find.WorldGrid == null) return false;
            try { layer = tile.Layer; }
            catch (KeyNotFoundException) { return false; }
            return layer != null && tile.tileId < layer.TilesCount;
        }

        internal static string RouteFare(PlanetTile origin, PlanetTile tile, TransportSpecs specs, out TransportFare fare)
        {
            fare = default;
            string error = CheckRoute(origin, tile, specs, out TransportLeg leg);
            if (error != null) return error;

            float fuelPrice = ThingDefOf.Chemfuel?.BaseMarketValue ?? 0f;
            var def = TransportBooking.ServiceDef;
            if (!Positive(fuelPrice) || def == null) return Error + "VehicleDefs";
            bool shuttle = specs.Ship != null;
            if (!TransportPodPricing.TryGetMaterialCost(out int podMaterials) && !shuttle) return Error + "VehicleDefs";
            double baseFare = shuttle ? Math.Max(specs.BaseFare, PodBaseFare + (double)podMaterials + MinimumShuttlePremium) : specs.BaseFare;
            int materialFare = shuttle ? 0 : podMaterials;

            IServicePricingContext pricing = ServicePricingContext.Current;
            double wealthFare = Math.Round(ServicePricingEngine.ScaledCost(def, pricing.TotalPlayerWealth, 1f, pricing.WealthPriceScalePct));
            double fuelFare = Math.Round(leg.Fuel * fuelPrice);
            double total = baseFare + materialFare + wealthFare + fuelFare;
            if (!double.IsFinite(total) || total > int.MaxValue) return Error + "VehicleDefs";

            fare = new TransportFare(leg.Distance, leg.Range, (int)baseFare, materialFare, (int)wealthFare,
                leg.Fuel, (int)fuelFare, fuelPrice, leg.LayerFactor);
            return null;
        }

        internal static string CheckRoute(PlanetTile origin, PlanetTile tile, TransportSpecs specs, out TransportLeg leg)
        {
            leg = default;
            if (!TryGetLayer(origin, out PlanetLayer from) || !TryGetLayer(tile, out PlanetLayer to)
                || from != to || origin == tile || to.Def.isSpace) return InvalidDestination;
            if (Find.WorldObjects.ObjectsAt(tile).Any(o => o.RequiresSignalJammerToReach)) return Error + "Jammer";
            int distance = Find.WorldGrid.TraversalDistanceBetween(origin, tile, passImpassable: true, int.MaxValue, canTraverseLayers: false);
            float factor = to.Def.rangeDistanceFactor;
            float rate = specs.FuelRate * factor;
            if (!Positive(factor) || !Positive(rate)) return Error + "VehicleDefs";
            int range = ClampRange(Math.Floor(specs.FuelCapacity / rate));
            if (specs.FixedRange >= 0)
                range = Math.Min(range, ClampRange(Math.Round(specs.FixedRange / factor)));
            float fuel = Mathf.Max(specs.MinimumFuel, distance * rate);
            if (distance <= 0 || distance == int.MaxValue || distance > range || !float.IsFinite(fuel) || fuel > specs.FuelCapacity)
                return Error + "Range";

            leg = new TransportLeg(distance, range, fuel, factor);
            return null;
        }

        private static int ClampRange(double value) => (int)Math.Min(value, int.MaxValue);
        private static bool Positive(float value) => float.IsFinite(value) && value > 0f;

        internal static bool IsWorldDestination(PlanetTile tile) => TryGetLayer(tile, out PlanetLayer layer)
            && !layer.Def.isSpace && layer.Def.canFormCaravans && !Find.World.Impassable(tile)
            && Find.WorldObjects.ObjectsAt(tile).All(o => !o.RequiresSignalJammerToReach && (o is Settlement || IsTransient(o)));

        internal static bool CanBookWorldDestination(PlanetTile tile) => IsWorldDestination(tile)
            && !LocalDestinations(tile).Any(p => p.Map.IsPlayerHome || p.Faction == Faction.OfPlayer);

        internal static IEnumerable<MapParent> LocalDestinations(PlanetTile tile)
        {
            if (!TryGetLayer(tile, out PlanetLayer layer) || layer.Def.isSpace) yield break;
            List<WorldObject> fixedObjects = Find.WorldObjects.ObjectsAt(tile).Where(o => !IsTransient(o)).ToList();
            if (fixedObjects.Any(o => o.RequiresSignalJammerToReach)) yield break;
            foreach (MapParent parent in fixedObjects.OfType<MapParent>())
            {
                if (parent.Destroyed || !parent.Spawned || !parent.HasMap || !Find.Maps.Contains(parent.Map)
                    || parent.Map.Tile != tile || parent.EnterCooldownBlocksEntering()) continue;
                if (fixedObjects.Any(o => o != parent && o is not Settlement)) continue;
                if (parent.Map.IsPlayerHome || (parent.Faction == Faction.OfPlayer && parent is not Site)
                    || (parent is Settlement && parent.Faction != null && !parent.Faction.HostileTo(Faction.OfPlayer)
                        && TransportersArrivalAction_LandInSpecificCell.CanLandInSpecificCell(null, parent)))
                    yield return parent;
            }
        }

        internal static MapParent FindLocalDestination(TransportDestination destination) => destination.IsLocal
            ? LocalDestinations(destination.Tile).FirstOrDefault(p => p.ID == destination.MapParentId) : null;

        private static bool IsTransient(WorldObject obj) => obj is Caravan || obj is TravellingTransporters
            || obj is SettlementTransportMarker || obj is RoutePlannerWaypoint;

        internal static IEnumerable<IntVec3> Footprint(TransportMode mode, ThingDef hull, IntVec3 cell, Map map)
        {
            if (mode == TransportMode.Pod) { yield return cell; yield break; }
            foreach (IntVec3 occupied in GenAdj.OccupiedRect(cell, LandingRotation, hull.Size)) yield return occupied;
            yield return ThingUtility.InteractionCellWhenAt(hull, cell, LandingRotation, map);
        }

        internal static bool IsSafeCell(TransportMode mode, ThingDef hull, IntVec3 cell, Map map, Thing ignore = null)
        {
            if (map == null || !cell.IsValid || !cell.InBounds(map)) return false;
            ThingDef landed = mode == TransportMode.Shuttle ? hull : ThingDefOf.ActiveDropPod;
            if (landed == null) return false;
            if (mode == TransportMode.Pod && ignore == null && !DropCellFinder.IsGoodDropSpot(cell, map, false, false, false))
                return false;
            if (mode == TransportMode.Pod && ignore is not SettlementTransportPod && cell.GetItemCount(map) >= cell.GetMaxItemsAllowedInCell(map))
                return false;

            foreach (IntVec3 spot in Footprint(mode, hull, cell, map))
            {
                if (!spot.IsValid || !spot.InBounds(map) || spot.Fogged(map) || spot.Roofed(map) || !spot.Standable(map)) return false;
                if (landed.terrainAffordanceNeeded != null && !spot.GetTerrain(map).affordances.Contains(landed.terrainAffordanceNeeded)) return false;
                if (spot.GetThingList(map).Any(t => t != ignore && (t is Skyfaller || t is IActiveTransporter
                    || t is Pawn || t.def.IsBlueprint || t.def.preventSkyfallersLandingOn
                    || GenSpawn.SpawningWipes(landed, t.def, ignoreDestroyable: true)))) return false;
            }
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Framework.Compat.VehicleFramework
{
    internal enum VehicleShopNavigation
    {
        Ok,
        Blocked,
        Unknown
    }

    internal static class VehicleShopAdapter
    {
        private const int EnabledForPlayerFlag = 1;

        private static bool resolveAttempted;
        private static bool resolveFailed;

        private static Type vehicleDefType;
        private static Type vehiclePawnType;

        private static FieldInfo canCaravanField;
        private static FieldInfo enabledField;
        private static FieldInfo kindDefField;
        private static FieldInfo vehicleTypeField;

        private static MethodInfo generateVehicleMethod;

        private static PropertyInfo pathGridInstanceProp;
        private static PropertyInfo pathGridInitializedProp;
        private static MethodInfo passableMethod;
        private static Type passableTileParamType;

        public static bool Available
        {
            get
            {
                EnsureResolved();
                return !resolveFailed;
            }
        }

        public static bool IsVehicleDef(ThingDef def)
        {
            EnsureResolved();
            return !resolveFailed && def != null && vehicleDefType.IsInstanceOfType(def);
        }

        public static bool IsPlayerPurchasable(ThingDef def)
        {
            if (!IsVehicleDef(def) || def.label.NullOrEmpty()) return false;

            float marketValue = def.BaseMarketValue;
            if (float.IsNaN(marketValue) || float.IsInfinity(marketValue) || marketValue <= 0f) return false;

            if (!(bool)canCaravanField.GetValue(def)) return false;
            if ((Convert.ToInt32(enabledField.GetValue(def)) & EnabledForPlayerFlag) == 0) return false;
            return kindDefField == null || kindDefField.GetValue(def) != null;
        }

        public static string VehicleTypeName(ThingDef def)
        {
            EnsureResolved();
            return resolveFailed || vehicleTypeField == null || !IsVehicleDef(def) ? null : vehicleTypeField.GetValue(def)?.ToString();
        }

        public static IEnumerable<ThingDef> VehicleDefsIn(Caravan caravan)
        {
            EnsureResolved();
            if (resolveFailed || caravan == null) return Enumerable.Empty<ThingDef>();
            return caravan.PawnsListForReading.Where(p => vehiclePawnType.IsInstanceOfType(p)).Select(p => p.def).Distinct().ToList();
        }

        public static Pawn GenerateVehicle(ThingDef def)
        {
            EnsureResolved();
            if (resolveFailed || !IsVehicleDef(def)) return null;

            try
            {
                object generated = generateVehicleMethod.Invoke(null, new object[] { def, Faction.OfPlayer });
                if (!(generated is Pawn pawn) || !vehiclePawnType.IsInstanceOfType(pawn))
                {
                    SupportLog.Error($"Vehicle Framework returned no vehicle while generating '{def.defName}' for the vehicle shop.");
                    return null;
                }
                if (pawn.Destroyed)
                {
                    SupportLog.Error($"Vehicle Framework returned an already-destroyed vehicle while generating '{def.defName}' for the vehicle shop.");
                    return null;
                }
                return pawn;
            }
            catch (Exception ex)
            {
                SupportLog.Error($"Vehicle Framework threw while generating '{def.defName}' for the vehicle shop: {(ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex)}");
                return null;
            }
        }

        public static VehicleShopNavigation CheckNavigation(ThingDef newDef, IEnumerable<ThingDef> fleetDefs, PlanetTile tile)
        {
            EnsureResolved();
            if (resolveFailed || newDef == null || !tile.Valid) return VehicleShopNavigation.Unknown;

            try
            {
                object grid = pathGridInstanceProp.GetValue(null);
                if (grid == null) return VehicleShopNavigation.Unknown;
                if (pathGridInitializedProp != null && !(bool)pathGridInitializedProp.GetValue(grid)) return VehicleShopNavigation.Unknown;

                List<ThingDef> defs = (fleetDefs ?? Enumerable.Empty<ThingDef>()).Append(newDef).Distinct().ToList();
                if (!defs.All(d => IsPassable(grid, d, tile))) return VehicleShopNavigation.Blocked;

                var neighbors = new List<PlanetTile>();
                Find.WorldGrid.GetTileNeighbors(tile, neighbors);
                return neighbors.Any(n => defs.All(d => IsPassable(grid, d, n))) ? VehicleShopNavigation.Ok : VehicleShopNavigation.Blocked;
            }
            catch (Exception ex)
            {
                SupportLog.Error($"Vehicle Framework world pathing could not be queried for the vehicle shop: {(ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex)}");
                return VehicleShopNavigation.Unknown;
            }
        }

        private static bool IsPassable(object grid, ThingDef vehicleDef, PlanetTile tile)
        {
            object tileArgument = passableTileParamType == typeof(PlanetTile) ? (object)tile : (object)tile.tileId;
            return (bool)passableMethod.Invoke(grid, new[] { tileArgument, vehicleDef });
        }

        private static void EnsureResolved()
        {
            if (resolveAttempted) return;
            resolveAttempted = true;

            vehicleDefType = SettlementServicesModCompat.ResolveOptionalType("VehicleDef", "Vehicles");
            vehiclePawnType = SettlementServicesModCompat.ResolveOptionalType("VehiclePawn", "Vehicles");
            Type spawnerType = SettlementServicesModCompat.ResolveOptionalType("VehicleSpawner", "Vehicles");
            Type pathGridType = SettlementServicesModCompat.ResolveOptionalType("WorldVehiclePathGrid", "Vehicles.World");

            var missing = new List<string>();
            if (vehicleDefType == null) missing.Add("Vehicles.VehicleDef");
            if (vehiclePawnType == null) missing.Add("Vehicles.VehiclePawn");
            if (spawnerType == null) missing.Add("Vehicles.VehicleSpawner");
            if (pathGridType == null) missing.Add("Vehicles.World.WorldVehiclePathGrid");

            if (missing.Count == 0)
            {
                canCaravanField = vehicleDefType.GetField("canCaravan", BindingFlags.Public | BindingFlags.Instance);
                enabledField = vehicleDefType.GetField("enabled", BindingFlags.Public | BindingFlags.Instance);
                kindDefField = vehicleDefType.GetField("kindDef", BindingFlags.Public | BindingFlags.Instance);
                vehicleTypeField = vehicleDefType.GetField("type", BindingFlags.Public | BindingFlags.Instance);

                generateVehicleMethod = spawnerType.GetMethod("GenerateVehicle", BindingFlags.Public | BindingFlags.Static, null, new[] { vehicleDefType, typeof(Faction) }, null);

                pathGridInstanceProp = pathGridType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                pathGridInitializedProp = pathGridType.GetProperty("Initialized", BindingFlags.Public | BindingFlags.Instance);
                passableMethod = pathGridType.GetMethod("Passable", BindingFlags.Public | BindingFlags.Instance);
                ParameterInfo[] passableParams = passableMethod?.GetParameters();
                passableTileParamType = passableParams != null && passableParams.Length == 2 && passableParams[1].ParameterType == vehicleDefType
                    ? passableParams[0].ParameterType
                    : null;

                if (canCaravanField == null || canCaravanField.FieldType != typeof(bool)) missing.Add("VehicleDef.canCaravan");
                if (enabledField == null || !enabledField.FieldType.IsEnum) missing.Add("VehicleDef.enabled");
                if (generateVehicleMethod == null) missing.Add("VehicleSpawner.GenerateVehicle(VehicleDef, Faction)");
                if (pathGridInstanceProp == null) missing.Add("WorldVehiclePathGrid.Instance");
                if (passableTileParamType != typeof(PlanetTile) && passableTileParamType != typeof(int)) missing.Add("WorldVehiclePathGrid.Passable(tile, VehicleDef)");
                if (pathGridInitializedProp != null && pathGridInitializedProp.PropertyType != typeof(bool)) pathGridInitializedProp = null;
            }

            if (missing.Count > 0)
            {
                resolveFailed = true;
                SupportLog.Info($"Vehicle Framework is installed but its API shape doesn't match what the vehicle shop expects (missing or changed: {string.Join(", ", missing)}); the vehicle shop will stay unavailable.");
            }
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Framework.Compat
{
    public class VehicleUpgradeOption
    {
        public string key;
        public string label;
        public List<ThingDefCountClass> ingredients = new List<ThingDefCountClass>();
    }

    public static class VehicleFrameworkAdapter
    {
        private static bool resolveAttempted;
        private static bool resolveFailed;

        private static Type vehiclePawnType;
        private static Type compFueledTravelType;
        private static Type compUpgradeTreeType;
        private static Type upgradeNodeType;

        private static FieldInfo statHandlerField;
        private static FieldInfo componentsField;
        private static PropertyInfo componentMaxHealthProp;
        private static PropertyInfo componentHealthPercentProp;
        private static MethodInfo componentSetHealthMethod;

        private static PropertyInfo fuelProp;
        private static PropertyInfo fuelCapacityProp;
        private static MethodInfo refuelMethod;
        private static FieldInfo fuelTypeField;

        private static FieldInfo nodesField;
        private static FieldInfo nodeKeyField;
        private static FieldInfo nodeLabelField;
        private static FieldInfo nodeHiddenField;
        private static FieldInfo nodeIngredientsField;
        private static FieldInfo nodeResearchField;
        private static MethodInfo nodeUnlockedMethod;
        private static MethodInfo prerequisitesMetMethod;
        private static MethodInfo disabledMethod;
        private static MethodInfo finishUnlockMethod;

        public static Type VehiclePawnType { get { EnsureResolved(); return resolveFailed ? null : vehiclePawnType; } }

        public static bool IsVehicle(Thing thing) => VehiclePawnType != null && VehiclePawnType.IsInstanceOfType(thing);

        public static IEnumerable<Thing> VehiclesInCaravan(Caravan caravan)
        {
            Type vehicleType = VehiclePawnType;
            if (caravan == null || vehicleType == null) return Enumerable.Empty<Thing>();
            return caravan.PawnsListForReading.Where(p => vehicleType.IsInstanceOfType(p));
        }

        public static bool TryGetFuel(Thing vehicle, out float current, out float capacity, out ThingDef fuelDef)
        {
            current = 0f; capacity = 0f; fuelDef = null;
            ThingComp comp = FuelComp(vehicle);
            if (comp == null) return false;

            current = (float)fuelProp.GetValue(comp);
            capacity = (float)fuelCapacityProp.GetValue(comp);
            fuelDef = comp.props != null && fuelTypeField != null ? (ThingDef)fuelTypeField.GetValue(comp.props) : null;
            return true;
        }

        public static bool TryRefuel(Thing vehicle, float amount)
        {
            if (amount <= 0f) return false;
            ThingComp comp = FuelComp(vehicle);
            if (comp == null) return false;
            refuelMethod.Invoke(comp, new object[] { amount });
            return true;
        }

        public static float HealthPercent(Thing vehicle)
        {
            List<object> components = Components(vehicle);
            return components.Count == 0 ? 1f : components.Average(c => (float)componentHealthPercentProp.GetValue(c));
        }

        public static bool NeedsRepairs(Thing vehicle) => Components(vehicle).Any(c => (float)componentHealthPercentProp.GetValue(c) < 1f);

        public static void RepairTo(Thing vehicle, float targetPercent)
        {
            foreach (object component in Components(vehicle))
            {
                float healthPercent = (float)componentHealthPercentProp.GetValue(component);
                if (healthPercent >= targetPercent) continue;
                float maxHealth = (float)componentMaxHealthProp.GetValue(component);
                componentSetHealthMethod.Invoke(component, new object[] { maxHealth * targetPercent });
            }
        }

        public static bool HasUpgradeTree(Thing vehicle) => UpgradeTreeComp(vehicle) != null;

        public static List<VehicleUpgradeOption> GetInstallableUpgrades(Thing vehicle)
        {
            var result = new List<VehicleUpgradeOption>();
            ThingComp comp = UpgradeTreeComp(vehicle);
            IEnumerable nodes = comp != null ? AllNodes(comp) : null;
            if (nodes == null) return result;

            foreach (object node in nodes)
            {
                if (nodeHiddenField != null && (bool)nodeHiddenField.GetValue(node)) continue;
                if ((bool)nodeUnlockedMethod.Invoke(comp, new[] { node })) continue;
                if (!(bool)prerequisitesMetMethod.Invoke(comp, new[] { node })) continue;
                if ((bool)disabledMethod.Invoke(comp, new[] { node })) continue;

                List<ResearchProjectDef> research = nodeResearchField != null
                    ? (List<ResearchProjectDef>)nodeResearchField.GetValue(node) : null;
                if (!research.NullOrEmpty() && research.Any(r => !r.IsFinished)) continue;

                result.Add(new VehicleUpgradeOption
                {
                    key = (string)nodeKeyField.GetValue(node),
                    label = (string)nodeLabelField.GetValue(node),
                    ingredients = nodeIngredientsField != null
                        ? (List<ThingDefCountClass>)nodeIngredientsField.GetValue(node) ?? new List<ThingDefCountClass>()
                        : new List<ThingDefCountClass>(),
                });
            }
            return result;
        }

        public static bool InstallUpgrade(Thing vehicle, string upgradeKey)
        {
            ThingComp comp = UpgradeTreeComp(vehicle);
            IEnumerable nodes = comp != null ? AllNodes(comp) : null;
            if (nodes == null) return false;

            object node = nodes.Cast<object>().FirstOrDefault(n => (string)nodeKeyField.GetValue(n) == upgradeKey);
            if (node == null) return false;

            bool unlocked = (bool)nodeUnlockedMethod.Invoke(comp, new[] { node });
            bool prereqsMet = (bool)prerequisitesMetMethod.Invoke(comp, new[] { node });
            bool disabled = (bool)disabledMethod.Invoke(comp, new[] { node });
            if (unlocked || !prereqsMet || disabled) return false;

            finishUnlockMethod.Invoke(comp, new[] { node });
            return true;
        }

        private static ThingComp FuelComp(Thing vehicle) => VehicleComp(vehicle, compFueledTravelType);
        private static ThingComp UpgradeTreeComp(Thing vehicle) => VehicleComp(vehicle, compUpgradeTreeType);

        private static ThingComp VehicleComp(Thing vehicle, Type compType)
        {
            EnsureResolved();
            if (resolveFailed || compType == null || !(vehicle is ThingWithComps twc)) return null;
            return twc.AllComps.FirstOrDefault(c => compType.IsInstanceOfType(c));
        }

        private static List<object> Components(Thing vehicle)
        {
            var result = new List<object>();
            if (!IsVehicle(vehicle)) return result;

            object statHandler = statHandlerField.GetValue(vehicle);
            if (statHandler == null || !(componentsField.GetValue(statHandler) is IEnumerable components)) return result;
            foreach (object component in components) result.Add(component);
            return result;
        }

        private static IEnumerable AllNodes(ThingComp upgradeTreeComp)
        {
            if (nodesField == null) return null;
            object upgradeTreeDef = FindMemberValueOfFieldType(upgradeTreeComp.props, nodesField.DeclaringType)
                ?? FindMemberValueOfFieldType(upgradeTreeComp, nodesField.DeclaringType);
            return upgradeTreeDef != null ? nodesField.GetValue(upgradeTreeDef) as IEnumerable : null;
        }

        private static object FindMemberValueOfFieldType(object instance, Type fieldType)
        {
            if (instance == null || fieldType == null) return null;
            FieldInfo field = instance.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(f => fieldType.IsAssignableFrom(f.FieldType));
            return field?.GetValue(instance);
        }

        private static void EnsureResolved()
        {
            if (resolveAttempted) return;
            resolveAttempted = true;

            vehiclePawnType = SettlementServicesModCompat.ResolveOptionalType("VehiclePawn", "Vehicles");
            compFueledTravelType = SettlementServicesModCompat.ResolveOptionalType("CompFueledTravel", "Vehicles");
            compUpgradeTreeType = SettlementServicesModCompat.ResolveOptionalType("CompUpgradeTree", "Vehicles");
            Type vehicleStatHandlerType = SettlementServicesModCompat.ResolveOptionalType("VehicleStatHandler", "Vehicles");
            Type vehicleComponentType = SettlementServicesModCompat.ResolveOptionalType("VehicleComponent", "Vehicles");
            upgradeNodeType = SettlementServicesModCompat.ResolveOptionalType("UpgradeNode", "Vehicles");
            Type upgradeTreeDefType = SettlementServicesModCompat.ResolveOptionalType("UpgradeTreeDef", "Vehicles");
            Type compPropsFueledTravelType = SettlementServicesModCompat.ResolveOptionalType("CompProperties_FueledTravel", "Vehicles");

            if (vehiclePawnType == null || compFueledTravelType == null || compUpgradeTreeType == null
                || vehicleStatHandlerType == null || vehicleComponentType == null || upgradeNodeType == null
                || upgradeTreeDefType == null || compPropsFueledTravelType == null)
            { resolveFailed = true; return; }

            statHandlerField = vehiclePawnType.GetField("statHandler", BindingFlags.Public | BindingFlags.Instance);
            componentsField = vehicleStatHandlerType.GetField("components", BindingFlags.Public | BindingFlags.Instance);
            componentMaxHealthProp = vehicleComponentType.GetProperty("MaxHealth");
            componentHealthPercentProp = vehicleComponentType.GetProperty("HealthPercent");
            componentSetHealthMethod = vehicleComponentType.GetMethod("SetHealth", new[] { typeof(float) });

            fuelProp = compFueledTravelType.GetProperty("Fuel");
            fuelCapacityProp = compFueledTravelType.GetProperty("FuelCapacity");
            refuelMethod = compFueledTravelType.GetMethod("Refuel", new[] { typeof(float) });
            fuelTypeField = compPropsFueledTravelType.GetField("fuelType", BindingFlags.Public | BindingFlags.Instance);

            nodesField = upgradeTreeDefType.GetField("nodes", BindingFlags.Public | BindingFlags.Instance);
            nodeKeyField = upgradeNodeType.GetField("key", BindingFlags.Public | BindingFlags.Instance);
            nodeLabelField = upgradeNodeType.GetField("label", BindingFlags.Public | BindingFlags.Instance);
            nodeHiddenField = upgradeNodeType.GetField("hidden", BindingFlags.Public | BindingFlags.Instance);
            nodeIngredientsField = upgradeNodeType.GetField("ingredients", BindingFlags.Public | BindingFlags.Instance);
            nodeResearchField = upgradeNodeType.GetField("researchPrerequisites", BindingFlags.Public | BindingFlags.Instance);
            nodeUnlockedMethod = compUpgradeTreeType.GetMethod("NodeUnlocked", new[] { upgradeNodeType });
            prerequisitesMetMethod = compUpgradeTreeType.GetMethod("PrerequisitesMet", new[] { upgradeNodeType });
            disabledMethod = compUpgradeTreeType.GetMethod("Disabled", new[] { upgradeNodeType });
            finishUnlockMethod = compUpgradeTreeType.GetMethod("FinishUnlock", new[] { upgradeNodeType });

            bool missingCore = statHandlerField == null || componentsField == null || componentHealthPercentProp == null
                || componentMaxHealthProp == null || componentSetHealthMethod == null
                || fuelProp == null || fuelCapacityProp == null || refuelMethod == null
                || nodesField == null || nodeKeyField == null || nodeLabelField == null
                || nodeUnlockedMethod == null || prerequisitesMetMethod == null || disabledMethod == null || finishUnlockMethod == null;

            if (missingCore)
            {
                resolveFailed = true;
                SupportLog.Info("Vehicle Framework is installed but its API shape doesn't match what this mod expects; vehicle services will stay unavailable.");
            }
        }
    }
}

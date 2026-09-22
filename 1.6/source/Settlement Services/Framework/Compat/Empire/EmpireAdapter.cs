using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Framework.Compat.Empire
{
    internal static class EmpireAdapter
    {
        private enum BindingState { Uninitialized, Unavailable, Ready, Incompatible }

        internal const string PackageId = "Matathias.Empire";
        private const string HarmonyId = "sk.settlementservices.compat.empire";

        private static BindingState state = BindingState.Uninitialized;

        private static Type worldSettlementFCType;
        private static Func<Settlement, bool> isUnderAttack;

        internal static bool IsReady => state == BindingState.Ready;

        internal static void Initialize()
        {
            if (state != BindingState.Uninitialized) return;

            if (!ModsConfig.IsActive(PackageId))
            {
                state = BindingState.Unavailable;
                return;
            }

            Type settlementType = SettlementServicesModCompat.ResolveOptionalType("WorldSettlementFC", "FactionColonies");
            if (settlementType == null || !typeof(Settlement).IsAssignableFrom(settlementType))
            { FailBind("WorldSettlementFC type not found or is not a Settlement"); return; }

            Type militaryCompType = SettlementServicesModCompat.ResolveOptionalType("WorldObjectComp_SettlementMilitary", "FactionColonies");
            if (militaryCompType == null) { FailBind("WorldObjectComp_SettlementMilitary type not found"); return; }

            MethodInfo gizmosMethod = settlementType.GetMethod(nameof(Settlement.GetCaravanGizmos),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, new[] { typeof(Caravan) }, null);
            MethodInfo floatMenuMethod = settlementType.GetMethod(nameof(Settlement.GetFloatMenuOptions),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, new[] { typeof(Caravan) }, null);
            PropertyInfo militaryCompProp = settlementType.GetProperty("MilitaryComp", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo isUnderAttackProp = militaryCompType.GetProperty("isUnderAttack", BindingFlags.Public | BindingFlags.Instance);

            bool shapeValid = gizmosMethod != null && typeof(IEnumerable<Gizmo>).IsAssignableFrom(gizmosMethod.ReturnType)
                && floatMenuMethod != null && typeof(IEnumerable<FloatMenuOption>).IsAssignableFrom(floatMenuMethod.ReturnType)
                && militaryCompProp != null && militaryCompType.IsAssignableFrom(militaryCompProp.PropertyType)
                && isUnderAttackProp != null && isUnderAttackProp.PropertyType == typeof(bool);

            if (!shapeValid) { FailBind("member shape mismatch"); return; }

            Func<Settlement, bool> compiledIsUnderAttack;
            try
            {
                compiledIsUnderAttack = CompileIsUnderAttack(settlementType, militaryCompType, militaryCompProp, isUnderAttackProp);
            }
            catch (Exception ex)
            {
                FailBind($"failed to bind isUnderAttack ({ex.Message})");
                return;
            }

            try
            {
                var harmony = new Harmony(HarmonyId);
                harmony.Patch(gizmosMethod, postfix: new HarmonyMethod(typeof(EmpireInteractionPatches), nameof(EmpireInteractionPatches.GizmosPostfix)));
                harmony.Patch(floatMenuMethod, postfix: new HarmonyMethod(typeof(EmpireInteractionPatches), nameof(EmpireInteractionPatches.FloatMenuPostfix)));
            }
            catch (Exception ex)
            {
                FailBind($"Harmony patch failed ({ex.Message})");
                return;
            }

            worldSettlementFCType = settlementType;
            isUnderAttack = compiledIsUnderAttack;
            state = BindingState.Ready;
            SupportLog.Info("Empire Refactored detected; settlement services compatibility active.");
        }

        internal static bool IsEmpireSettlement(Settlement settlement) =>
            state == BindingState.Ready && settlement != null && worldSettlementFCType.IsInstanceOfType(settlement);

        internal static bool IsUnderAttack(Settlement settlement) =>
            IsEmpireSettlement(settlement) && isUnderAttack(settlement);

        private static void FailBind(string reason)
        {
            state = BindingState.Incompatible;
            worldSettlementFCType = null;
            isUnderAttack = null;
            SupportLog.Warning($"Empire Refactored is installed but its API shape doesn't match what this mod expects ({reason}); Empire compatibility will stay disabled.");
        }

        private static Func<Settlement, bool> CompileIsUnderAttack(Type settlementType, Type militaryCompType, PropertyInfo militaryCompProp, PropertyInfo isUnderAttackProp)
        {
            ParameterExpression settlementParam = Expression.Parameter(typeof(Settlement), "settlement");
            Expression typedSettlement = Expression.Convert(settlementParam, settlementType);
            Expression militaryComp = Expression.Property(typedSettlement, militaryCompProp);
            Expression isUnderAttackAccess = Expression.Property(militaryComp, isUnderAttackProp);
            Expression nullSafeAccess = Expression.Condition(
                Expression.Equal(militaryComp, Expression.Constant(null, militaryCompType)),
                Expression.Constant(false),
                isUnderAttackAccess);
            return Expression.Lambda<Func<Settlement, bool>>(nullSafeAccess, settlementParam).Compile();
        }
    }
}

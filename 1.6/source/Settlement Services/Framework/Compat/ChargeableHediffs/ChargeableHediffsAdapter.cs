using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Compatibility;

namespace Settlement_Services.Framework.Compat.ChargeableHediffs
{
    internal static class ChargeableHediffsAdapter
    {
        private enum BindingState { Uninitialized, Unavailable, Ready, Incompatible }

        private const string PackageId = "sk.chargehediff";

        private static BindingState state = BindingState.Uninitialized;

        private static Type compType;

        private static Func<HediffComp, float> getMaxCharge;
        private static Func<HediffComp, float> getCurrentCharge;
        private static Func<HediffComp, bool> getNeedsCharge;
        private static Action<HediffComp, float> setCharge;

        internal static bool IsReady => state == BindingState.Ready;

        internal static void Initialize()
        {
            if (state != BindingState.Uninitialized) return;

            if (!ModsConfig.IsActive(PackageId))
            {
                state = BindingState.Unavailable;
                return;
            }

            Type localCompType = SettlementServicesModCompat.ResolveOptionalType("HediffComp_Rechargeable", "Chargeable_Hediffs_Framework");
            if (localCompType == null) { FailBind("HediffComp_Rechargeable type not found"); return; }
            if (!typeof(HediffComp).IsAssignableFrom(localCompType)) { FailBind("HediffComp_Rechargeable is not a HediffComp"); return; }

            PropertyInfo maxChargeProp = localCompType.GetProperty("MaxCharge", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo currentChargeProp = localCompType.GetProperty("CurrentCharge", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo needsChargeProp = localCompType.GetProperty("NeedsCharge", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo setChargeMethod = localCompType.GetMethod("SetCharge", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(float) }, null);

            bool shapeValid = maxChargeProp != null && maxChargeProp.PropertyType == typeof(float)
                && currentChargeProp != null && currentChargeProp.PropertyType == typeof(float)
                && needsChargeProp != null && needsChargeProp.PropertyType == typeof(bool)
                && setChargeMethod != null && setChargeMethod.ReturnType == typeof(void);

            if (!shapeValid) { FailBind("member shape mismatch"); return; }

            compType = localCompType;
            getMaxCharge = CompileInstanceFloatGetter(localCompType, maxChargeProp);
            getCurrentCharge = CompileInstanceFloatGetter(localCompType, currentChargeProp);
            getNeedsCharge = CompileInstanceBoolGetter(localCompType, needsChargeProp);
            setCharge = CompileInstanceFloatSetter(localCompType, setChargeMethod);

            state = BindingState.Ready;
            SupportLog.Info("Chargeable Hediffs Framework detected; bionic recharge compatibility active.");
        }

        internal static RechargeableHediffStatus Inspect(Pawn pawn)
        {
            if (state != BindingState.Ready || pawn == null || pawn.Dead || pawn.health?.hediffSet == null)
                return RechargeableHediffStatus.None;

            float deficitSum = 0f;
            float maxSum = 0f;
            bool hasAny = false;

            foreach (HediffComp comp in EnumerateComps(pawn))
            {
                hasAny = true;
                float max = getMaxCharge(comp);
                if (max <= 0f) continue;

                float current = getCurrentCharge(comp);
                deficitSum += Mathf.Max(0f, max - current);
                maxSum += max;
            }

            if (!hasAny || maxSum <= 0f) return new RechargeableHediffStatus(hasAny, false, 0f);

            float deficitFraction = Mathf.Clamp01(deficitSum / maxSum);
            return new RechargeableHediffStatus(true, deficitFraction > 0f, deficitFraction);
        }

        internal static bool TryRechargeFully(Pawn pawn, out string errorKey)
        {
            errorKey = null;

            if (state != BindingState.Ready)
            {
                errorKey = "SettlementServices.Error.ChargeableHediffsUnavailable";
                return false;
            }
            if (pawn == null || pawn.Dead || pawn.health?.hediffSet == null)
            {
                errorKey = "SettlementServices.Error.TargetNoLongerExists";
                return false;
            }

            bool any = false;
            foreach (HediffComp comp in EnumerateComps(pawn).ToList())
            {
                any = true;
                if (getNeedsCharge(comp))
                    setCharge(comp, getMaxCharge(comp));
            }

            if (!any)
            {
                errorKey = "SettlementServices.Error.NoRechargeableBionicsNeedCharge";
                return false;
            }
            return true;
        }

        private static IEnumerable<HediffComp> EnumerateComps(Pawn pawn)
        {
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
            {
                if (!(hediff is HediffWithComps withComps)) continue;
                foreach (HediffComp comp in withComps.comps)
                {
                    if (compType.IsInstanceOfType(comp)) yield return comp;
                }
            }
        }

        private static void FailBind(string reason)
        {
            state = BindingState.Incompatible;
            compType = null;
            getMaxCharge = null;
            getCurrentCharge = null;
            getNeedsCharge = null;
            setCharge = null;
            SupportLog.Warning($"Chargeable Hediffs Framework is installed but its API shape doesn't match what this mod expects ({reason}); bionic recharge will stay unavailable.");
        }

        private static Func<HediffComp, float> CompileInstanceFloatGetter(Type declaringType, PropertyInfo prop)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(HediffComp), "comp");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression access = Expression.Property(typedInstance, prop);
            return Expression.Lambda<Func<HediffComp, float>>(access, instanceParam).Compile();
        }

        private static Func<HediffComp, bool> CompileInstanceBoolGetter(Type declaringType, PropertyInfo prop)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(HediffComp), "comp");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression access = Expression.Property(typedInstance, prop);
            return Expression.Lambda<Func<HediffComp, bool>>(access, instanceParam).Compile();
        }

        private static Action<HediffComp, float> CompileInstanceFloatSetter(Type declaringType, MethodInfo method)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(HediffComp), "comp");
            ParameterExpression amountParam = Expression.Parameter(typeof(float), "amount");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression call = Expression.Call(typedInstance, method, amountParam);
            return Expression.Lambda<Action<HediffComp, float>>(call, instanceParam, amountParam).Compile();
        }
    }
}

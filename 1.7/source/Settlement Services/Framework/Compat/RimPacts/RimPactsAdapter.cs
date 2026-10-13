using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using RimWorld;
using Verse;

namespace Settlement_Services.Framework.Compat.RimPacts
{
    internal enum RimPactsServiceBlock
    {
        None,
        Embargoed,
        CivilWar
    }

    internal readonly struct RimPactsMarketSnapshot
    {
        public readonly bool pricingActive;
        public readonly float priceOffset;

        public RimPactsMarketSnapshot(bool pricingActive, float priceOffset)
        {
            this.pricingActive = pricingActive;
            this.priceOffset = priceOffset;
        }
    }

    internal static class RimPactsAdapter
    {
        private enum BindingState { Uninitialized, Unavailable, Ready, Incompatible }

        private const string PackageId = "wowgag.rimpacts";
        private const string AssemblySimpleName = "RimPacts";

        private static BindingState state = BindingState.Uninitialized;

        private static Func<object> getInstance;
        private static Func<object, Faction, bool> isEmbargoed;
        private static Func<object, Faction, bool> inCivilWar;
        private static Func<object, float> getEcoPriceOffset;
        private static Func<bool> getMarketPricesActive;
        private static Action<object, Faction, int, string> offsetTrust;

        private static Game boundGame;
        private static object componentInstance;

        private static int lastSnapshotTick = -1;
        private static RimPactsMarketSnapshot marketSnapshot;
        private static readonly Dictionary<Faction, RimPactsServiceBlock> factionBlockCache = new Dictionary<Faction, RimPactsServiceBlock>();

        internal static bool IsReady => state == BindingState.Ready;

        internal static void Initialize()
        {
            if (state != BindingState.Uninitialized) return;

            if (!ModsConfig.IsActive(PackageId))
            {
                state = BindingState.Unavailable;
                return;
            }

            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == AssemblySimpleName);
            if (assembly == null) { FailBind("RimPacts assembly not found"); return; }

            Type componentType = assembly.GetType("RimPacts.WorldComponent_RimPacts");
            Type modType = assembly.GetType("RimPacts.RimPactsMod");
            if (componentType == null || modType == null) { FailBind("core types not found"); return; }

            PropertyInfo instanceProp = componentType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            MethodInfo isEmbargoedMethod = componentType.GetMethod("IsEmbargoed", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Faction) }, null);
            MethodInfo inCivilWarMethod = componentType.GetMethod("InCivilWar", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Faction) }, null);
            PropertyInfo ecoOffsetProp = componentType.GetProperty("EcoPriceOffset", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo marketActiveProp = modType.GetProperty("MarketPricesActive", BindingFlags.Public | BindingFlags.Static);
            MethodInfo offsetTrustMethod = componentType.GetMethod("OffsetTrust", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Faction), typeof(int), typeof(string) }, null);

            bool shapeValid = instanceProp != null && componentType.IsAssignableFrom(instanceProp.PropertyType)
                && isEmbargoedMethod != null && isEmbargoedMethod.ReturnType == typeof(bool)
                && inCivilWarMethod != null && inCivilWarMethod.ReturnType == typeof(bool)
                && ecoOffsetProp != null && ecoOffsetProp.PropertyType == typeof(float)
                && marketActiveProp != null && marketActiveProp.PropertyType == typeof(bool)
                && offsetTrustMethod != null && offsetTrustMethod.ReturnType == typeof(void);

            if (!shapeValid) { FailBind("member shape mismatch"); return; }

            getInstance = CompileStaticObjectGetter(instanceProp);
            isEmbargoed = CompileInstanceBoolMethod(componentType, isEmbargoedMethod);
            inCivilWar = CompileInstanceBoolMethod(componentType, inCivilWarMethod);
            getEcoPriceOffset = CompileInstanceFloatGetter(componentType, ecoOffsetProp);
            getMarketPricesActive = CompileStaticBoolGetter(marketActiveProp);
            offsetTrust = CompileInstanceOffsetTrustAction(componentType, offsetTrustMethod);

            state = BindingState.Ready;
            SupportLog.Info("RimPacts detected; political and market compatibility active.");
        }

        internal static RimPactsServiceBlock GetServiceBlock(Faction faction)
        {
            if (state != BindingState.Ready || faction == null) return RimPactsServiceBlock.None;

            EnsureCurrentGameComponent();
            if (componentInstance == null) return RimPactsServiceBlock.None;

            EnsureTickFresh();

            if (factionBlockCache.TryGetValue(faction, out RimPactsServiceBlock cached)) return cached;

            RimPactsServiceBlock block = isEmbargoed(componentInstance, faction) ? RimPactsServiceBlock.Embargoed
                : inCivilWar(componentInstance, faction) ? RimPactsServiceBlock.CivilWar
                : RimPactsServiceBlock.None;
            factionBlockCache[faction] = block;
            return block;
        }

        internal static RimPactsMarketSnapshot GetMarketSnapshot()
        {
            if (state != BindingState.Ready) return default;

            EnsureCurrentGameComponent();
            if (componentInstance == null) return default;

            EnsureTickFresh();
            return marketSnapshot;
        }

        internal static bool TryAwardServiceTrust(Faction faction, int amount, string reasonKey)
        {
            if (state != BindingState.Ready || faction == null || amount == 0) return false;

            EnsureCurrentGameComponent();
            if (componentInstance == null) return false;

            offsetTrust(componentInstance, faction, amount, reasonKey);
            return true;
        }

        private static void EnsureCurrentGameComponent()
        {
            Game currentGame = Current.Game;
            if (currentGame != boundGame)
            {
                boundGame = currentGame;
                componentInstance = null;
                factionBlockCache.Clear();
                lastSnapshotTick = -1;
            }

            if (componentInstance == null) componentInstance = getInstance();
        }

        private static void EnsureTickFresh()
        {
            int tick = Find.TickManager.TicksGame;
            if (tick == lastSnapshotTick) return;

            lastSnapshotTick = tick;
            factionBlockCache.Clear();

            bool pricingActive = getMarketPricesActive();
            float offset = pricingActive ? getEcoPriceOffset(componentInstance) : 0f;
            marketSnapshot = new RimPactsMarketSnapshot(pricingActive, offset);
        }

        private static void FailBind(string reason)
        {
            state = BindingState.Incompatible;
            getInstance = null;
            isEmbargoed = null;
            inCivilWar = null;
            getEcoPriceOffset = null;
            getMarketPricesActive = null;
            offsetTrust = null;
            componentInstance = null;
            boundGame = null;
            factionBlockCache.Clear();
            SupportLog.Warning($"RimPacts is installed but its API shape doesn't match what this mod expects ({reason}); RimPacts compatibility will stay disabled.");
        }

        private static Func<object> CompileStaticObjectGetter(PropertyInfo staticProp)
        {
            Expression access = Expression.Property(null, staticProp);
            Expression converted = Expression.Convert(access, typeof(object));
            return Expression.Lambda<Func<object>>(converted).Compile();
        }

        private static Func<bool> CompileStaticBoolGetter(PropertyInfo staticProp)
        {
            Expression access = Expression.Property(null, staticProp);
            return Expression.Lambda<Func<bool>>(access).Compile();
        }

        private static Func<object, Faction, bool> CompileInstanceBoolMethod(Type declaringType, MethodInfo method)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(object), "instance");
            ParameterExpression factionParam = Expression.Parameter(typeof(Faction), "faction");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression call = Expression.Call(typedInstance, method, factionParam);
            return Expression.Lambda<Func<object, Faction, bool>>(call, instanceParam, factionParam).Compile();
        }

        private static Func<object, float> CompileInstanceFloatGetter(Type declaringType, PropertyInfo instanceProp)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(object), "instance");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression access = Expression.Property(typedInstance, instanceProp);
            return Expression.Lambda<Func<object, float>>(access, instanceParam).Compile();
        }

        private static Action<object, Faction, int, string> CompileInstanceOffsetTrustAction(Type declaringType, MethodInfo method)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(object), "instance");
            ParameterExpression factionParam = Expression.Parameter(typeof(Faction), "faction");
            ParameterExpression amountParam = Expression.Parameter(typeof(int), "amount");
            ParameterExpression reasonParam = Expression.Parameter(typeof(string), "reasonKey");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression call = Expression.Call(typedInstance, method, factionParam, amountParam, reasonParam);
            return Expression.Lambda<Action<object, Faction, int, string>>(call, instanceParam, factionParam, amountParam, reasonParam).Compile();
        }
    }
}

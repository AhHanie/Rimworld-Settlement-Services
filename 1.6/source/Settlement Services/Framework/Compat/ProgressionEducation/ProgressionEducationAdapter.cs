using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using RimWorld;
using Verse;

namespace Settlement_Services.Framework.Compat.ProgressionEducation
{
    internal static class ProgressionEducationAdapter
    {
        private enum BindingState { Uninitialized, Unavailable, Ready, Incompatible }

        private const string PackageId = "ferny.ProgressionEducation";
        private const string AssemblySimpleName = "ProgressionEducation";
        private const string OptionKeyPrefix = "ProgressionEducation.ProficiencyTier:";

        private static BindingState state = BindingState.Uninitialized;

        private static Func<Pawn, bool> canHaveProficiencies;
        private static Func<Def, bool> isTrackEnabled;
        private static Func<Pawn, Def, Def> getCurrentTier;
        private static Action<Pawn, Def, Def> grantTier;
        private static Func<Def, IEnumerable> getTiers;
        private static Func<IEnumerable> getAllProficiencyDefs;

        internal static bool IsReady => state == BindingState.Ready;

        internal static readonly IProgressionEducationProficiencyGateway Instance = new AdapterGateway();

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
            if (assembly == null) { FailBind("Progression Education assembly not found"); return; }

            Type utilityType = assembly.GetType("ProgressionEducation.ProficiencyUtility");
            Type trackDefType = assembly.GetType("ProgressionEducation.ProficiencyDef");
            Type tierDefType = assembly.GetType("ProgressionEducation.ProficiencyTierDef");
            if (utilityType == null || trackDefType == null || tierDefType == null) { FailBind("core types not found"); return; }

            if (!typeof(Def).IsAssignableFrom(trackDefType)) { FailBind("ProficiencyDef is not a Def"); return; }
            if (!typeof(Def).IsAssignableFrom(tierDefType)) { FailBind("ProficiencyTierDef is not a Def"); return; }

            MethodInfo canHaveProficienciesMethod = utilityType.GetMethod("CanHaveProficiencies", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Pawn) }, null);
            MethodInfo isTrackEnabledMethod = utilityType.GetMethod("IsTrackEnabled", BindingFlags.Public | BindingFlags.Static, null, new[] { trackDefType }, null);
            MethodInfo getCurrentTierMethod = utilityType.GetMethod("GetCurrentTier", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Pawn), trackDefType }, null);
            MethodInfo grantTierMethod = utilityType.GetMethod("GrantTier", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Pawn), trackDefType, tierDefType }, null);
            FieldInfo tiersField = trackDefType.GetField("tiers", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo traitDefField = tierDefType.GetField("traitDef", BindingFlags.Public | BindingFlags.Instance);

            Type defDatabaseType = typeof(DefDatabase<>).MakeGenericType(trackDefType);
            PropertyInfo allDefsProp = defDatabaseType.GetProperty("AllDefsListForReading", BindingFlags.Public | BindingFlags.Static);

            bool shapeValid = canHaveProficienciesMethod != null && canHaveProficienciesMethod.ReturnType == typeof(bool)
                && isTrackEnabledMethod != null && isTrackEnabledMethod.ReturnType == typeof(bool)
                && getCurrentTierMethod != null && tierDefType.IsAssignableFrom(getCurrentTierMethod.ReturnType)
                && grantTierMethod != null && grantTierMethod.ReturnType == typeof(void)
                && tiersField != null && typeof(IEnumerable).IsAssignableFrom(tiersField.FieldType)
                && traitDefField != null && traitDefField.FieldType == typeof(TraitDef)
                && allDefsProp != null && typeof(IEnumerable).IsAssignableFrom(allDefsProp.PropertyType);

            if (!shapeValid) { FailBind("member shape mismatch"); return; }

            canHaveProficiencies = CompileStaticPredicate(canHaveProficienciesMethod);
            isTrackEnabled = CompileStaticDefPredicate(isTrackEnabledMethod, trackDefType);
            getCurrentTier = CompileGetCurrentTier(getCurrentTierMethod, trackDefType);
            grantTier = CompileGrantTier(grantTierMethod, trackDefType, tierDefType);
            getTiers = CompileDefEnumerableField(trackDefType, tiersField);
            getAllProficiencyDefs = CompileStaticEnumerableGetter(allDefsProp);

            state = BindingState.Ready;
            SupportLog.Info("Progression Education detected; proficiency training compatibility active.");
        }

        private struct EligiblePromotion
        {
            public Def track;
            public Def currentTier;
            public Def nextTier;
        }

        private static IEnumerable<EligiblePromotion> EnumerateEligiblePromotions(Pawn pawn)
        {
            if (state != BindingState.Ready || pawn == null || pawn.Destroyed || pawn.DevelopmentalStage.Baby())
                yield break;
            if (!canHaveProficiencies(pawn)) yield break;

            foreach (Def track in EnumerateDefs(getAllProficiencyDefs()))
            {
                if (!isTrackEnabled(track)) continue;

                Def currentTier = getCurrentTier(pawn, track);
                if (currentTier == null) continue;

                List<Def> tiers = EnumerateDefs(getTiers(track)).ToList();
                int currentIndex = tiers.FindIndex(t => ReferenceEquals(t, currentTier));
                if (currentIndex < 0 || currentIndex + 1 >= tiers.Count) continue;

                yield return new EligiblePromotion { track = track, currentTier = currentTier, nextTier = tiers[currentIndex + 1] };
            }
        }

        private static IEnumerable<Def> EnumerateDefs(IEnumerable source)
        {
            foreach (object raw in source)
                if (raw is Def def) yield return def;
        }

        private static string OptionKeyFor(Def tierDef) => OptionKeyPrefix + tierDef.defName;

        private static IEnumerable<ProficiencyPromotionOption> GetEligiblePromotions(Pawn pawn)
        {
            foreach (EligiblePromotion promotion in EnumerateEligiblePromotions(pawn))
                yield return new ProficiencyPromotionOption(OptionKeyFor(promotion.nextTier), promotion.track.LabelCap, promotion.currentTier.LabelCap, promotion.nextTier.LabelCap);
        }

        private static bool TryResolvePromotion(Pawn pawn, string optionKey, out ProficiencyPromotionOption option)
        {
            foreach (EligiblePromotion promotion in EnumerateEligiblePromotions(pawn))
            {
                if (OptionKeyFor(promotion.nextTier) != optionKey) continue;
                option = new ProficiencyPromotionOption(optionKey, promotion.track.LabelCap, promotion.currentTier.LabelCap, promotion.nextTier.LabelCap);
                return true;
            }

            option = default;
            return false;
        }

        private static ProficiencyPromotionResult TryGrantPromotion(Pawn pawn, string optionKey)
        {
            if (state != BindingState.Ready || pawn == null || pawn.Destroyed)
                return ProficiencyPromotionResult.Fail("adapter not ready or pawn missing");

            foreach (EligiblePromotion promotion in EnumerateEligiblePromotions(pawn))
            {
                if (OptionKeyFor(promotion.nextTier) != optionKey) continue;
                grantTier(pawn, promotion.track, promotion.nextTier);
                return ProficiencyPromotionResult.Ok();
            }

            return ProficiencyPromotionResult.Fail("pawn is no longer exactly one tier below the selected promotion, or the promotion no longer exists");
        }

        private sealed class AdapterGateway : IProgressionEducationProficiencyGateway
        {
            public bool IsReady => ProgressionEducationAdapter.IsReady;

            public IEnumerable<ProficiencyPromotionOption> GetEligiblePromotions(Pawn pawn) =>
                ProgressionEducationAdapter.GetEligiblePromotions(pawn);

            public bool TryResolvePromotion(Pawn pawn, string optionKey, out ProficiencyPromotionOption option) =>
                ProgressionEducationAdapter.TryResolvePromotion(pawn, optionKey, out option);

            public ProficiencyPromotionResult TryGrantPromotion(Pawn pawn, string optionKey) =>
                ProgressionEducationAdapter.TryGrantPromotion(pawn, optionKey);
        }

        private static void FailBind(string reason)
        {
            state = BindingState.Incompatible;
            canHaveProficiencies = null;
            isTrackEnabled = null;
            getCurrentTier = null;
            grantTier = null;
            getTiers = null;
            getAllProficiencyDefs = null;
            SupportLog.Warning($"Progression Education is installed but its API shape doesn't match what this mod expects ({reason}); proficiency training will stay unavailable.");
        }

        private static Func<Pawn, bool> CompileStaticPredicate(MethodInfo method)
        {
            ParameterExpression pawnParam = Expression.Parameter(typeof(Pawn), "pawn");
            Expression call = Expression.Call(null, method, pawnParam);
            return Expression.Lambda<Func<Pawn, bool>>(call, pawnParam).Compile();
        }

        private static Func<Def, bool> CompileStaticDefPredicate(MethodInfo method, Type paramType)
        {
            ParameterExpression defParam = Expression.Parameter(typeof(Def), "def");
            Expression typedParam = Expression.Convert(defParam, paramType);
            Expression call = Expression.Call(null, method, typedParam);
            return Expression.Lambda<Func<Def, bool>>(call, defParam).Compile();
        }

        private static Func<Pawn, Def, Def> CompileGetCurrentTier(MethodInfo method, Type trackType)
        {
            ParameterExpression pawnParam = Expression.Parameter(typeof(Pawn), "pawn");
            ParameterExpression trackParam = Expression.Parameter(typeof(Def), "track");
            Expression typedTrack = Expression.Convert(trackParam, trackType);
            Expression call = Expression.Call(null, method, pawnParam, typedTrack);
            Expression converted = Expression.Convert(call, typeof(Def));
            return Expression.Lambda<Func<Pawn, Def, Def>>(converted, pawnParam, trackParam).Compile();
        }

        private static Action<Pawn, Def, Def> CompileGrantTier(MethodInfo method, Type trackType, Type tierType)
        {
            ParameterExpression pawnParam = Expression.Parameter(typeof(Pawn), "pawn");
            ParameterExpression trackParam = Expression.Parameter(typeof(Def), "track");
            ParameterExpression tierParam = Expression.Parameter(typeof(Def), "tier");
            Expression typedTrack = Expression.Convert(trackParam, trackType);
            Expression typedTier = Expression.Convert(tierParam, tierType);
            Expression call = Expression.Call(null, method, pawnParam, typedTrack, typedTier);
            return Expression.Lambda<Action<Pawn, Def, Def>>(call, pawnParam, trackParam, tierParam).Compile();
        }

        private static Func<Def, IEnumerable> CompileDefEnumerableField(Type declaringType, FieldInfo field)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(Def), "instance");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression access = Expression.Field(typedInstance, field);
            Expression converted = Expression.Convert(access, typeof(IEnumerable));
            return Expression.Lambda<Func<Def, IEnumerable>>(converted, instanceParam).Compile();
        }

        private static Func<IEnumerable> CompileStaticEnumerableGetter(PropertyInfo property)
        {
            Expression access = Expression.Property(null, property);
            Expression converted = Expression.Convert(access, typeof(IEnumerable));
            return Expression.Lambda<Func<IEnumerable>>(converted).Compile();
        }
    }
}

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using RimWorld;
using Verse;

namespace Settlement_Services.Framework.Compat.RimEducation
{
    internal readonly struct RimEducationCoursePreview
    {
        public readonly string currentLabelCap;
        public readonly string nextLabelCap;
        public readonly float currentProgress01;
        public readonly float rawReward;
        public readonly float effectiveGain;
        public readonly float cappedPredictedProgress01;
        public readonly bool willAttainNextTier;

        public RimEducationCoursePreview(string currentLabelCap, string nextLabelCap, float currentProgress01, float rawReward, float effectiveGain, float cappedPredictedProgress01, bool willAttainNextTier)
        {
            this.currentLabelCap = currentLabelCap;
            this.nextLabelCap = nextLabelCap;
            this.currentProgress01 = currentProgress01;
            this.rawReward = rawReward;
            this.effectiveGain = effectiveGain;
            this.cappedPredictedProgress01 = cappedPredictedProgress01;
            this.willAttainNextTier = willAttainNextTier;
        }
    }

    internal readonly struct RimEducationAwardResult
    {
        public readonly bool anyProgressApplied;
        public readonly bool tierAttained;
        public readonly string diagnosticReason;

        public RimEducationAwardResult(bool anyProgressApplied, bool tierAttained, string diagnosticReason)
        {
            this.anyProgressApplied = anyProgressApplied;
            this.tierAttained = tierAttained;
            this.diagnosticReason = diagnosticReason;
        }
    }

    internal static class RimEducationAdapter
    {
        private delegate bool TryGetEducationDelegate(ThingComp comp, out Def education);

        private enum BindingState { Uninitialized, Unavailable, Ready, Incompatible }

        private const string PackageId = "DimonSever000.ScienceRework";
        private const string AssemblySimpleName = "ScienceRework";
        private const int AwardSliceCount = 600;

        internal const float RawCourseReward = 1f;
        internal const float ReferenceDifficulty = 1f;
        internal const float MinWorkloadRatio = 0.5f;
        internal const float MaxWorkloadRatio = 2f;

        private static BindingState state = BindingState.Uninitialized;

        private static Type compType;

        private static Func<Pawn, Pawn, float, bool> tryLearnForEducation;
        private static TryGetEducationDelegate tryGetEducation;
        private static Func<ThingComp, float> getEducationProgress;
        private static Func<ThingComp, bool> shouldEverHaveEducation;
        private static Func<Pawn, Pawn, float> educationRateFactor;
        private static Func<Def, Def> getNext;
        private static Func<Def, float> getDifficulty;
        private static Func<Def, DevelopmentalStage?> getDevelopmentalStageFilter;
        private static Func<Verse.ModSettings> getSettingsInstance;
        private static Func<Verse.ModSettings, float> getEducationSpeed;

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
            if (assembly == null) { FailBind("ScienceRework assembly not found"); return; }

            Type utilityType = assembly.GetType("ScienceRework.Utility");
            Type localCompType = assembly.GetType("ScienceRework.CompPawnEducation");
            Type localEducationDefType = assembly.GetType("ScienceRework.EducationDef");
            Type settingsType = assembly.GetType("ScienceRework.Settings");
            if (utilityType == null || localCompType == null || localEducationDefType == null || settingsType == null)
            {
                FailBind("core types not found");
                return;
            }

            if (!typeof(ThingComp).IsAssignableFrom(localCompType)) { FailBind("CompPawnEducation is not a ThingComp"); return; }
            if (!typeof(Def).IsAssignableFrom(localEducationDefType)) { FailBind("EducationDef is not a Def"); return; }
            if (!typeof(Verse.ModSettings).IsAssignableFrom(settingsType)) { FailBind("Settings is not a ModSettings"); return; }

            MethodInfo tryLearnMethod = utilityType.GetMethod("TryLearnForEducation", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Pawn), typeof(Pawn), typeof(float) }, null);
            PropertyInfo settingsProp = utilityType.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static);
            PropertyInfo progressProp = localCompType.GetProperty("EducationProgress", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo shouldEverMethod = localCompType.GetMethod("ShouldEverHaveEducation", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            MethodInfo tryGetEducationMethod = localCompType.GetMethod("TryGetEducation", BindingFlags.Public | BindingFlags.Instance, null, new[] { localEducationDefType.MakeByRefType() }, null);
            MethodInfo rateFactorMethod = localCompType.GetMethod("EducationRateFactor", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Pawn), typeof(Pawn) }, null);
            FieldInfo nextField = localEducationDefType.GetField("next", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo difficultyField = localEducationDefType.GetField("difficulty", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo devStageField = localEducationDefType.GetField("developmentalStageFilter", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo educationSpeedField = settingsType.GetField("educationSpeed", BindingFlags.Public | BindingFlags.Instance);

            bool shapeValid = tryLearnMethod != null && tryLearnMethod.ReturnType == typeof(bool)
                && settingsProp != null && typeof(Verse.ModSettings).IsAssignableFrom(settingsProp.PropertyType)
                && progressProp != null && progressProp.PropertyType == typeof(float)
                && shouldEverMethod != null && shouldEverMethod.ReturnType == typeof(bool)
                && tryGetEducationMethod != null && tryGetEducationMethod.ReturnType == typeof(bool)
                && rateFactorMethod != null && rateFactorMethod.ReturnType == typeof(float)
                && nextField != null && localEducationDefType.IsAssignableFrom(nextField.FieldType)
                && difficultyField != null && difficultyField.FieldType == typeof(float)
                && devStageField != null && devStageField.FieldType == typeof(DevelopmentalStage?)
                && educationSpeedField != null && educationSpeedField.FieldType == typeof(float);

            if (!shapeValid) { FailBind("member shape mismatch"); return; }

            compType = localCompType;

            tryLearnForEducation = CompileStaticTryLearn(utilityType, tryLearnMethod);
            getSettingsInstance = CompileStaticModSettingsGetter(settingsProp);
            getEducationProgress = CompileInstanceFloatGetter(localCompType, progressProp);
            shouldEverHaveEducation = CompileInstanceBoolMethodNoArgs(localCompType, shouldEverMethod);
            tryGetEducation = CompileTryGetEducation(localCompType, tryGetEducationMethod, localEducationDefType);
            educationRateFactor = CompileStaticRateFactor(rateFactorMethod);
            getNext = CompileDefField(localEducationDefType, nextField);
            getDifficulty = CompileFloatField(localEducationDefType, difficultyField);
            getDevelopmentalStageFilter = CompileDevelopmentalStageField(localEducationDefType, devStageField);
            getEducationSpeed = CompileInstanceFloatField(settingsType, educationSpeedField);

            state = BindingState.Ready;
            SupportLog.Info("Rim Education detected; education course compatibility active.");
        }

        internal static bool TryGetCoursePreview(Pawn pawn, out RimEducationCoursePreview preview, out string errorKey)
        {
            preview = default;

            if (!TryGetEligibleTiers(pawn, out ThingComp comp, out Def currentDef, out Def nextDef, out errorKey))
                return false;

            float rateFactor = educationRateFactor(null, pawn);
            float educationSpeedSetting = getEducationSpeed(getSettingsInstance());
            float difficulty = getDifficulty(nextDef);
            float effectiveMultiplier = 1f / (1f + difficulty) * rateFactor * educationSpeedSetting;
            float effectiveGain = RawCourseReward * effectiveMultiplier;

            if (effectiveGain <= 0f) { errorKey = "SettlementServices.Error.RimEducationZeroRate"; return false; }

            float currentProgress = getEducationProgress(comp);
            float cappedPredicted = UnityEngine.Mathf.Min(1f, currentProgress + effectiveGain);

            preview = new RimEducationCoursePreview(
                currentDef.LabelCap,
                nextDef.LabelCap,
                currentProgress,
                RawCourseReward,
                effectiveGain,
                cappedPredicted,
                cappedPredicted >= 1f);
            return true;
        }

        internal static float GetWorkloadRatio(Pawn pawn)
        {
            if (!TryGetEligibleTiers(pawn, out _, out _, out Def nextDef, out _)) return 1f;

            float difficulty = getDifficulty(nextDef);
            return UnityEngine.Mathf.Clamp((1f + difficulty) / (1f + ReferenceDifficulty), MinWorkloadRatio, MaxWorkloadRatio);
        }

        private static bool TryGetEligibleTiers(Pawn pawn, out ThingComp comp, out Def currentDef, out Def nextDef, out string errorKey)
        {
            comp = null;
            currentDef = null;
            nextDef = null;
            errorKey = null;

            if (state != BindingState.Ready) { errorKey = "SettlementServices.Error.RimEducationUnavailable"; return false; }
            if (pawn == null) { errorKey = "SettlementServices.Error.RimEducationNoComponent"; return false; }

            comp = FindComp(pawn);
            if (comp == null) { errorKey = "SettlementServices.Error.RimEducationNoComponent"; return false; }
            if (!shouldEverHaveEducation(comp)) { errorKey = "SettlementServices.Error.RimEducationNotEligible"; return false; }

            if (!tryGetEducation(comp, out currentDef)) { errorKey = "SettlementServices.Error.RimEducationNotEligible"; return false; }

            nextDef = getNext(currentDef);
            if (nextDef == null) { errorKey = "SettlementServices.Error.RimEducationFinalTier"; return false; }

            DevelopmentalStage? stageFilter = getDevelopmentalStageFilter(currentDef);
            if (stageFilter.HasValue && !stageFilter.Value.Has(pawn.DevelopmentalStage))
            {
                errorKey = "SettlementServices.Error.RimEducationDevelopmentalStage";
                return false;
            }

            return true;
        }

        internal static RimEducationAwardResult TryAwardCourse(Pawn pawn, float rawProgress)
        {
            if (state != BindingState.Ready || pawn == null || pawn.Destroyed)
                return new RimEducationAwardResult(false, false, "adapter not ready or pawn missing");

            ThingComp comp = FindComp(pawn);
            if (comp == null) return new RimEducationAwardResult(false, false, "pawn lacks CompPawnEducation");
            if (!shouldEverHaveEducation(comp)) return new RimEducationAwardResult(false, false, "pawn is no longer eligible for education");
            if (!tryGetEducation(comp, out Def before)) return new RimEducationAwardResult(false, false, "current education tier could not be read");

            float sliceAmount = rawProgress / AwardSliceCount;
            bool appliedAny = false;
            bool tierAttained = false;

            for (int i = 0; i < AwardSliceCount; i++)
            {
                if (!tryLearnForEducation(pawn, null, sliceAmount)) break;
                appliedAny = true;

                if (!tryGetEducation(comp, out Def after)) break;
                if (!ReferenceEquals(after, before))
                {
                    tierAttained = true;
                    break;
                }
            }

            if (!appliedAny) return new RimEducationAwardResult(false, false, "no effective progress applied (zero rate or ineligible)");
            return new RimEducationAwardResult(true, tierAttained, null);
        }

        private static ThingComp FindComp(Pawn pawn)
        {
            var comps = pawn.AllComps;
            if (comps == null) return null;
            for (int i = 0; i < comps.Count; i++)
                if (compType.IsInstanceOfType(comps[i])) return comps[i];
            return null;
        }

        private static void FailBind(string reason)
        {
            state = BindingState.Incompatible;
            compType = null;
            tryLearnForEducation = null;
            tryGetEducation = null;
            getEducationProgress = null;
            shouldEverHaveEducation = null;
            educationRateFactor = null;
            getNext = null;
            getDifficulty = null;
            getDevelopmentalStageFilter = null;
            getSettingsInstance = null;
            getEducationSpeed = null;
            SupportLog.Warning($"Rim Education is installed but its API shape doesn't match what this mod expects ({reason}); the education course will stay unavailable.");
        }

        private static Func<Pawn, Pawn, float, bool> CompileStaticTryLearn(Type declaringType, MethodInfo method)
        {
            ParameterExpression pawnParam = Expression.Parameter(typeof(Pawn), "pawn");
            ParameterExpression teacherParam = Expression.Parameter(typeof(Pawn), "teacher");
            ParameterExpression amountParam = Expression.Parameter(typeof(float), "amount");
            Expression call = Expression.Call(null, method, pawnParam, teacherParam, amountParam);
            return Expression.Lambda<Func<Pawn, Pawn, float, bool>>(call, pawnParam, teacherParam, amountParam).Compile();
        }

        private static Func<Pawn, Pawn, float> CompileStaticRateFactor(MethodInfo method)
        {
            ParameterExpression teacherParam = Expression.Parameter(typeof(Pawn), "teacher");
            ParameterExpression studentParam = Expression.Parameter(typeof(Pawn), "student");
            Expression call = Expression.Call(null, method, teacherParam, studentParam);
            return Expression.Lambda<Func<Pawn, Pawn, float>>(call, teacherParam, studentParam).Compile();
        }

        private static Func<Verse.ModSettings> CompileStaticModSettingsGetter(PropertyInfo staticProp)
        {
            Expression access = Expression.Property(null, staticProp);
            Expression converted = Expression.Convert(access, typeof(Verse.ModSettings));
            return Expression.Lambda<Func<Verse.ModSettings>>(converted).Compile();
        }

        private static Func<ThingComp, float> CompileInstanceFloatGetter(Type declaringType, PropertyInfo instanceProp)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(ThingComp), "comp");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression access = Expression.Property(typedInstance, instanceProp);
            return Expression.Lambda<Func<ThingComp, float>>(access, instanceParam).Compile();
        }

        private static Func<ThingComp, bool> CompileInstanceBoolMethodNoArgs(Type declaringType, MethodInfo method)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(ThingComp), "comp");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression call = Expression.Call(typedInstance, method);
            return Expression.Lambda<Func<ThingComp, bool>>(call, instanceParam).Compile();
        }

        private static TryGetEducationDelegate CompileTryGetEducation(Type declaringType, MethodInfo method, Type educationDefType)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(ThingComp), "comp");
            ParameterExpression outParam = Expression.Parameter(typeof(Def).MakeByRefType(), "education");
            ParameterExpression localVar = Expression.Variable(educationDefType, "local");
            ParameterExpression resultVar = Expression.Variable(typeof(bool), "result");

            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression call = Expression.Call(typedInstance, method, localVar);
            Expression assignResult = Expression.Assign(resultVar, call);
            Expression assignOut = Expression.Assign(outParam, Expression.Convert(localVar, typeof(Def)));
            Expression block = Expression.Block(new[] { localVar, resultVar }, assignResult, assignOut, resultVar);

            return Expression.Lambda<TryGetEducationDelegate>(block, instanceParam, outParam).Compile();
        }

        private static Func<Def, Def> CompileDefField(Type declaringType, FieldInfo field)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(Def), "def");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression access = Expression.Field(typedInstance, field);
            Expression converted = Expression.Convert(access, typeof(Def));
            return Expression.Lambda<Func<Def, Def>>(converted, instanceParam).Compile();
        }

        private static Func<Def, float> CompileFloatField(Type declaringType, FieldInfo field)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(Def), "def");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression access = Expression.Field(typedInstance, field);
            return Expression.Lambda<Func<Def, float>>(access, instanceParam).Compile();
        }

        private static Func<Def, DevelopmentalStage?> CompileDevelopmentalStageField(Type declaringType, FieldInfo field)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(Def), "def");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression access = Expression.Field(typedInstance, field);
            return Expression.Lambda<Func<Def, DevelopmentalStage?>>(access, instanceParam).Compile();
        }

        private static Func<Verse.ModSettings, float> CompileInstanceFloatField(Type declaringType, FieldInfo field)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(Verse.ModSettings), "settings");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression access = Expression.Field(typedInstance, field);
            return Expression.Lambda<Func<Verse.ModSettings, float>>(access, instanceParam).Compile();
        }
    }
}

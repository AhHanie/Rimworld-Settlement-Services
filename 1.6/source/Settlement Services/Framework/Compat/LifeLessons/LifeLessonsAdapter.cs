using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Verse;

namespace Settlement_Services.Framework.Compat.LifeLessons
{
    internal static class LifeLessonsAdapter
    {
        private enum BindingState { Uninitialized, Unavailable, Ready, Incompatible }

        private const string PackageId = "GhostData.lifelessons";
        private const string AssemblySimpleName = "LifeLessons";
        private const string OptionKeyPrefix = "LifeLessons.Proficiency:";

        private static BindingState state = BindingState.Uninitialized;

        private static Func<Pawn, ThingComp> getComp;
        private static Func<Def, bool> isEnabled;
        private static Func<Def, bool> isTeachableInClass;
        private static Func<Def, Def> getCategory;
        private static Func<Def, int> getCostPractical;
        private static Func<Def, int> getCostTheoretical;
        private static Func<ThingComp, Def, bool> canLearn;
        private static Func<ThingComp, Def, bool, bool> tryGainProficiency;
        private static Func<IEnumerable> getAllProficiencyDefs;

        internal static bool IsReady => state == BindingState.Ready;

        internal static readonly ILifeLessonsProficiencyGateway Instance = new AdapterGateway();

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
            if (assembly == null) { FailBind("Life Lessons assembly not found"); return; }

            Type proficiencyDefType = assembly.GetType("LifeLessons.ProficiencyDef");
            Type proficiencyCompType = assembly.GetType("LifeLessons.ProficiencyComp");
            if (proficiencyDefType == null || proficiencyCompType == null) { FailBind("core types not found"); return; }

            if (!typeof(Def).IsAssignableFrom(proficiencyDefType)) { FailBind("ProficiencyDef is not a Def"); return; }
            if (!typeof(ThingComp).IsAssignableFrom(proficiencyCompType)) { FailBind("ProficiencyComp is not a ThingComp"); return; }

            FieldInfo enabledField = proficiencyDefType.GetField("enabled", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo canBeTaughtField = proficiencyDefType.GetField("canBeTaughtInClass", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo categoryField = proficiencyDefType.GetField("category", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo costPracticalField = proficiencyDefType.GetField("costPractical", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo costTheoreticalField = proficiencyDefType.GetField("costTheoretical", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo canLearnMethod = proficiencyCompType.GetMethod("CanLearn", BindingFlags.Public | BindingFlags.Instance, null, new[] { proficiencyDefType }, null);
            MethodInfo tryGainMethod = proficiencyCompType.GetMethod("TryGainProficiency", BindingFlags.Public | BindingFlags.Instance, null, new[] { proficiencyDefType, typeof(bool) }, null);

            Type defDatabaseType = typeof(DefDatabase<>).MakeGenericType(proficiencyDefType);
            PropertyInfo allDefsProp = defDatabaseType.GetProperty("AllDefsListForReading", BindingFlags.Public | BindingFlags.Static);

            MethodInfo tryGetCompOpen = typeof(ThingCompUtility).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "TryGetComp" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Thing));

            bool shapeValid = enabledField != null && enabledField.FieldType == typeof(bool)
                && canBeTaughtField != null && canBeTaughtField.FieldType == typeof(bool)
                && categoryField != null && typeof(Def).IsAssignableFrom(categoryField.FieldType)
                && costPracticalField != null && costPracticalField.FieldType == typeof(int)
                && costTheoreticalField != null && costTheoreticalField.FieldType == typeof(int)
                && canLearnMethod != null && canLearnMethod.ReturnType == typeof(bool)
                && tryGainMethod != null && tryGainMethod.ReturnType == typeof(bool)
                && allDefsProp != null && typeof(IEnumerable).IsAssignableFrom(allDefsProp.PropertyType)
                && tryGetCompOpen != null;

            if (!shapeValid) { FailBind("member shape mismatch"); return; }

            isEnabled = CompileDefBoolField(proficiencyDefType, enabledField);
            isTeachableInClass = CompileDefBoolField(proficiencyDefType, canBeTaughtField);
            getCategory = CompileDefField(proficiencyDefType, categoryField);
            getCostPractical = CompileDefIntField(proficiencyDefType, costPracticalField);
            getCostTheoretical = CompileDefIntField(proficiencyDefType, costTheoreticalField);
            canLearn = CompileCanLearn(proficiencyCompType, canLearnMethod, proficiencyDefType);
            tryGainProficiency = CompileTryGainProficiency(proficiencyCompType, tryGainMethod, proficiencyDefType);
            getAllProficiencyDefs = CompileStaticEnumerableGetter(allDefsProp);
            getComp = CompileGetComp(tryGetCompOpen.MakeGenericMethod(proficiencyCompType));

            state = BindingState.Ready;
            SupportLog.Info("Life Lessons detected; proficiency training compatibility active.");
        }

        private static IEnumerable<Def> EnumerateDefs(IEnumerable source)
        {
            foreach (object raw in source)
                if (raw is Def def) yield return def;
        }

        private static IEnumerable<Def> EnumerateEligibleProficiencies(Pawn pawn)
        {
            if (state != BindingState.Ready || pawn == null || pawn.Destroyed || pawn.DevelopmentalStage.Baby())
                yield break;

            ThingComp comp = getComp(pawn);
            if (comp == null) yield break;

            foreach (Def proficiency in EnumerateDefs(getAllProficiencyDefs()))
            {
                if (!isEnabled(proficiency)) continue;
                if (!isTeachableInClass(proficiency)) continue;
                if (!canLearn(comp, proficiency)) continue;

                yield return proficiency;
            }
        }

        private static string OptionKeyFor(Def proficiency) => OptionKeyPrefix + proficiency.defName;

        private static int TotalLearningCostFor(Def proficiency) => getCostPractical(proficiency) + getCostTheoretical(proficiency);

        private static IEnumerable<LifeLessonsProficiencyOption> GetEligibleProficiencies(Pawn pawn)
        {
            foreach (Def proficiency in EnumerateEligibleProficiencies(pawn))
                yield return new LifeLessonsProficiencyOption(OptionKeyFor(proficiency), proficiency.LabelCap, getCategory(proficiency)?.LabelCap ?? string.Empty, TotalLearningCostFor(proficiency));
        }

        private static bool TryResolveProficiency(Pawn pawn, string optionKey, out LifeLessonsProficiencyOption option)
        {
            foreach (Def proficiency in EnumerateEligibleProficiencies(pawn))
            {
                if (OptionKeyFor(proficiency) != optionKey) continue;
                option = new LifeLessonsProficiencyOption(optionKey, proficiency.LabelCap, getCategory(proficiency)?.LabelCap ?? string.Empty, TotalLearningCostFor(proficiency));
                return true;
            }

            option = default;
            return false;
        }

        private static LifeLessonsAwardResult TryGrantProficiency(Pawn pawn, string optionKey)
        {
            if (state != BindingState.Ready || pawn == null || pawn.Destroyed)
                return LifeLessonsAwardResult.Fail("adapter not ready or pawn missing");

            ThingComp comp = getComp(pawn);
            if (comp == null) return LifeLessonsAwardResult.Fail("pawn lacks ProficiencyComp");

            foreach (Def proficiency in EnumerateEligibleProficiencies(pawn))
            {
                if (OptionKeyFor(proficiency) != optionKey) continue;
                return tryGainProficiency(comp, proficiency, false)
                    ? LifeLessonsAwardResult.Ok()
                    : LifeLessonsAwardResult.Fail("Life Lessons declined to grant the proficiency");
            }

            return LifeLessonsAwardResult.Fail("pawn is no longer eligible for the selected proficiency, or it no longer exists");
        }

        private sealed class AdapterGateway : ILifeLessonsProficiencyGateway
        {
            public bool IsReady => LifeLessonsAdapter.IsReady;

            public IEnumerable<LifeLessonsProficiencyOption> GetEligibleProficiencies(Pawn pawn) =>
                LifeLessonsAdapter.GetEligibleProficiencies(pawn);

            public bool TryResolveProficiency(Pawn pawn, string optionKey, out LifeLessonsProficiencyOption option) =>
                LifeLessonsAdapter.TryResolveProficiency(pawn, optionKey, out option);

            public LifeLessonsAwardResult TryGrantProficiency(Pawn pawn, string optionKey) =>
                LifeLessonsAdapter.TryGrantProficiency(pawn, optionKey);
        }

        private static void FailBind(string reason)
        {
            state = BindingState.Incompatible;
            getComp = null;
            isEnabled = null;
            isTeachableInClass = null;
            getCategory = null;
            getCostPractical = null;
            getCostTheoretical = null;
            canLearn = null;
            tryGainProficiency = null;
            getAllProficiencyDefs = null;
            SupportLog.Warning($"Life Lessons is installed but its API shape doesn't match what this mod expects ({reason}); proficiency training will stay unavailable.");
        }

        private static Func<Def, bool> CompileDefBoolField(Type declaringType, FieldInfo field)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(Def), "def");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression access = Expression.Field(typedInstance, field);
            return Expression.Lambda<Func<Def, bool>>(access, instanceParam).Compile();
        }

        private static Func<Def, int> CompileDefIntField(Type declaringType, FieldInfo field)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(Def), "def");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression access = Expression.Field(typedInstance, field);
            return Expression.Lambda<Func<Def, int>>(access, instanceParam).Compile();
        }

        private static Func<Def, Def> CompileDefField(Type declaringType, FieldInfo field)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(Def), "def");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression access = Expression.Field(typedInstance, field);
            Expression converted = Expression.Convert(access, typeof(Def));
            return Expression.Lambda<Func<Def, Def>>(converted, instanceParam).Compile();
        }

        private static Func<ThingComp, Def, bool> CompileCanLearn(Type declaringType, MethodInfo method, Type defParamType)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(ThingComp), "comp");
            ParameterExpression defParam = Expression.Parameter(typeof(Def), "def");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression typedDef = Expression.Convert(defParam, defParamType);
            Expression call = Expression.Call(typedInstance, method, typedDef);
            return Expression.Lambda<Func<ThingComp, Def, bool>>(call, instanceParam, defParam).Compile();
        }

        private static Func<ThingComp, Def, bool, bool> CompileTryGainProficiency(Type declaringType, MethodInfo method, Type defParamType)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(ThingComp), "comp");
            ParameterExpression defParam = Expression.Parameter(typeof(Def), "def");
            ParameterExpression forceParam = Expression.Parameter(typeof(bool), "force");
            Expression typedInstance = Expression.Convert(instanceParam, declaringType);
            Expression typedDef = Expression.Convert(defParam, defParamType);
            Expression call = Expression.Call(typedInstance, method, typedDef, forceParam);
            return Expression.Lambda<Func<ThingComp, Def, bool, bool>>(call, instanceParam, defParam, forceParam).Compile();
        }

        private static Func<IEnumerable> CompileStaticEnumerableGetter(PropertyInfo property)
        {
            Expression access = Expression.Property(null, property);
            Expression converted = Expression.Convert(access, typeof(IEnumerable));
            return Expression.Lambda<Func<IEnumerable>>(converted).Compile();
        }

        private static Func<Pawn, ThingComp> CompileGetComp(MethodInfo closedTryGetComp)
        {
            ParameterExpression pawnParam = Expression.Parameter(typeof(Pawn), "pawn");
            Expression thingArg = Expression.Convert(pawnParam, typeof(Thing));
            Expression call = Expression.Call(null, closedTryGetComp, thingArg);
            Expression converted = Expression.Convert(call, typeof(ThingComp));
            return Expression.Lambda<Func<Pawn, ThingComp>>(converted, pawnParam).Compile();
        }
    }
}

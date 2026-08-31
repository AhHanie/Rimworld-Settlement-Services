using System;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace Settlement_Services.Framework.Compat
{
    public static class AndroidsAdapter
    {
        private static bool resolveAttempted;
        private static bool resolveFailed;

        private static MethodInfo isAndroidMethod;
        private static Type geneSyntheticBodyType;
        private static FieldInfo autoRepairField;
        private static Type androidPartHediffType;
        private static Type androidReactorHediffType;

        public static Type AndroidPartHediffType { get { EnsureResolved(); return resolveFailed ? null : androidPartHediffType; } }
        public static Type ReactorHediffType { get { EnsureResolved(); return resolveFailed ? null : androidReactorHediffType; } }

        public static bool IsAndroid(Thing thing)
        {
            EnsureResolved();
            if (resolveFailed || !(thing is Pawn pawn) || pawn.genes == null) return false;
            return (bool)isAndroidMethod.Invoke(null, new object[] { pawn });
        }

        public static bool AutoRepairEnabled(Pawn android)
        {
            EnsureResolved();
            if (resolveFailed || geneSyntheticBodyType == null || autoRepairField == null || android?.genes == null) return true;
            object gene = android.genes.GenesListForReading.FirstOrDefault(g => geneSyntheticBodyType.IsInstanceOfType(g));
            return gene == null || (bool)autoRepairField.GetValue(gene);
        }

        public static HediffDef NeutroLossHediffDef => DefDatabase<HediffDef>.GetNamedSilentFail("VREA_NeutroLoss");
        public static GeneDef NeutroCirculationGeneDef => DefDatabase<GeneDef>.GetNamedSilentFail("VREA_NeutroCirculation");
        public static ThingDef NeutroamineThingDef => DefDatabase<ThingDef>.GetNamedSilentFail("Neutroamine");

        private static void EnsureResolved()
        {
            if (resolveAttempted) return;
            resolveAttempted = true;

            Type utilsType = SettlementServicesModCompat.ResolveOptionalType("Utils", "VREAndroids");
            geneSyntheticBodyType = SettlementServicesModCompat.ResolveOptionalType("Gene_SyntheticBody", "VREAndroids");
            androidPartHediffType = SettlementServicesModCompat.ResolveOptionalType("Hediff_AndroidPart", "VREAndroids");
            androidReactorHediffType = SettlementServicesModCompat.ResolveOptionalType("Hediff_AndroidReactor", "VREAndroids");
            if (utilsType == null || androidPartHediffType == null) { resolveFailed = true; return; }

            isAndroidMethod = utilsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "IsAndroid" && m.GetParameters().Length == 1);
            autoRepairField = geneSyntheticBodyType?.GetField("autoRepair", BindingFlags.Public | BindingFlags.Instance);

            if (isAndroidMethod == null)
            {
                resolveFailed = true;
                SupportLog.Info("Vanilla Races Expanded - Android is installed but its API shape doesn't match what this mod expects; android services will stay unavailable.");
            }
        }
    }
}

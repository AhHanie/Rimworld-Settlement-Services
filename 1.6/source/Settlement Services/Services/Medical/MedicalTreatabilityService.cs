using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Settlement_Services.Services.Medical
{
    internal static class MedicalTreatabilityService
    {
        public const string RehabilitationTierKey = "Rehabilitation";

        public enum TreatmentAction
        {
            CureInjury,
            CureDisease,
            TendOnly,
            CureAddiction,
        }

        public readonly struct TreatableCondition
        {
            public readonly Hediff hediff;
            public readonly TreatmentAction action;

            public TreatableCondition(Hediff hediff, TreatmentAction action)
            {
                this.hediff = hediff;
                this.action = action;
            }

            public string Key => $"{action}:{hediff.def.defName}:{(hediff.Part?.Index.ToString() ?? "-")}";
            public string Label => hediff.LabelCap;
        }

        public static bool AllowsAddictionTreatment(string selectedTierKey) => selectedTierKey == RehabilitationTierKey;

        public static bool IsCurableDisease(Hediff h) =>
            h.TryGetComp<HediffComp_Immunizable>() != null && !h.def.chronic && h.def.everCurableByItem;

        public static bool IsCurableAddiction(Hediff h) => h is Hediff_Addiction && h.def.everCurableByItem;

        public static IEnumerable<TreatableCondition> FindTreatable(Pawn pawn, string selectedTierKey)
        {
            if (pawn?.health?.hediffSet == null) yield break;

            bool allowAddictions = AllowsAddictionTreatment(selectedTierKey);
            foreach (Hediff h in pawn.health.hediffSet.hediffs)
            {
                if (h is Hediff_Injury injury)
                {
                    if (!injury.IsPermanent()) yield return new TreatableCondition(h, TreatmentAction.CureInjury);
                    continue;
                }

                if (IsCurableDisease(h)) { yield return new TreatableCondition(h, TreatmentAction.CureDisease); continue; }

                if (h is Hediff_Addiction)
                {
                    if (allowAddictions && IsCurableAddiction(h)) yield return new TreatableCondition(h, TreatmentAction.CureAddiction);
                    continue;
                }

                if (h.TendableNow()) yield return new TreatableCondition(h, TreatmentAction.TendOnly);
            }
        }
    }
}

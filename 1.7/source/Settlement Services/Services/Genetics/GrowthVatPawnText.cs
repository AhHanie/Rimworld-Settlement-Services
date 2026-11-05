using Verse;

namespace Settlement_Services.Services.Genetics
{
    internal static class GrowthVatPawnText
    {
        public static string FullName(Pawn pawn)
        {
            if (pawn == null) return null;

            string full = pawn.Name?.ToStringFull;
            if (!full.NullOrEmpty()) return full;

            string label = pawn.LabelCap;
            return label.NullOrEmpty() ? null : label;
        }

        public static string SelectedTargetText(Pawn pawn) =>
            "SettlementServices.Label.GrowthVatSelectedChild".Translate(FullName(pawn) ?? pawn.LabelCap, pawn.ageTracker.AgeBiologicalYears).ToString();
    }
}

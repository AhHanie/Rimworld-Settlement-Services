using System.Linq;
using RimWorld;
using Verse;

namespace Settlement_Services.Services.Genetics
{
    internal static class GeneEligibilityService
    {
        public static bool IsValidRecipient(Pawn pawn) =>
            pawn?.genes != null && pawn.RaceProps.Humanlike && !pawn.IsQuestLodger()
            && (pawn.IsColonist || pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony);

        public static bool IsValidExtractionSource(Pawn pawn, out string reasonKey)
        {
            bool validMember = pawn?.genes != null && pawn.RaceProps.Humanlike && !pawn.IsQuestLodger()
                && (pawn.IsColonist || pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony
                    || (pawn.IsColonySubhuman && pawn.IsGhoul));
            if (!validMember) { reasonKey = "SettlementServices.Error.InvalidGeneTarget"; return false; }
            if (pawn.health.hediffSet.HasHediff(HediffDefOf.XenogerminationComa))
            { reasonKey = "SettlementServices.Error.InXenogerminationComa"; return false; }
            if (!pawn.genes.GenesListForReading.Any(g => g.def.passOnDirectly))
            { reasonKey = "SettlementServices.Error.NoExtractableGenes"; return false; }
            if (!pawn.genes.GenesListForReading.Any(g => g.def.biostatArc == 0))
            { reasonKey = "SettlementServices.Error.NoNonArchiteGenes"; return false; }

            reasonKey = null;
            return true;
        }
    }
}

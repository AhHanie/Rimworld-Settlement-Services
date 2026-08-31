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

        public static bool CanImplant(Pawn pawn, Xenogerm xenogerm, out string reasonKey)
        {
            if (!IsValidRecipient(pawn)) { reasonKey = "SettlementServices.Error.InvalidGeneTarget"; return false; }
            if (pawn.health.hediffSet.HasHediff(HediffDefOf.XenogerminationComa))
            { reasonKey = "SettlementServices.Error.InXenogerminationComa"; return false; }
            if (xenogerm?.GeneSet == null || GeneUtility.MetabolismAfterImplanting(pawn, xenogerm.GeneSet) < GeneTuning.BiostatRange.TrueMin)
            { reasonKey = "SettlementServices.Error.ResultingMetabolismTooLow"; return false; }
            if (xenogerm.PawnIdeoDisallowsImplanting(pawn))
            { reasonKey = "SettlementServices.Error.IdeoligionForbidsImplanting"; return false; }

            reasonKey = null;
            return true;
        }

        public static bool IsValidExtractionSource(Pawn pawn, out string reasonKey)
        {
            if (!IsValidRecipient(pawn)) { reasonKey = "SettlementServices.Error.InvalidGeneTarget"; return false; }
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

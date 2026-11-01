using RimWorld;
using Verse;

namespace Settlement_Services.Services.Correction
{
    internal enum CaptiveKind
    {
        Prisoner,
        Slave,
    }

    internal static class CorrectionEligibility
    {
        internal static bool MatchesKind(Pawn pawn, CaptiveKind kind)
        {
            if (pawn == null || pawn.guest == null) return false;
            return kind == CaptiveKind.Prisoner
                ? pawn.IsPrisonerOfColony
                : ModsConfig.IdeologyActive && pawn.IsSlaveOfColony;
        }

        internal static string Check(Thing target, CaptiveKind kind)
        {
            if (!(target is Pawn pawn) || pawn.Destroyed || pawn.Dead) return "SettlementServices.Error.TargetNoLongerExists";
            if (!pawn.RaceProps.Humanlike || pawn.guest == null) return "SettlementServices.Error.InvalidTarget";
            if (pawn.IsQuestLodger() || pawn.IsSubhuman || pawn.IsShambler || pawn.IsWildMan()) return "SettlementServices.Error.CorrectionProtectedTarget";

            if (!MatchesKind(pawn, kind))
                return kind == CaptiveKind.Prisoner
                    ? "SettlementServices.Error.CorrectionNotPrisoner"
                    : "SettlementServices.Error.CorrectionNotSlave";

            if (!pawn.guest.Recruitable) return "SettlementServices.Error.CorrectionUnwavering";
            return null;
        }
    }
}

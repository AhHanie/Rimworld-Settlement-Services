using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;

namespace Settlement_Services.Framework.Board
{
    internal static class BoardPawnEligibility
    {
        private const float DangerousSeverityFraction = 0.5f;
        private const int PregnancyMarginTicks = 60000;

        internal static string GetBlockReasonKey(Pawn pawn, Caravan caravan, BoardJobRecord job)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead) return "SettlementServices.JobBoard.Block.Gone";
            if (caravan == null || caravan.Destroyed || !caravan.PawnsListForReading.Contains(pawn)) return "SettlementServices.JobBoard.Block.NotInCaravan";
            if (!pawn.RaceProps.Humanlike || pawn.skills == null) return "SettlementServices.JobBoard.Block.NotHumanlike";
            if (!pawn.IsFreeColonist || pawn.IsQuestLodger() || pawn.IsMutant) return "SettlementServices.JobBoard.Block.NotFreeColonist";
            if (!pawn.DevelopmentalStage.Adult()) return "SettlementServices.JobBoard.Block.NotAdult";
            if (pawn.IsBorrowedByAnyFaction() || QuestUtility.IsReservedByQuestOrQuestBeingGenerated(pawn)) return "SettlementServices.JobBoard.Block.QuestReserved";
            if (pawn.Suspended) return "SettlementServices.JobBoard.Block.Suspended";
            if (pawn.Downed) return "SettlementServices.JobBoard.Block.Downed";
            if (pawn.InMentalState) return "SettlementServices.JobBoard.Block.MentalState";
            if (pawn.Drafted) return "SettlementServices.JobBoard.Block.Drafted";

            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain != null && (domain.IsPawnHeldByBoard(pawn) || domain.IsTargetReserved(pawn))) return "SettlementServices.JobBoard.Block.AlreadyAssigned";

            if (pawn.health.hediffSet.BleedRateTotal > 0.01f) return "SettlementServices.JobBoard.Block.Bleeding";
            if (IsDangerouslySick(pawn)) return "SettlementServices.JobBoard.Block.DangerouslySick";
            if (WouldGiveBirthDuringJob(pawn, job)) return "SettlementServices.JobBoard.Block.Pregnant";
            return null;
        }

        private static bool IsDangerouslySick(Pawn pawn)
        {
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff.def.lethalSeverity > 0f && hediff.Severity >= hediff.def.lethalSeverity * DangerousSeverityFraction) return true;
            }
            return false;
        }

        private static bool WouldGiveBirthDuringJob(Pawn pawn, BoardJobRecord job)
        {
            if (job == null || HediffDefOf.PregnantHuman == null) return false;

            Hediff pregnancy = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.PregnantHuman);
            if (pregnancy == null) return false;

            float remainingTicks = (1f - pregnancy.Severity) * pawn.RaceProps.gestationPeriodDays * 60000f;
            return remainingTicks < job.workTicks + PregnancyMarginTicks;
        }
    }
}

using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;

namespace Settlement_Services.Framework.Board
{
    internal static class BoardJobNotifier
    {
        internal static void NotifyStarted(BoardJobRecord job, Settlement settlement)
        {
            Messages.Message(
                "SettlementServices.JobBoard.Message.Started".Translate(job.partySize, job.title, settlement?.LabelCap ?? string.Empty, FormatDuration(job.workTicks)),
                settlement != null ? new LookTargets(settlement) : LookTargets.Invalid,
                MessageTypeDefOf.PositiveEvent,
                historical: false);
        }

        internal static void NotifyFinished(BoardJobRecord job)
        {
            if (job.resultNotified) return;
            job.resultNotified = true;

            Settlement settlement = WorldObjectLookup.ResolveSettlement(job.settlementWorldObjectId);
            string settlementLabel = settlement?.LabelCap ?? "SettlementServices.JobBoard.UnknownSettlement".Translate().Resolve();

            Caravan caravan = BoardPawnCustodyService.FindHoldingCaravan(job);
            LookTargets targets = caravan != null ? new LookTargets(caravan)
                : settlement != null ? new LookTargets(settlement)
                : LookTargets.Invalid;

            switch (job.status)
            {
                case BoardJobStatus.Completed:
                    Find.LetterStack.ReceiveLetter(
                        "SettlementServices.JobBoard.Letter.CompletedLabel".Translate(),
                        "SettlementServices.JobBoard.Letter.CompletedText".Translate(job.title, settlementLabel, job.reward.label ?? string.Empty, job.reward.marketValue.ToStringMoney()),
                        LetterDefOf.PositiveEvent, targets);
                    break;

                case BoardJobStatus.Failed:
                    Find.LetterStack.ReceiveLetter(
                        "SettlementServices.JobBoard.Letter.FailedLabel".Translate(),
                        "SettlementServices.JobBoard.Letter.FailedText".Translate(job.title, settlementLabel, FailureText(job)),
                        LetterDefOf.NegativeEvent, targets);
                    break;

                case BoardJobStatus.Cancelled:
                    Messages.Message("SettlementServices.JobBoard.Message.Cancelled".Translate(job.title, settlementLabel), targets, MessageTypeDefOf.NeutralEvent, historical: false);
                    break;
            }
        }

        internal static string FailureText(BoardJobRecord job) =>
            job.failureKey.NullOrEmpty() ? string.Empty : job.failureKey.Translate().Resolve();

        internal static string FormatDuration(int ticks) => ticks.ToStringTicksToPeriod();
    }
}

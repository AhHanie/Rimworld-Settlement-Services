using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Correction
{
    public abstract class CorrectionServiceWorker : SettlementServiceWorker
    {
        internal abstract CaptiveKind Kind { get; }

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (ctx.SelectedTarget == null) return ServiceAvailabilityReport.Available;
            string errorKey = CorrectionEligibility.Check(ctx.SelectedTarget, Kind);
            return errorKey == null ? ServiceAvailabilityReport.Available : ServiceAvailabilityReport.Unavailable(errorKey);
        }

        public override bool ShouldListTarget(Thing target) => CorrectionEligibility.MatchesKind(target as Pawn, Kind);

        public override string GetTargetBlockReason(Thing target)
        {
            string errorKey = CorrectionEligibility.Check(target, Kind);
            return errorKey == null ? null : errorKey.Translate().ToString();
        }

        public override string ValidateUnitRequest(SettlementServiceRequest request) =>
            CorrectionEligibility.Check(request.target?.thing, Kind);

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) =>
            Enumerable.Empty<ServiceLineItem>();

        public override ServiceStartResult Start(ServiceJobContext ctx)
        {
            string errorKey = CorrectionEligibility.Check(ctx.CurrentTargetThing, Kind);
            return errorKey == null ? ServiceStartResult.Ok : ServiceStartResult.Fail(errorKey);
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            string errorKey = CorrectionEligibility.Check(ctx.CurrentTargetThing, Kind);
            if (errorKey != null) return ServiceCompletionResult.Fail(errorKey);

            Pawn pawn = (Pawn)ctx.CurrentTargetThing;
            InteractionWorker_RecruitAttempt.DoRecruit(null, pawn, useAudiovisualEffects: false);

            if (Kind == CaptiveKind.Slave && pawn.kindDef != Faction.OfPlayer.def.basicMemberKind)
                pawn.ChangeKind(Faction.OfPlayer.def.basicMemberKind);

            if (pawn.Faction != Faction.OfPlayer || !pawn.IsFreeNonSlaveColonist || pawn.IsPrisoner || pawn.IsSlave)
            {
                Logger.Error($"Job {ctx.Job.jobId}: {Kind} correction left {pawn.LabelShort} in an unexpected state (faction {pawn.Faction?.Name ?? "none"}, prisoner {pawn.IsPrisoner}, slave {pawn.IsSlave}) after recruitment.");
                return ServiceCompletionResult.Fail("SettlementServices.Error.CorrectionRecruitFailed");
            }

            Messages.Message("SettlementServices.Message.CorrectionSucceeded".Translate(pawn.LabelShortCap), pawn, MessageTypeDefOf.PositiveEvent);
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();
    }

    public sealed class PrisonerRehabilitationServiceWorker : CorrectionServiceWorker
    {
        internal override CaptiveKind Kind => CaptiveKind.Prisoner;
    }

    public sealed class SlaveIntegrationServiceWorker : CorrectionServiceWorker
    {
        internal override CaptiveKind Kind => CaptiveKind.Slave;
    }
}

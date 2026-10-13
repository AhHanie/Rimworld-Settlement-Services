using System.Collections.Generic;
using RimWorld;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Hospitality
{
    public class RumorGatheringServiceWorker : SettlementServiceWorker
    {
        private const int CooldownTicks = 5 * GenDate.TicksPerDay;

        private const string NoNewRumorsKey = "SettlementServices.Error.NoNewRumors";
        private const string SearchInProgressKey = "SettlementServices.Error.RumorSearchInProgress";
        private const string NoLeadsKey = "SettlementServices.Error.NoRumorLeadsAvailable";
        private const string TargetGoneKey = "SettlementServices.Error.TargetNoLongerExists";
        private const string SettlementGoneKey = "SettlementServices.Error.SettlementNoLongerExists";

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            string errorKey = SettlementAvailabilityError(ctx.Domain, ctx.Settlement?.ID ?? -1, -1);
            return errorKey == null ? ServiceAvailabilityReport.Available : ServiceAvailabilityReport.Unavailable(errorKey);
        }

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            string errorKey = SettlementAvailabilityError(SettlementServicesWorldComponent.Current, request.settlement?.ID ?? -1, -1);
            if (errorKey != null) return errorKey;

            if (!IsLiveColonist(request.target.thing as Pawn)) return TargetGoneKey;

            return RumorQuestGenerator.CanFindCandidate(forceFresh: request.bookingTick >= 0) ? null : NoLeadsKey;
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override ServiceStartResult Start(ServiceJobContext ctx)
        {
            string errorKey = SettlementAvailabilityError(ctx.Domain, ctx.Job.settlementWorldObjectId, ctx.Job.jobId);
            if (errorKey != null) return ServiceStartResult.Fail(errorKey);

            if (!IsLiveColonist(ctx.CurrentTarget?.liveThing as Pawn)) return ServiceStartResult.Fail(TargetGoneKey);

            return RumorQuestGenerator.CanFindCandidate(forceFresh: true) ? ServiceStartResult.Ok : ServiceStartResult.Fail(NoLeadsKey);
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            int settlementId = ctx.Job.settlementWorldObjectId;
            if (ctx.Domain.HasRumorQuestFor(settlementId, ctx.Job.jobId)) return ServiceCompletionResult.Ok();

            if (!RumorQuestGenerator.TryGenerate(ctx.ResolveSettlement(), out string errorKey))
                return ServiceCompletionResult.Fail(errorKey);

            ctx.Domain.RecordRumorQuestGenerated(settlementId, ctx.Job.jobId, CooldownTicks);
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private string SettlementAvailabilityError(SettlementServicesWorldComponent domain, int settlementId, int excludingJobId)
        {
            if (domain == null || settlementId < 0) return SettlementGoneKey;
            if (domain.RumorCooldownRemainingTicks(settlementId) > 0) return NoNewRumorsKey;
            if (domain.HasPendingServiceJob(settlementId, def.defName, excludingJobId)) return SearchInProgressKey;
            if (Find.AnyPlayerHomeMap == null) return NoLeadsKey;
            return null;
        }

        private static bool IsLiveColonist(Pawn pawn) =>
            pawn != null && !pawn.Destroyed && !pawn.Dead && pawn.IsColonist;
    }
}

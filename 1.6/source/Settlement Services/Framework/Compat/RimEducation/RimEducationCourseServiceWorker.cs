using System.Collections.Generic;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Framework.Compat.RimEducation
{
    public class RimEducationCourseServiceWorker : SettlementServiceWorker
    {
        internal const string DefName = "SettlementService_RimEducationCourse";

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!RimEducationAdapter.IsReady) return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.RimEducationUnavailable");
            if (!(ctx.SelectedTarget is Pawn pawn)) return ServiceAvailabilityReport.Available;

            return RimEducationAdapter.TryGetCoursePreview(pawn, out _, out string errorKey)
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable(errorKey);
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override IEnumerable<string> GetDisplaySummaryLines(SettlementServiceContext ctx)
        {
            if (ctx.SelectedTarget is Pawn pawn && RimEducationAdapter.TryGetCoursePreview(pawn, out RimEducationCoursePreview preview, out _))
                return BuildSummaryLines(preview);
            return new[] { "SettlementServices.Label.RimEducationSelectPawnPrompt".Translate().ToString() };
        }

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx) => ServiceCompletionResult.Ok();

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static IEnumerable<string> BuildSummaryLines(RimEducationCoursePreview preview)
        {
            yield return "SettlementServices.Label.RimEducationCurrentNext".Translate(preview.currentLabelCap, preview.nextLabelCap);
            yield return "SettlementServices.Label.RimEducationProgress".Translate(preview.currentProgress01.ToStringPercent());
            yield return "SettlementServices.Label.RimEducationExpectedGain".Translate(preview.effectiveGain.ToStringPercent(), preview.rawReward.ToStringPercent());
            yield return "SettlementServices.Label.RimEducationPredictedProgress".Translate(preview.cappedPredictedProgress01.ToStringPercent());
            if (preview.willAttainNextTier)
                yield return "SettlementServices.Label.RimEducationTierAttained".Translate(preview.nextLabelCap);
        }
    }
}

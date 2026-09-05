using System.Collections.Generic;
using System.Linq;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Framework.Compat.ProgressionEducation
{
    public class ProgressionEducationProficiencyServiceWorker : SettlementServiceWorker
    {
        internal const string DefName = "SettlementService_ProgressionEducationProficiencyTraining";

        private const string TrackGroupKey = "SettlementServices.Label.ProficiencyTrackChoice";

        public override bool UsesPerTargetOptionSelections => true;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!ProgressionEducationCompatibilityModule.Gateway.IsReady)
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.ProgressionEducationUnavailable");

            if (!(ctx.SelectedTarget is Pawn pawn)) return ServiceAvailabilityReport.Available;

            return ProgressionEducationCompatibilityModule.Gateway.GetEligiblePromotions(pawn).Any()
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoTrainableProficiency");
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) yield break;

            foreach (ProficiencyPromotionOption option in ProgressionEducationCompatibilityModule.Gateway.GetEligiblePromotions(pawn))
                yield return new ServiceDisplayOption
                {
                    key = option.optionKey,
                    label = "SettlementServices.Label.ProficiencyTrackOption".Translate(option.trackLabelCap, option.nextTierLabelCap),
                    description = "SettlementServices.Label.ProficiencyCurrentTier".Translate(option.currentTierLabelCap),
                    groupKey = TrackGroupKey,
                };
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            if (!(request.target.thing is Pawn pawn)) return null;

            List<string> selected = request.selectedOptionKeys ?? new List<string>();
            if (selected.Count != 1) return "SettlementServices.Error.NoProficiencyPromotionSelected";

            return ProgressionEducationCompatibilityModule.Gateway.TryResolvePromotion(pawn, selected[0], out _)
                ? null
                : "SettlementServices.Error.NoTrainableProficiency";
        }

        public override IEnumerable<string> GetDisplaySummaryLines(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn))
                return new[] { "SettlementServices.Label.ProficiencyPromotionSelectPawnPrompt".Translate().ToString() };

            string key = ctx.SelectedOptionKeys.FirstOrDefault();
            if (key != null && ProgressionEducationCompatibilityModule.Gateway.TryResolvePromotion(pawn, key, out ProficiencyPromotionOption option))
                return new[] { "SettlementServices.Label.ProficiencyPromotionPreview".Translate(option.trackLabelCap, option.currentTierLabelCap, option.nextTierLabelCap).ToString() };

            return new[] { "SettlementServices.Label.ProficiencyPromotionSelectPrompt".Translate().ToString() };
        }

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx) => ServiceCompletionResult.Ok();

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();
    }
}

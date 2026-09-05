using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Framework.Compat.LifeLessons
{
    public class LifeLessonsProficiencyServiceWorker : SettlementServiceWorker
    {
        internal const string DefName = "SettlementService_LifeLessonsProficiencyTraining";

        private const string ProficiencyGroupKey = "SettlementServices.Label.LifeLessonsProficiencyChoice";

        private const float ReferenceLearningCost = 1200f;
        private const float MinCostRatio = 0.3f;
        private const float MaxCostRatio = 3.5f;

        public override bool UsesPerTargetOptionSelections => true;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!LifeLessonsCompatibilityModule.Gateway.IsReady)
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.LifeLessonsUnavailable");

            if (!(ctx.SelectedTarget is Pawn pawn)) return ServiceAvailabilityReport.Available;

            return LifeLessonsCompatibilityModule.Gateway.GetEligibleProficiencies(pawn).Any()
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.LifeLessonsNoEligibleProficiency");
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) yield break;

            IEnumerable<LifeLessonsProficiencyOption> options = LifeLessonsCompatibilityModule.Gateway.GetEligibleProficiencies(pawn)
                .OrderBy(o => o.categoryLabelCap)
                .ThenBy(o => o.proficiencyLabelCap);

            foreach (LifeLessonsProficiencyOption option in options)
                yield return new ServiceDisplayOption
                {
                    key = option.optionKey,
                    label = option.proficiencyLabelCap,
                    description = "SettlementServices.Label.LifeLessonsProficiencyCategory".Translate(option.categoryLabelCap),
                    groupKey = ProficiencyGroupKey,
                };
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            if (!(request.target.thing is Pawn pawn)) return null;

            List<string> selected = request.selectedOptionKeys ?? new List<string>();
            if (selected.Count != 1) return "SettlementServices.Error.LifeLessonsNoProficiencySelected";

            return LifeLessonsCompatibilityModule.Gateway.TryResolveProficiency(pawn, selected[0], out _)
                ? null
                : "SettlementServices.Error.LifeLessonsNoEligibleProficiency";
        }

        public override float DurationMultiplierFor(SettlementServiceRequest request) => CostRatioFor(request);

        public override float WealthScaleAdditionFor(SettlementServiceRequest request) =>
            def.wealthScale * Mathf.Max(0f, CostRatioFor(request) - 1f);

        private static float CostRatioFor(SettlementServiceRequest request)
        {
            if (!(request.target.thing is Pawn pawn)) return 1f;

            string optionKey = request.selectedOptionKeys?.FirstOrDefault();
            if (optionKey == null) return 1f;

            return LifeLessonsCompatibilityModule.Gateway.TryResolveProficiency(pawn, optionKey, out LifeLessonsProficiencyOption option)
                ? Mathf.Clamp(option.totalLearningCost / ReferenceLearningCost, MinCostRatio, MaxCostRatio)
                : 1f;
        }

        public override IEnumerable<string> GetDisplaySummaryLines(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn))
                return new[] { "SettlementServices.Label.LifeLessonsSelectPawnPrompt".Translate().ToString() };

            string key = ctx.SelectedOptionKeys.FirstOrDefault();
            if (key != null && LifeLessonsCompatibilityModule.Gateway.TryResolveProficiency(pawn, key, out LifeLessonsProficiencyOption option))
                return new[] { "SettlementServices.Label.LifeLessonsProficiencyPreview".Translate(option.proficiencyLabelCap, option.categoryLabelCap).ToString() };

            return new[] { "SettlementServices.Label.LifeLessonsSelectPrompt".Translate().ToString() };
        }

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx) => ServiceCompletionResult.Ok();

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();
    }
}

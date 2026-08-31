using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Education
{
    public class SkillLessonServiceWorker : SettlementServiceWorker
    {
        private const string IntensityGroupKey = "SettlementServices.Label.LessonIntensityChoice";

        private SkillLessonServiceDef LessonDef => def as SkillLessonServiceDef;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) return ServiceAvailabilityReport.Available;
            return SkillEligibilityService.FindEligible(pawn).Any()
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoTrainableSkills");
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) yield break;

            foreach (SkillDef skillDef in SkillEligibilityService.FindEligible(pawn))
                yield return new ServiceDisplayOption { key = SkillEligibilityService.KeyFor(skillDef), label = skillDef.LabelCap, groupKey = SkillEligibilityService.SkillGroupKey, iconTexPath = SkillEligibilityService.IconPathFor(skillDef), groupColumnCount = 2 };

            if (LessonDef != null)
                foreach (SkillLessonOption option in LessonDef.lessonOptions)
                    yield return new ServiceDisplayOption { key = option.key, label = option.labelKey.Translate(), groupKey = IntensityGroupKey };
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            if (!(request.target.thing is Pawn pawn)) return null;
            SkillDef skillDef = SkillEligibilityService.FindByKey(request.selectedOptionKeys);
            if (skillDef == null) return null;
            return SkillEligibilityService.FindEligible(pawn).Contains(skillDef) ? null : "SettlementServices.Error.NoTrainableSkills";
        }

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override float DurationMultiplierFor(SettlementServiceRequest request) =>
            LessonDef?.OptionFor(request.selectedOptionKeys)?.durationMultiplier ?? 1f;

        public override float WealthScaleAdditionFor(SettlementServiceRequest request) =>
            LessonDef?.OptionFor(request.selectedOptionKeys)?.priceMultiplier ?? 0f;

        public override IEnumerable<string> GetDisplaySummaryLines(SettlementServiceContext ctx)
        {
            if (ctx.SelectedTarget is Pawn pawn &&
                EducationExperienceCalculator.TryCalculate(pawn, ctx.Settlement, def.category, LessonDef, ctx.SelectedOptionKeys, out EducationExperienceResult result))
                return EducationExperienceCalculator.BuildSummaryLines(result);
            return new string[] { "SettlementServices.Label.EducationPreviewPrompt".Translate() };
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (ctx.CurrentTarget?.liveThing is Pawn pawn &&
                EducationExperienceCalculator.TryCalculate(pawn, ctx.ResolveSettlement(), def.category, LessonDef, ctx.Job.selectedOptionKeys, out EducationExperienceResult result))
                pawn.skills.Learn(result.Skill, result.QualityXp);

            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();
    }
}

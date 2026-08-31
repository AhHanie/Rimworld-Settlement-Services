using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Education
{
    public class ResearchSeminarServiceWorker : SettlementServiceWorker
    {
        private const string ProjectGroupKey = "SettlementServices.Label.ResearchProjectChoice";
        private const float LowBandMinFraction = 0.10f;
        private const float LowBandMaxFraction = 0.15f;
        private const float HighBandMaxFraction = 0.30f;
        private const float OptionalIntellectualXp = 500f;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx) =>
            EligibleProjects().Any()
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoResearchProjectAvailable");

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            foreach (ResearchProjectDef proj in EligibleProjects())
                yield return new ServiceDisplayOption { key = proj.defName, label = proj.LabelCap, description = proj.description, groupKey = ProjectGroupKey };
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override IEnumerable<string> GetDisplaySummaryLines(SettlementServiceContext ctx)
        {
            string selectedKey = ctx.SelectedOptionKeys.FirstOrDefault();
            if (selectedKey == null) yield break;

            ResearchProjectDef proj = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(selectedKey);
            if (proj == null || !IsSeminarEligible(proj)) yield break;

            int minReward = Mathf.RoundToInt(proj.Cost * LowBandMinFraction);
            int maxReward = Mathf.RoundToInt(proj.Cost * HighBandMaxFraction);
            yield return "SettlementServices.Label.ResearchSeminarPreview".Translate(minReward, maxReward);
        }

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            ResearchProjectDef proj = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(ctx.Job.selectedOptionKeys.FirstOrDefault());
            if (proj != null && IsSeminarEligible(proj))
            {
                float quality = EducationQualityService.TrainingQuality(ctx.ResolveSettlement(), def.category);
                float fraction = RollRewardFraction(quality);
                Find.ResearchManager.AddProgress(proj, proj.Cost * fraction, ctx.CurrentTarget?.liveThing as Pawn);
            }

            if (ctx.CurrentTarget?.liveThing is Pawn attendee)
                attendee.skills.Learn(SkillDefOf.Intellectual, OptionalIntellectualXp);

            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static float RollRewardFraction(float quality)
        {
            float highBandProbability = Mathf.Clamp01(quality / 2f);
            return Rand.Chance(highBandProbability)
                ? Rand.Range(LowBandMaxFraction, HighBandMaxFraction)
                : Rand.Range(LowBandMinFraction, LowBandMaxFraction);
        }

        private static bool IsSeminarEligible(ResearchProjectDef proj) =>
            proj.baseCost > 0f && proj.CanStartNow;

        private static IEnumerable<ResearchProjectDef> EligibleProjects() =>
            DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(IsSeminarEligible);
    }
}

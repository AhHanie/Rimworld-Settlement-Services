using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Services.Education;

namespace Settlement_Services.Framework.Events.Workers
{
    public class UsefulNotesServiceEventWorker : ServiceEventWorker
    {
        private const float MinFraction = 0.05f;
        private const float MaxFraction = 0.10f;
        private const int SelectionSalt = 0x4e6f7465;
        private const int FractionSalt = 0x46726163;

        public override bool CanApply(ServiceJobContext ctx) => SecondaryPool(ctx).Any();

        public override void Apply(ServiceJobContext ctx)
        {
            Pawn attendee = ctx.ResolvePrimaryPawn();
            if (attendee == null) return;

            List<ResearchProjectDef> pool = SecondaryPool(ctx).ToList();
            if (pool.Count == 0) return;

            int selectionSeed = Gen.HashCombineInt(ctx.Job.jobId, SelectionSalt);
            ResearchProjectDef chosen = pool[Rand.RangeSeeded(0, pool.Count, selectionSeed)];

            int fractionSeed = Gen.HashCombineInt(ctx.Job.jobId, FractionSalt);
            float fraction = Rand.RangeSeeded(MinFraction, MaxFraction, fractionSeed);

            Find.ResearchManager.AddProgress(chosen, chosen.Cost * fraction, attendee);
        }

        private static IEnumerable<ResearchProjectDef> SecondaryPool(ServiceJobContext ctx)
        {
            ResearchProjectDef primary = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(ctx.Job.selectedOptionKeys.FirstOrDefault());
            return ResearchProjectEligibilityService.EligibleProjects()
                .Where(p => p != primary)
                .OrderBy(p => p.defName);
        }
    }
}

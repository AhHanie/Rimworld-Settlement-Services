using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Services.Education;

namespace Settlement_Services.Framework.Events.Workers
{
    public class InterestingConversationServiceEventWorker : ServiceEventWorker
    {
        private const float RewardXp = 600f;
        private const int SkillSalt = 0x496e7443;

        public override bool CanApply(ServiceJobContext ctx)
        {
            Pawn pawn = ctx.ResolvePrimaryPawn();
            return pawn?.skills != null && SkillEligibilityService.FindEligible(pawn).Any();
        }

        public override void Apply(ServiceJobContext ctx)
        {
            Pawn pawn = ctx.ResolvePrimaryPawn();
            if (pawn?.skills == null) return;

            List<SkillDef> pool = SkillEligibilityService.FindEligible(pawn).OrderBy(s => s.defName).ToList();
            if (pool.Count == 0) return;

            int seed = Gen.HashCombineInt(ctx.Job.jobId, SkillSalt);
            SkillDef skill = pool[Rand.RangeSeeded(0, pool.Count, seed)];
            pawn.skills.Learn(skill, RewardXp);
        }
    }
}

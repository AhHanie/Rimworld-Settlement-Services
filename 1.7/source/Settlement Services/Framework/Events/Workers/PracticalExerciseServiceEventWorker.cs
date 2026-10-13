using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Services.Education;

namespace Settlement_Services.Framework.Events.Workers
{
    public class PracticalExerciseServiceEventWorker : ServiceEventWorker
    {
        private const float RewardXp = 1000f;
        private const int SkillSalt = 0x50726163;

        public override bool CanApply(ServiceJobContext ctx) =>
            Participants(ctx.Job).Any(p => EligibleSecondarySkills(p.pawn, p.primary).Any());

        public override void Apply(ServiceJobContext ctx)
        {
            foreach ((Pawn pawn, SkillDef primary, int targetIndex) in Participants(ctx.Job))
            {
                List<SkillDef> pool = EligibleSecondarySkills(pawn, primary).ToList();
                if (pool.Count == 0) continue;

                int seed = Gen.HashCombineInt(Gen.HashCombineInt(ctx.Job.jobId, SkillSalt), targetIndex);
                SkillDef skill = pool[Rand.RangeSeeded(0, pool.Count, seed)];
                pawn.skills.Learn(skill, RewardXp);
            }
        }

        private static IEnumerable<(Pawn pawn, SkillDef primary, int targetIndex)> Participants(ServiceJobRecord job)
        {
            IReadOnlyList<TargetSnapshot> targets = job.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                if (!(targets[i]?.liveThing is Pawn pawn) || pawn.Destroyed || pawn.skills == null) continue;

                SkillDef primary = SkillEligibilityService.FindByKey(job.OptionKeysForTarget(i));
                if (primary == null) continue;

                yield return (pawn, primary, i);
            }
        }

        private static IEnumerable<SkillDef> EligibleSecondarySkills(Pawn pawn, SkillDef primary) =>
            SkillEligibilityService.FindEligible(pawn).Where(skill => skill != primary).OrderBy(skill => skill.defName);
    }
}

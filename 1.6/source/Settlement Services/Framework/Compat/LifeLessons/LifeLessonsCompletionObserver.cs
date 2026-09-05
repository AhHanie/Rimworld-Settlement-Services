using System.Collections.Generic;
using System.Linq;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Compatibility;

namespace Settlement_Services.Framework.Compat.LifeLessons
{
    internal sealed class LifeLessonsCompletionObserver : ICompatibilityCompletionObserver
    {
        public void OnCompleted(CompatibilityCompletionContext context)
        {
            ServiceJobRecord job = context.job;
            if (job == null || job.status == ServiceJobStatus.Failed) return;
            if (job.serviceDefName != LifeLessonsProficiencyServiceWorker.DefName) return;

            IReadOnlyList<TargetSnapshot> targets = job.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                if (!(targets[i]?.liveThing is Pawn pawn) || pawn.Destroyed)
                {
                    SupportLog.Warning($"Life Lessons proficiency for job {job.jobId} target {i} was skipped: pawn missing or destroyed.");
                    continue;
                }

                string optionKey = job.OptionKeysForTarget(i).FirstOrDefault();
                if (optionKey == null)
                {
                    SupportLog.Warning($"Life Lessons proficiency for job {job.jobId} ({pawn.LabelShort}) was skipped: no proficiency was selected.");
                    continue;
                }

                LifeLessonsAwardResult result = LifeLessonsCompatibilityModule.Gateway.TryGrantProficiency(pawn, optionKey);
                if (!result.success)
                    SupportLog.Warning($"Life Lessons proficiency for job {job.jobId} ({pawn.LabelShort}) was not granted: {result.diagnosticReason}");
            }
        }
    }
}

using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Compatibility;

namespace Settlement_Services.Framework.Compat.RimEducation
{
    internal sealed class RimEducationCompletionObserver : ICompatibilityCompletionObserver
    {
        public void OnCompleted(CompatibilityCompletionContext context)
        {
            ServiceJobRecord job = context.job;
            if (job == null || job.status == ServiceJobStatus.Failed) return;
            if (job.serviceDefName != RimEducationCourseServiceWorker.DefName) return;

            if (!(job.target?.liveThing is Pawn targetPawn) || targetPawn.Destroyed) return;

            RimEducationAwardResult result = RimEducationAdapter.TryAwardCourse(targetPawn, RimEducationAdapter.RawCourseReward);
            if (!result.anyProgressApplied)
                SupportLog.Warning($"Rim Education course for job {job.jobId} ({targetPawn.LabelShort}) awarded no progress: {result.diagnosticReason}");
        }
    }
}

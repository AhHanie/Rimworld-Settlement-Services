using RimWorld;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;
using Verse;

namespace Settlement_Services.UI.Overview
{
    internal static class ServiceOverviewFormatting
    {
        public static string StatusLabel(ServiceJobStatus status) =>
            ("SettlementServices.Status." + status).Translate();

        public static string TargetRuleLabel(ServiceTargetRule rule) =>
            ("SettlementServices.Label.TargetRule." + rule).Translate();

        public static string ExpectedCompletionLabel(ServiceJobRecord job)
        {
            if (job.status == ServiceJobStatus.Active
                && SettlementServicesWorldComponent.Current.TryGetHiringTransit(job.jobId, out HiringTransitRecord transit))
            {
                int transitTicksLeft = transit.arrivalTick - Find.TickManager.TicksGame;
                return transitTicksLeft <= 0
                    ? "SettlementServices.Label.ReadyToCollect".Translate()
                    : "SettlementServices.Label.ContractorsTravellingEta".Translate(transitTicksLeft.ToStringTicksToPeriod());
            }

            if (job.status == ServiceJobStatus.AwaitingCollection || job.status == ServiceJobStatus.Completed)
                return "SettlementServices.Label.ReadyToCollect".Translate();

            if (job.status == ServiceJobStatus.Collected || job.status == ServiceJobStatus.Cancelled || job.status == ServiceJobStatus.Failed)
                return StatusLabel(job.status);

            if (job.expectedCompletionTick < 0) return "SettlementServices.Label.NotYetStarted".Translate();

            int ticksLeft = job.expectedCompletionTick - Find.TickManager.TicksGame;
            return ticksLeft <= 0
                ? "SettlementServices.Label.ReadyToCollect".Translate()
                : "SettlementServices.Label.CompletionEstimate".Translate(ticksLeft.ToStringTicksToPeriod());
        }
    }
}

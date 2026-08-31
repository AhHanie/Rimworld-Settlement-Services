using System;
using System.Collections.Generic;
using Verse;
using Settlement_Services.Domain.Records;

namespace Settlement_Services.Domain
{
    public static class ServiceJobStatusMachine
    {
        private static readonly Dictionary<ServiceJobStatus, ServiceJobStatus[]> Allowed = new Dictionary<ServiceJobStatus, ServiceJobStatus[]>
        {
            [ServiceJobStatus.Drafted] = new[] { ServiceJobStatus.Quoted, ServiceJobStatus.Cancelled },
            [ServiceJobStatus.Quoted] = new[] { ServiceJobStatus.Reserved, ServiceJobStatus.Cancelled },
            [ServiceJobStatus.Reserved] = new[] { ServiceJobStatus.Active, ServiceJobStatus.Cancelled, ServiceJobStatus.Failed },
            [ServiceJobStatus.Active] = new[] { ServiceJobStatus.AwaitingCollection, ServiceJobStatus.Completed, ServiceJobStatus.Cancelled, ServiceJobStatus.Failed },
            [ServiceJobStatus.AwaitingCollection] = new[] { ServiceJobStatus.Collected, ServiceJobStatus.Failed },
            [ServiceJobStatus.Completed] = new[] { ServiceJobStatus.Collected },
            [ServiceJobStatus.Collected] = new ServiceJobStatus[0],
            [ServiceJobStatus.Cancelled] = new ServiceJobStatus[0],
            [ServiceJobStatus.Failed] = new ServiceJobStatus[0],
        };

        public static bool TryTransition(ServiceJobRecord job, ServiceJobStatus to)
        {
            if (job.status == to) return true;

            if (!Allowed.TryGetValue(job.status, out ServiceJobStatus[] next) || Array.IndexOf(next, to) < 0)
            {
                Settlement_Services.SupportLog.Error($"Illegal job status transition {job.status} -> {to} for job {job.jobId}.");
                return false;
            }

            job.status = to;
            job.statusChangedTick = Find.TickManager.TicksGame;
            return true;
        }

        public static bool IsTerminal(ServiceJobStatus status) =>
            status == ServiceJobStatus.Completed || status == ServiceJobStatus.Collected
            || status == ServiceJobStatus.Cancelled || status == ServiceJobStatus.Failed;
    }
}

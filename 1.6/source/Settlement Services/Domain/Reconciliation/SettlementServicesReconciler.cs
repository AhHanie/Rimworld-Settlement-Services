using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Services.Crafting;

namespace Settlement_Services.Domain.Reconciliation
{
    public static class SettlementServicesReconciler
    {
        public static void Reconcile(SettlementServicesWorldComponent component)
        {
            int failedJobs = 0;
            int droppedSettlements = 0;
            int droppedSpecialties = 0;

            foreach (ServiceJobRecord job in component.JobsRaw)
            {
                if (SkipsReconciliation(job.status)) continue;

                SettlementServiceDef def = DefDatabase<SettlementServiceDef>.GetNamedSilentFail(job.serviceDefName);
                if (def == null)
                {
                    FailJob(component, job, "SettlementServices.Error.ServiceNoLongerExists");
                    failedJobs++;
                    continue;
                }

                bool jobShouldBeGuarded = job.status == ServiceJobStatus.Reserved || job.status == ServiceJobStatus.Active;
                if (jobShouldBeGuarded && job.craftingProductionPlan != null && !CraftingProductionPlanValidator.Validate(job.craftingProductionPlan, out string craftingErrorKey))
                {
                    FailJob(component, job, craftingErrorKey);
                    failedJobs++;
                    continue;
                }

                if (job.status == ServiceJobStatus.Active)
                {
                    Settlement hostileCheckSettlement = WorldObjectLookup.ResolveSettlement(job.settlementWorldObjectId);
                    if (hostileCheckSettlement != null && hostileCheckSettlement.Faction != null && hostileCheckSettlement.Faction.HostileTo(Faction.OfPlayer))
                    {
                        FailJob(component, job, "SettlementServices.Error.FactionHostile");
                        failedJobs++;
                        continue;
                    }
                }

                bool targetShouldExist = job.status == ServiceJobStatus.Active || job.status == ServiceJobStatus.AwaitingCollection;
                if (targetShouldExist && job.targets.Any(t => t != null && !t.IsResolvable))
                {
                    FailJob(component, job, "SettlementServices.Error.TargetNoLongerExists");
                    failedJobs++;
                    continue;
                }

                if (WorldObjectLookup.ResolveSettlement(job.settlementWorldObjectId) == null)
                {
                    FailJob(component, job, "SettlementServices.Error.SettlementNoLongerExists");
                    failedJobs++;
                }
            }

            List<SettlementRecord> toDrop = component.SettlementRecordsRaw
                .Where(r => WorldObjectLookup.ResolveSettlement(r.settlementWorldObjectId) == null
                    && !component.JobsRaw.Any(j => j.settlementWorldObjectId == r.settlementWorldObjectId))
                .ToList();
            foreach (SettlementRecord orphan in toDrop)
            {
                component.SettlementRecordsRaw.Remove(orphan);
                droppedSettlements++;
            }

            foreach (SettlementRecord record in component.SettlementRecordsRaw)
            {
                if (record.capability == null) continue;

                List<string> toRemove = record.capability.specialtyDefNames
                    .Where(defName => !StillEligible(defName))
                    .ToList();
                if (toRemove.Count == 0) continue;

                component.RemoveSpecialties(record.settlementWorldObjectId, toRemove);
                droppedSpecialties += toRemove.Count;
            }

            if (failedJobs > 0 || droppedSettlements > 0 || droppedSpecialties > 0)
            {
                Settlement_Services.SupportLog.Info(
                    $"Reconciliation: failed {failedJobs} orphaned job(s), dropped {droppedSettlements} orphaned settlement record(s), dropped {droppedSpecialties} specialty grant(s) whose gate is no longer met.");
            }
        }

        private static bool StillEligible(string defName)
        {
            SettlementSpecialtyDef def = DefDatabase<SettlementSpecialtyDef>.GetNamedSilentFail(defName);
            return def != null;
        }

        private static bool SkipsReconciliation(ServiceJobStatus status)
        {
            return status == ServiceJobStatus.Completed || status == ServiceJobStatus.Collected
                || status == ServiceJobStatus.Cancelled || status == ServiceJobStatus.Failed;
        }

        private static void FailJob(SettlementServicesWorldComponent component, ServiceJobRecord job, string errorKey)
        {
            SettlementServiceOrchestrator.FailJob(component, job.jobId, errorKey);
        }
    }
}

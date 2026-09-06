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
            int droppedIdeoEntries = 0;

            foreach (ServiceJobRecord job in component.JobsRaw)
            {
                if (ServiceJobStatusMachine.IsTerminal(job.status)) continue;

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
                }
            }

            failedJobs += DetectAndHandleMissingProviders(component);
            ReconcileHiringTransits(component);

            List<SettlementRecord> toDrop = component.SettlementRecordsRaw
                .Where(r => WorldObjectLookup.ResolveSettlement(r.settlementWorldObjectId) == null
                    && !component.JobsRaw.Any(j => j.settlementWorldObjectId == r.settlementWorldObjectId))
                .ToList();
            foreach (SettlementRecord orphan in toDrop)
            {
                component.DisposeHiringCandidates(orphan);
                component.SettlementRecordsRaw.Remove(orphan);
                droppedSettlements++;
            }

            foreach (SettlementRecord record in component.SettlementRecordsRaw)
            {
                if (record.capability == null) continue;
                if (record.capability.specialtyDefNames == null) record.capability.specialtyDefNames = new List<string>();

                List<string> toRemove = record.capability.specialtyDefNames
                    .Where(defName => !StillEligible(defName))
                    .ToList();
                if (toRemove.Count == 0) continue;

                component.RemoveSpecialties(record.settlementWorldObjectId, toRemove);
                droppedSpecialties += toRemove.Count;
            }

            foreach (SettlementRecord record in component.SettlementRecordsRaw)
            {
                if (!record.practicedIdeosInitialized) continue;

                int staleCount = record.practicedIdeoLoadIds.Count(id => IdeoLookup.ResolveIdeo(id) == null);
                if (staleCount == 0) continue;

                component.ReconcilePracticedIdeoRoster(record);
                droppedIdeoEntries += staleCount;
            }

            if (failedJobs > 0 || droppedSettlements > 0 || droppedSpecialties > 0 || droppedIdeoEntries > 0)
            {
                Settlement_Services.SupportLog.Info(
                    $"Reconciliation: failed {failedJobs} orphaned job(s), dropped {droppedSettlements} orphaned settlement record(s), dropped {droppedSpecialties} specialty grant(s) whose gate is no longer met, "
                    + $"dropped {droppedIdeoEntries} stale practiced ideoligion entries.");
            }
        }

        public static int DetectAndHandleMissingProviders(SettlementServicesWorldComponent component)
        {
            List<ServiceJobRecord> unresolved = component.AllJobs.Where(j => !ServiceJobStatusMachine.IsTerminal(j.status)).ToList();
            if (unresolved.Count == 0) return 0;

            int affected = 0;
            foreach (int settlementWorldObjectId in unresolved.Select(j => j.settlementWorldObjectId).Distinct().ToList())
            {
                Settlement settlement = WorldObjectLookup.ResolveSettlement(settlementWorldObjectId);
                if (settlement != null)
                {
                    component.RecordSettlementTileSnapshot(settlementWorldObjectId, settlement.Tile);
                    continue;
                }

                ServiceJobRecord withTile = unresolved.FirstOrDefault(j => j.settlementWorldObjectId == settlementWorldObjectId && j.settlementTile.Valid);
                PlanetTile tile = withTile?.settlementTile ?? PlanetTile.Invalid;
                affected += unresolved.Count(j => j.settlementWorldObjectId == settlementWorldObjectId);
                SettlementServiceOrchestrator.HandleSettlementDestroyed(component, settlementWorldObjectId, tile);
            }
            return affected;
        }

        public static void ReconcileHiringTransits(SettlementServicesWorldComponent component)
        {
            foreach (HiringTransitRecord transit in component.HiringTransitsRaw.ToList())
            {
                bool hasLiveContractor = transit.pawns.Any(p => p != null && !p.Destroyed && !p.Dead);
                ServiceJobRecord job = component.GetJob(transit.jobId);
                bool jobActive = job != null && job.status == ServiceJobStatus.Active;

                if (hasLiveContractor && jobActive) continue;

                component.AbortHiringTransit(transit.jobId);
                if (jobActive) SettlementServiceOrchestrator.FailJob(component, transit.jobId, "SettlementServices.Error.CandidateNoLongerAvailable");
            }
        }

        private static bool StillEligible(string defName)
        {
            SettlementSpecialtyDef def = DefDatabase<SettlementSpecialtyDef>.GetNamedSilentFail(defName);
            return def != null;
        }

        private static void FailJob(SettlementServicesWorldComponent component, ServiceJobRecord job, string errorKey)
        {
            SettlementServiceOrchestrator.FailJob(component, job.jobId, errorKey);
        }
    }
}

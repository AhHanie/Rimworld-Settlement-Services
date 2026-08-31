using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain.Reconciliation;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework;
using Settlement_Services.Framework.Custody;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Payment;

namespace Settlement_Services.Domain
{
    public class SettlementServicesWorldComponent : WorldComponent, IThingHolder
    {
        internal const int TickInterval = 250;

        private const int JobRetentionWindowTicks = 900000;

        private int loadedSchemaVersion = SchemaVersion.Current;
        private int nextJobId = 1;

        private List<SettlementRecord> settlementRecords = new List<SettlementRecord>();
        private List<ServiceJobRecord> jobs = new List<ServiceJobRecord>();

        private List<int> pendingHomeSilverRefunds = new List<int>();

        private ThingOwner<Thing> itemCustody;

        private List<TargetSnapshot> pendingHomeDeliveries = new List<TargetSnapshot>();

        private Dictionary<int, SettlementRecord> settlementsByWorldObjectId;
        private Dictionary<int, List<ServiceJobRecord>> jobsBySettlement;
        private Dictionary<int, ServiceJobRecord> jobsById;
        private List<ServiceJobRecord> activeJobsIndex;

        public static SettlementServicesWorldComponent Current => Find.World?.GetComponent<SettlementServicesWorldComponent>();

        public SettlementServicesWorldComponent(World world) : base(world)
        {
            itemCustody = new ThingOwner<Thing>(this);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref loadedSchemaVersion, "schemaVersion", 1);
            Scribe_Values.Look(ref nextJobId, "nextJobId", 1);
            Scribe_Collections.Look(ref settlementRecords, "settlementRecords", LookMode.Deep);
            Scribe_Collections.Look(ref jobs, "jobs", LookMode.Deep);
            Scribe_Collections.Look(ref pendingHomeSilverRefunds, "pendingHomeSilverRefunds", LookMode.Value);
            Scribe_Deep.Look(ref itemCustody, "itemCustody");
            Scribe_Collections.Look(ref pendingHomeDeliveries, "pendingHomeDeliveries", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (settlementRecords == null) settlementRecords = new List<SettlementRecord>();
                if (jobs == null) jobs = new List<ServiceJobRecord>();
                if (pendingHomeSilverRefunds == null) pendingHomeSilverRefunds = new List<int>();
                if (itemCustody == null) itemCustody = new ThingOwner<Thing>(this);
                if (pendingHomeDeliveries == null) pendingHomeDeliveries = new List<TargetSnapshot>();
                settlementRecords.RemoveAll(r => r == null);
                jobs.RemoveAll(j => j == null);

                loadedSchemaVersion = SchemaVersion.Current;
            }
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            RebuildIndexes();
            SettlementServicesReconciler.Reconcile(this);
        }

        public override void WorldComponentTick()
        {
            FlushPendingHomeSilverRefunds();
            FlushPendingHomeDeliveries();
            PruneResolvedJobs();
            if (Find.TickManager.TicksGame % TickInterval != 0) return;
            SettlementServicesReconciler.DetectAndHandleMissingProviders(this);
            SettlementServiceJobScheduler.TickAll(this, TickInterval);
        }

        private void FlushPendingHomeSilverRefunds()
        {
            if (pendingHomeSilverRefunds.Count == 0) return;

            for (int i = pendingHomeSilverRefunds.Count - 1; i >= 0; i--)
            {
                if (HomeColonySilverPaymentProvider.Instance.TryPlaceRefund(pendingHomeSilverRefunds[i]))
                    pendingHomeSilverRefunds.RemoveAt(i);
            }
        }

        public void QueuePendingHomeSilverRefund(int amount)
        {
            pendingHomeSilverRefunds.Add(amount);
        }

        private void FlushPendingHomeDeliveries()
        {
            if (pendingHomeDeliveries.Count == 0) return;

            for (int i = pendingHomeDeliveries.Count - 1; i >= 0; i--)
            {
                if (TargetCustodyService.TryDeliverHome(pendingHomeDeliveries[i]))
                    pendingHomeDeliveries.RemoveAt(i);
            }
        }

        public void QueueHomeDelivery(TargetSnapshot snapshot)
        {
            if (snapshot?.liveThing == null) return;
            pendingHomeDeliveries.Add(snapshot);
        }

        private void PruneResolvedJobs()
        {
            if (Find.TickManager.TicksGame % TickInterval != 0) return;

            for (int i = jobs.Count - 1; i >= 0; i--)
            {
                ServiceJobRecord job = jobs[i];
                bool prunable = job.status == ServiceJobStatus.Collected
                    || job.status == ServiceJobStatus.Cancelled
                    || job.status == ServiceJobStatus.Failed;
                if (!prunable) continue;
                if (Find.TickManager.TicksGame - job.statusChangedTick < JobRetentionWindowTicks) continue;

                jobs.RemoveAt(i);
                jobsById.Remove(job.jobId);
                if (jobsBySettlement.TryGetValue(job.settlementWorldObjectId, out List<ServiceJobRecord> list)) list.Remove(job);
            }
        }

        IThingHolder IThingHolder.ParentHolder => null;
        public ThingOwner GetDirectlyHeldThings() => itemCustody;
        public void GetChildHolders(List<IThingHolder> outChildren) =>
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, itemCustody);


        public void TakeItemCustody(Thing thing) => itemCustody.TryAdd(thing, canMergeWithExistingStacks: false);

        public void ReleaseItemCustody(Thing thing)
        {
            if (itemCustody.Contains(thing)) itemCustody.Remove(thing);
        }

        public bool IsTargetReserved(Thing thing, int excludingJobId = -1)
        {
            foreach (ServiceJobRecord job in jobs)
            {
                if (job.jobId == excludingJobId) continue;
                if (!IsOpenCustodyStatus(job.status)) continue;
                if (job.targets != null && job.targets.Any(t => t?.liveThing == thing)) return true;
                if (job.results != null && job.results.Any(r => r.liveThing == thing)) return true;
            }
            return false;
        }

        private static bool IsOpenCustodyStatus(ServiceJobStatus s) =>
            s == ServiceJobStatus.Active || s == ServiceJobStatus.AwaitingCollection;

        private void RebuildIndexes()
        {
            settlementsByWorldObjectId = new Dictionary<int, SettlementRecord>();
            foreach (SettlementRecord record in settlementRecords)
            {
                if (settlementsByWorldObjectId.ContainsKey(record.settlementWorldObjectId))
                {
                    Settlement_Services.SupportLog.Warning($"Duplicate settlement record for world object {record.settlementWorldObjectId}, ignoring extra copy.");
                    continue;
                }
                settlementsByWorldObjectId[record.settlementWorldObjectId] = record;
            }

            jobsById = new Dictionary<int, ServiceJobRecord>();
            jobsBySettlement = new Dictionary<int, List<ServiceJobRecord>>();
            activeJobsIndex = new List<ServiceJobRecord>();
            foreach (ServiceJobRecord job in jobs)
            {
                if (jobsById.ContainsKey(job.jobId))
                {
                    Settlement_Services.SupportLog.Warning($"Duplicate job id {job.jobId}, ignoring extra copy.");
                    continue;
                }
                jobsById[job.jobId] = job;

                if (!jobsBySettlement.TryGetValue(job.settlementWorldObjectId, out List<ServiceJobRecord> list))
                {
                    list = new List<ServiceJobRecord>();
                    jobsBySettlement[job.settlementWorldObjectId] = list;
                }
                list.Add(job);

                if (job.status == ServiceJobStatus.Active) activeJobsIndex.Add(job);
            }
        }

        internal List<SettlementRecord> SettlementRecordsRaw => settlementRecords;
        internal List<ServiceJobRecord> JobsRaw => jobs;


        public IReadOnlyList<ServiceJobRecord> JobsForSettlement(int settlementWorldObjectId)
        {
            return jobsBySettlement.TryGetValue(settlementWorldObjectId, out List<ServiceJobRecord> list)
                ? list
                : (IReadOnlyList<ServiceJobRecord>)Array.Empty<ServiceJobRecord>();
        }

        public void RecordSettlementTileSnapshot(int settlementWorldObjectId, PlanetTile tile)
        {
            if (!tile.Valid) return;
            if (!jobsBySettlement.TryGetValue(settlementWorldObjectId, out List<ServiceJobRecord> list)) return;

            foreach (ServiceJobRecord job in list)
            {
                if (ServiceJobStatusMachine.IsTerminal(job.status)) continue;
                job.settlementTile = tile;
            }
        }

        public IReadOnlyList<ServiceJobRecord> ActiveJobs => activeJobsIndex;

        public IReadOnlyList<ServiceJobRecord> AllJobs => jobs;

        public IReadOnlyList<DiscoveryRecord> DiscoveriesForSettlement(int settlementWorldObjectId)
        {
            return settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record)
                ? record.discoveries
                : (IReadOnlyList<DiscoveryRecord>)Array.Empty<DiscoveryRecord>();
        }

        public IEnumerable<int> SettlementWorldObjectIdsWithAnyDiscovery =>
            settlementRecords.Where(r => r.discoveries.Count > 0).Select(r => r.settlementWorldObjectId);

        public ServiceJobRecord GetJob(int jobId)
        {
            return jobsById.TryGetValue(jobId, out ServiceJobRecord job) ? job : null;
        }

        public ServiceJobRecord CreateJob(int settlementWorldObjectId, PlanetTile settlementTile, string serviceDefName, RequestChannel channel, List<TargetSnapshot> targets, int quantity = 1,
            int requesterCaravanId = -1, string requesterFactionLoadId = null, string requesterCaravanSnapshotLabel = null)
        {
            List<TargetSnapshot> normalizedTargets = targets ?? new List<TargetSnapshot>();
            var job = new ServiceJobRecord
            {
                jobId = nextJobId++,
                schemaVersion = SchemaVersion.Current,
                settlementWorldObjectId = settlementWorldObjectId,
                settlementTile = settlementTile,
                status = ServiceJobStatus.Drafted,
                createdTick = Find.TickManager.TicksGame,
                statusChangedTick = Find.TickManager.TicksGame,
                requestChannel = channel,
                serviceDefName = serviceDefName,
                target = normalizedTargets.Count > 0 ? normalizedTargets[0] : null,
                targets = normalizedTargets,
                quantity = Mathf.Max(1, quantity),
                requesterCaravanId = requesterCaravanId,
                requesterFactionLoadId = requesterFactionLoadId,
                requesterCaravanSnapshotLabel = requesterCaravanSnapshotLabel,
            };

            jobs.Add(job);
            jobsById[job.jobId] = job;
            if (!jobsBySettlement.TryGetValue(settlementWorldObjectId, out List<ServiceJobRecord> list))
            {
                list = new List<ServiceJobRecord>();
                jobsBySettlement[settlementWorldObjectId] = list;
            }
            list.Add(job);

            return job;
        }

        public bool TryTransition(int jobId, ServiceJobStatus to)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job == null) return false;

            ServiceJobStatus from = job.status;
            if (!ServiceJobStatusMachine.TryTransition(job, to)) return false;

            if (from == ServiceJobStatus.Active && to != ServiceJobStatus.Active) activeJobsIndex.Remove(job);
            if (to == ServiceJobStatus.Active && from != ServiceJobStatus.Active) activeJobsIndex.Add(job);

            return true;
        }

        public void FreezeQuote(int jobId, SettlementServiceQuote quote)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job == null) return;
            job.acceptedQuote = quote;
        }

        public void FreezeCraftingProductionPlan(int jobId, CraftingProductionPlan plan)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job == null) return;
            job.craftingProductionPlan = plan;
        }

        public void FreezeConsumedPlayerSuppliedInputs(int jobId, List<ThingDefCountClass> items)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job == null) return;
            job.consumedPlayerSuppliedInputs = items?.Select(i => new ThingDefCountClass(i.thingDef, i.count)).ToList() ?? new List<ThingDefCountClass>();
        }

        public void MarkPlayerSuppliedInputsRefunded(int jobId)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job == null) return;
            job.playerSuppliedInputsRefunded = true;
        }

        public void FreezeOptionSelections(int jobId, SettlementServiceRequest request)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job == null) return;
            job.selectedOptionKeys = new List<string>(request?.selectedOptionKeys ?? Enumerable.Empty<string>());
            job.targetOptionSelections = request?.targetOptionSelections?.Select(s => s.Clone()).ToList() ?? new List<ServiceTargetOptionSelection>();
            job.craftingCommissionLines = request?.craftingCommissionLines?.Select(l => l.Clone()).ToList() ?? new List<CraftingCommissionLine>();
        }

        public DiscoveryRecord RecordDiscovery(int settlementWorldObjectId, string serviceDefName, RequestChannel via)
        {
            if (HasDiscovered(settlementWorldObjectId, serviceDefName)) return null;

            SettlementRecord record = GetOrCreateSettlementRecord(settlementWorldObjectId);
            var discovery = new DiscoveryRecord
            {
                serviceDefName = serviceDefName,
                discoveredVia = via,
                discoveredTick = Find.TickManager.TicksGame,
            };
            record.discoveries.Add(discovery);
            return discovery;
        }

        public bool HasDiscovered(int settlementWorldObjectId, string serviceDefName)
        {
            return settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record)
                && record.discoveries.Any(d => d.serviceDefName == serviceDefName);
        }

        public ReservationRecord Reserve(int jobId, string thingDefName, int amount)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job == null) return null;

            SettlementRecord record = GetOrCreateSettlementRecord(job.settlementWorldObjectId);
            var reservation = new ReservationRecord
            {
                jobId = jobId,
                stockThingDefName = thingDefName,
                amountReserved = amount,
                reservedAtTick = Find.TickManager.TicksGame,
            };
            record.reservations.Add(reservation);
            return reservation;
        }

        public bool IsServiceEventOnCooldown(int settlementWorldObjectId, string eventDefName, int cooldownTicks)
        {
            if (!settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record)) return false;
            return record.recentServiceEventTicks.TryGetValue(eventDefName, out int lastTick)
                && Find.TickManager.TicksGame - lastTick < cooldownTicks;
        }

        public void RecordServiceEventOccurrence(int settlementWorldObjectId, string eventDefName)
        {
            SettlementRecord record = GetOrCreateSettlementRecord(settlementWorldObjectId);
            record.recentServiceEventTicks[eventDefName] = Find.TickManager.TicksGame;
        }

        public void ReleaseReservation(int jobId, string thingDefName)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job == null) return;
            if (!settlementsByWorldObjectId.TryGetValue(job.settlementWorldObjectId, out SettlementRecord record)) return;

            record.reservations.RemoveAll(r => r.jobId == jobId && r.stockThingDefName == thingDefName);
        }

        public void ReleaseAllReservations(int jobId)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job == null) return;
            if (!settlementsByWorldObjectId.TryGetValue(job.settlementWorldObjectId, out SettlementRecord record)) return;

            record.reservations.RemoveAll(r => r.jobId == jobId);
        }

        public IReadOnlyList<ReservationRecord> ReservationsForJob(int jobId)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job == null || !settlementsByWorldObjectId.TryGetValue(job.settlementWorldObjectId, out SettlementRecord record))
                return Array.Empty<ReservationRecord>();
            return record.reservations.Where(r => r.jobId == jobId).ToList();
        }

        public IReadOnlyList<string> GetOrGenerateSpecialtyDefNames(int settlementWorldObjectId, Func<List<string>> generator)
        {
            SettlementRecord record = GetOrCreateSettlementRecord(settlementWorldObjectId);
            if (record.capability != null) return record.capability.specialtyDefNames;

            record.capability = new SettlementCapabilityRecord { specialtyDefNames = generator() };
            return record.capability.specialtyDefNames;
        }

        public bool TryGetCapability(int settlementWorldObjectId, out IReadOnlyList<string> specialtyDefNames)
        {
            if (settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record) && record.capability != null)
            {
                specialtyDefNames = record.capability.specialtyDefNames;
                return true;
            }
            specialtyDefNames = null;
            return false;
        }

        public void RemoveSpecialties(int settlementWorldObjectId, IEnumerable<string> defNamesToRemove)
        {
            if (!settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record) || record.capability == null) return;
            record.capability.specialtyDefNames.RemoveAll(defNamesToRemove.Contains);
        }

        public bool TryAddSpecialty(int settlementWorldObjectId, string specialtyDefName)
        {
            if (specialtyDefName.NullOrEmpty()) return false;

            SettlementRecord record = GetOrCreateSettlementRecord(settlementWorldObjectId);
            if (record.capability == null) record.capability = new SettlementCapabilityRecord();
            if (record.capability.specialtyDefNames.Contains(specialtyDefName)) return false;

            record.capability.specialtyDefNames.Add(specialtyDefName);
            return true;
        }

        public InvestmentRecord GetInvestment(int settlementWorldObjectId) =>
            settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record) ? record.investment : null;

        public void Invest(int settlementWorldObjectId, float discountPct, int decayDurationTicks)
        {
            SettlementRecord record = GetOrCreateSettlementRecord(settlementWorldObjectId);
            record.investment = new InvestmentRecord
            {
                investedTick = Find.TickManager.TicksGame,
                investedDiscountPct = discountPct,
                decayDurationTicks = decayDurationTicks,
            };
        }

        public int CatchUpStock(int settlementWorldObjectId, string thingDefName, int capacity, int refreshAmount, int refreshIntervalTicks)
        {
            SettlementRecord record = GetOrCreateSettlementRecord(settlementWorldObjectId);
            StockRecord stock = record.stock.Find(s => s.stockThingDefName == thingDefName);
            int now = Find.TickManager.TicksGame;

            if (stock == null)
            {
                stock = new StockRecord { stockThingDefName = thingDefName, currentAmount = capacity, lastRefreshTick = now };
                record.stock.Add(stock);
                return stock.currentAmount;
            }

            int elapsed = now - stock.lastRefreshTick;
            if (elapsed >= refreshIntervalTicks && refreshIntervalTicks > 0)
            {
                int intervals = elapsed / refreshIntervalTicks;
                stock.currentAmount = Mathf.Min(capacity, stock.currentAmount + intervals * refreshAmount);
                stock.lastRefreshTick += intervals * refreshIntervalTicks;
            }

            stock.currentAmount = Mathf.Min(stock.currentAmount, capacity);
            return stock.currentAmount;
        }

        public int TotalReserved(int settlementWorldObjectId, string thingDefName)
        {
            if (!settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record)) return 0;
            return record.reservations.Where(r => r.stockThingDefName == thingDefName).Sum(r => r.amountReserved);
        }

        public void ConsumeReservedStock(int jobId, string thingDefName)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job == null) return;
            if (!settlementsByWorldObjectId.TryGetValue(job.settlementWorldObjectId, out SettlementRecord record)) return;

            ReservationRecord reservation = record.reservations.Find(r => r.jobId == jobId && r.stockThingDefName == thingDefName);
            if (reservation == null) return;

            StockRecord stock = record.stock.Find(s => s.stockThingDefName == thingDefName);
            if (stock != null) stock.currentAmount = Mathf.Max(0, stock.currentAmount - reservation.amountReserved);

            record.reservations.Remove(reservation);
        }

        public IReadOnlyList<HiringCandidateRecord> GetOrRefreshHiringCandidates(int settlementWorldObjectId, int capacity, int refreshIntervalTicks, Func<int, HiringCandidateRecord> generator)
        {
            SettlementRecord record = GetOrCreateSettlementRecord(settlementWorldObjectId);
            int now = Find.TickManager.TicksGame;

            if (record.hiringPoolLastRefreshTick < 0)
            {
                for (int i = 0; i < capacity; i++) record.hiringCandidates.Add(generator(record.nextHiringCandidateId++));
                record.hiringPoolLastRefreshTick = now;
                return record.hiringCandidates;
            }

            record.hiringCandidates.RemoveAll(c => c.expiryTick >= 0 && now >= c.expiryTick);

            int elapsed = now - record.hiringPoolLastRefreshTick;
            if (elapsed >= refreshIntervalTicks && refreshIntervalTicks > 0)
            {
                int intervals = elapsed / refreshIntervalTicks;
                int toAdd = Mathf.Max(0, Mathf.Min(intervals, capacity - record.hiringCandidates.Count));
                for (int i = 0; i < toAdd; i++) record.hiringCandidates.Add(generator(record.nextHiringCandidateId++));
                record.hiringPoolLastRefreshTick += intervals * refreshIntervalTicks;
            }

            return record.hiringCandidates;
        }

        public void RemoveHiringCandidate(int settlementWorldObjectId, int candidateId)
        {
            if (!settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record)) return;
            record.hiringCandidates.RemoveAll(c => c.candidateId == candidateId);
        }

        private SettlementRecord GetOrCreateSettlementRecord(int settlementWorldObjectId)
        {
            if (settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record)) return record;

            record = new SettlementRecord { settlementWorldObjectId = settlementWorldObjectId };
            settlementRecords.Add(record);
            settlementsByWorldObjectId[settlementWorldObjectId] = record;
            return record;
        }
    }
}

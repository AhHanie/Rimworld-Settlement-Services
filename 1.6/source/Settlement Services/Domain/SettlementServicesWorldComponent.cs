using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain.Migration;
using Settlement_Services.Domain.Reconciliation;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework;
using Settlement_Services.Framework.Board;
using Settlement_Services.Framework.Compatibility;
using Settlement_Services.Framework.Custody;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Payment;
using Settlement_Services.Framework.Stock;

namespace Settlement_Services.Domain
{
    public class SettlementServicesWorldComponent : WorldComponent, IThingHolder
    {
        internal const int TickInterval = 250;

        private const int JobRetentionWindowTicks = 900000;
        private const int CompatibilityCooldownRetentionTicks = 5400000;

        private int loadedSchemaVersion = SchemaVersion.Current;
        private int nextJobId = 1;

        private List<SettlementRecord> settlementRecords = new List<SettlementRecord>();
        private List<ServiceJobRecord> jobs = new List<ServiceJobRecord>();

        private int nextBoardJobId = 1;
        private bool boardReconcilePending;
        private bool loadReconcilePending;
        private bool loadReconcileFailed;
        private List<BoardJobRecord> boardJobs = new List<BoardJobRecord>();

        private List<int> pendingHomeSilverRefunds = new List<int>();

        private ThingOwner<Thing> itemCustody;
        private ThingOwner<Pawn> hiringCandidateCustody;
        private ThingOwner<Pawn> hiringTransitCustody;
        private ThingOwner<Pawn> animalVendorCustody;
        private List<HiringTransitRecord> hiringTransits = new List<HiringTransitRecord>();

        private const int HiringRosterDurationTicks = 300000;
        public const int AnimalVendorRefreshPeriodTicks = 600000;
        public const int AnimalVendorMaxOffers = 20;

        private List<TargetSnapshot> pendingHomeDeliveries = new List<TargetSnapshot>();

        private CompatibilityWorldState compatibilityWorldState = new CompatibilityWorldState();

        private Dictionary<int, SettlementRecord> settlementsByWorldObjectId;
        private Dictionary<int, List<ServiceJobRecord>> jobsBySettlement;
        private Dictionary<int, ServiceJobRecord> jobsById;
        private Dictionary<int, BoardJobRecord> boardJobsById = new Dictionary<int, BoardJobRecord>();
        private List<ServiceJobRecord> activeJobsIndex;
        private Dictionary<int, int> dynamicStockSyncTicks = new Dictionary<int, int>();

        public static SettlementServicesWorldComponent Current => Find.World?.GetComponent<SettlementServicesWorldComponent>();

        public CompatibilityWorldState CompatibilityWorldState => compatibilityWorldState;

        public SettlementServicesWorldComponent(World world) : base(world)
        {
            itemCustody = new ThingOwner<Thing>(this);
            hiringCandidateCustody = new ThingOwner<Pawn>(this);
            hiringTransitCustody = new ThingOwner<Pawn>(this);
            animalVendorCustody = new ThingOwner<Pawn>(this);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref loadedSchemaVersion, "schemaVersion", 1);
            Scribe_Values.Look(ref nextJobId, "nextJobId", 1);
            Scribe_Collections.Look(ref settlementRecords, "settlementRecords", LookMode.Deep);
            Scribe_Collections.Look(ref jobs, "jobs", LookMode.Deep);
            Scribe_Values.Look(ref nextBoardJobId, "nextBoardJobId", 1);
            Scribe_Collections.Look(ref boardJobs, "boardJobs", LookMode.Deep);
            Scribe_Collections.Look(ref pendingHomeSilverRefunds, "pendingHomeSilverRefunds", LookMode.Value);
            Scribe_Deep.Look(ref itemCustody, "itemCustody");
            Scribe_Deep.Look(ref hiringCandidateCustody, "hiringCandidateCustody");
            Scribe_Deep.Look(ref hiringTransitCustody, "hiringTransitCustody");
            Scribe_Deep.Look(ref animalVendorCustody, "animalVendorCustody");
            Scribe_Collections.Look(ref hiringTransits, "hiringTransits", LookMode.Deep);
            Scribe_Collections.Look(ref pendingHomeDeliveries, "pendingHomeDeliveries", LookMode.Deep);
            Scribe_Deep.Look(ref compatibilityWorldState, "compatibilityWorldState");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                SupportLog.Info($"Job board load: boardJobs list {(boardJobs == null ? "was null" : "has " + boardJobs.Count + " record(s)")}, itemCustody {(itemCustody == null ? "was null" : "holds " + itemCustody.Count + " thing(s)")}.");
                EnsureTopLevelState();
            }
        }

        private void EnsureTopLevelState()
        {
            if (settlementRecords == null) settlementRecords = new List<SettlementRecord>();
            if (jobs == null) jobs = new List<ServiceJobRecord>();
            if (boardJobs == null) boardJobs = new List<BoardJobRecord>();
            if (pendingHomeSilverRefunds == null) pendingHomeSilverRefunds = new List<int>();
            if (itemCustody == null) itemCustody = new ThingOwner<Thing>(this);
            if (hiringCandidateCustody == null) hiringCandidateCustody = new ThingOwner<Pawn>(this);
            if (hiringTransitCustody == null) hiringTransitCustody = new ThingOwner<Pawn>(this);
            if (animalVendorCustody == null) animalVendorCustody = new ThingOwner<Pawn>(this);
            if (hiringTransits == null) hiringTransits = new List<HiringTransitRecord>();
            if (pendingHomeDeliveries == null) pendingHomeDeliveries = new List<TargetSnapshot>();
            if (compatibilityWorldState == null) compatibilityWorldState = new CompatibilityWorldState();

            settlementRecords.RemoveAll(r => r == null);
            jobs.RemoveAll(j => j == null);
            boardJobs.RemoveAll(j => j == null);
            hiringTransits.RemoveAll(t => t == null);
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            EnsureTopLevelState();
            RebuildIndexes();

            if (fromLoad)
            {
                loadReconcilePending = true;
                boardReconcilePending = true;
                return;
            }

            loadedSchemaVersion = SchemaVersion.Current;
            SettlementServicesReconciler.Reconcile(this);
            RebuildIndexes();
        }

        private bool EnsureLoadReconciled()
        {
            if (loadReconcileFailed) return false;
            if (!loadReconcilePending) return true;

            try
            {
                EnsureTopLevelState();
                foreach (SettlementRecord record in settlementRecords)
                {
                    record.hiringCandidates?.RemoveAll(c => c == null || !hiringCandidateCustody.Contains(c.pawn));
                    record.animalVendorOffers?.RemoveAll(o => o == null || !animalVendorCustody.Contains(o.pawn));
                }
                RebuildIndexes();

                if (loadedSchemaVersion < SchemaVersion.Current)
                {
                    SettlementServiceMigrationRegistry.Run(this, loadedSchemaVersion, SchemaVersion.Current);
                    RebuildIndexes();
                }

                SettlementServicesReconciler.Reconcile(this);
                RebuildIndexes();

                loadedSchemaVersion = SchemaVersion.Current;
                loadReconcilePending = false;
                return true;
            }
            catch (Exception ex)
            {
                loadReconcileFailed = true;
                SupportLog.Error($"Settlement Services failed to finish initializing after loading the save (schema {loadedSchemaVersion} -> {SchemaVersion.Current}); service and board ticking is disabled for this session: {ex}");
                return false;
            }
        }

        internal void EnsureBoardReconciled()
        {
            if (!boardReconcilePending) return;

            boardReconcilePending = false;
            BoardJobCoordinator.Reconcile(this);
            SettlementServicesReconciler.HandleMissingBoardProviders(this);
        }

        public override void WorldComponentTick()
        {
            if (!EnsureLoadReconciled()) return;
            EnsureBoardReconciled();
            FlushPendingHomeSilverRefunds();
            FlushPendingHomeDeliveries();
            PruneResolvedJobs();
            if (Find.TickManager.TicksGame % TickInterval != 0) return;
            SettlementServicesReconciler.DetectAndHandleMissingProviders(this);
            SettlementServicesReconciler.ReconcileHiringTransits(this);
            SettlementServicesReconciler.ReconcileAnimalVendorOffers(this);
            SettlementServiceJobScheduler.TickAll(this, TickInterval);
            BoardJobCoordinator.TickDue(this);
            BoardOfferScheduler.TickDue(this);
            compatibilityWorldState.PruneOlderThan(CompatibilityCooldownRetentionTicks);
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
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, itemCustody);
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, hiringCandidateCustody);
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, hiringTransitCustody);
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, animalVendorCustody);
        }


        public void TakeItemCustody(Thing thing) => itemCustody.TryAdd(thing, canMergeWithExistingStacks: false);

        public bool TryTakeItemCustody(Thing thing) => itemCustody.TryAdd(thing, canMergeWithExistingStacks: false);

        public void ReleaseItemCustody(Thing thing)
        {
            if (itemCustody.Contains(thing)) itemCustody.Remove(thing);
        }

        public bool TryAddJobResult(int jobId, Thing thing)
        {
            if (thing == null || thing.Destroyed) return false;

            ServiceJobRecord job = GetJob(jobId);
            if (job == null || job.status != ServiceJobStatus.Active) return false;

            TakeItemCustody(thing);
            job.results.Add(new TargetSnapshot
            {
                kind = TargetKind.Item,
                liveThing = thing,
                snapshotLabel = thing.LabelCap,
                snapshotDefName = thing.def?.defName,
                snapshotQuality = thing.TryGetQuality(out QualityCategory q) ? q : (QualityCategory?)null,
            });
            return true;
        }

        public bool IsTargetReserved(Thing thing, int excludingJobId = -1)
        {
            if (thing is Pawn boardPawn && IsPawnHeldByBoard(boardPawn)) return true;

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

        public bool HasCustodyHeldFreeColonist()
        {
            foreach (ServiceJobRecord job in jobs)
            {
                if (job == null || !job.targetInCustody) continue;
                if (!IsOpenCustodyStatus(job.status)) continue;

                foreach (TargetSnapshot target in job.Targets)
                {
                    if (target?.liveThing is Pawn pawn && !pawn.Destroyed && !pawn.Dead && pawn.IsColonist && pawn.HostFaction == null)
                        return true;
                }
            }
            return false;
        }

        public bool IsPawnHeldByBoard(Pawn pawn)
        {
            if (pawn == null) return false;

            foreach (BoardJobRecord job in boardJobs)
            {
                if (!job.HoldsWorkers || job.pawnsReturned) continue;
                if (job.assignedPawns.Contains(pawn)) return true;
            }
            return false;
        }

        public bool HasBoardHeldFreeColonist()
        {
            foreach (BoardJobRecord job in boardJobs)
            {
                if (!job.HoldsWorkers || job.pawnsReturned) continue;

                foreach (Pawn pawn in job.assignedPawns)
                {
                    if (pawn != null && !pawn.Destroyed && !pawn.Dead && pawn.IsColonist && pawn.HostFaction == null) return true;
                }
            }
            return false;
        }

        private void RebuildIndexes()
        {
            settlementsByWorldObjectId = new Dictionary<int, SettlementRecord>();
            foreach (SettlementRecord record in settlementRecords)
            {
                if (record == null) continue;
                if (settlementsByWorldObjectId.ContainsKey(record.settlementWorldObjectId))
                {
                    Settlement_Services.SupportLog.Warning($"Duplicate settlement record for world object {record.settlementWorldObjectId}, ignoring extra copy.");
                    continue;
                }
                settlementsByWorldObjectId[record.settlementWorldObjectId] = record;
            }

            boardJobsById = new Dictionary<int, BoardJobRecord>();
            foreach (BoardJobRecord boardJob in boardJobs)
            {
                if (boardJob == null) continue;
                if (boardJob.boardJobId < 0 || boardJobsById.ContainsKey(boardJob.boardJobId))
                {
                    Settlement_Services.SupportLog.Warning($"Duplicate or invalid board job id {boardJob.boardJobId}, assigning a fresh id.");
                    boardJob.boardJobId = nextBoardJobId++;
                }
                boardJobsById[boardJob.boardJobId] = boardJob;
                if (boardJob.boardJobId >= nextBoardJobId) nextBoardJobId = boardJob.boardJobId + 1;
            }

            jobsById = new Dictionary<int, ServiceJobRecord>();
            jobsBySettlement = new Dictionary<int, List<ServiceJobRecord>>();
            activeJobsIndex = new List<ServiceJobRecord>();
            foreach (ServiceJobRecord job in jobs)
            {
                if (job == null) continue;
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
        internal List<BoardJobRecord> BoardJobsRaw => boardJobs;

        public IReadOnlyList<BoardJobRecord> AllBoardJobs => boardJobs;

        public List<BoardJobRecord> BoardJobsForSettlement(int settlementWorldObjectId)
        {
            var result = new List<BoardJobRecord>();
            foreach (BoardJobRecord job in boardJobs)
            {
                if (job.settlementWorldObjectId == settlementWorldObjectId) result.Add(job);
            }
            return result;
        }

        public BoardJobRecord GetBoardJob(int boardJobId) =>
            boardJobsById.TryGetValue(boardJobId, out BoardJobRecord job) ? job : null;

        public void AddBoardJob(BoardJobRecord job)
        {
            job.boardJobId = nextBoardJobId++;
            boardJobs.Add(job);
            boardJobsById[job.boardJobId] = job;
        }

        public void RemoveBoardJob(BoardJobRecord job)
        {
            boardJobs.Remove(job);
            boardJobsById.Remove(job.boardJobId);
        }

        public JobBoardRecord GetOrCreateJobBoard(int settlementWorldObjectId)
        {
            SettlementRecord record = GetOrCreateSettlementRecord(settlementWorldObjectId);
            if (record.jobBoard == null) record.jobBoard = new JobBoardRecord();
            return record.jobBoard;
        }

        public JobBoardRecord TryGetJobBoard(int settlementWorldObjectId) =>
            settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record) ? record.jobBoard : null;


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
            int requesterCaravanId = -1, string requesterFactionLoadId = null, string requesterCaravanSnapshotLabel = null, string providerFactionLoadId = null)
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
                providerFactionLoadId = providerFactionLoadId,
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

        public void UpdateJobRequesterCaravan(int jobId, Caravan caravan)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job == null) return;

            job.requesterCaravanId = caravan?.ID ?? -1;
            job.requesterFactionLoadId = (caravan?.Faction ?? Faction.OfPlayer)?.GetUniqueLoadID();
            job.requesterCaravanSnapshotLabel = caravan?.LabelCap;
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

        public void FreezeBuildingCommissionPlan(int jobId, BuildingCommissionPlan plan)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job == null) return;
            job.buildingCommissionPlan = plan;
        }

        public void FreezeMarketPurchasePlan(int jobId, MarketPurchasePlan plan)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job == null) return;
            job.marketPurchasePlan = plan;
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
                usesDynamicStock = SettlementStockCatalog.ItemFor(thingDefName) == null
                    && DynamicStockEngine.FindOfferedEntry(record, thingDefName, out _) != null,
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

        public int RumorCooldownRemainingTicks(int settlementWorldObjectId)
        {
            if (!settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record)) return 0;
            if (record.nextRumorAvailableTick < 0) return 0;
            return Mathf.Max(0, record.nextRumorAvailableTick - Find.TickManager.TicksGame);
        }

        public bool HasRumorQuestFor(int settlementWorldObjectId, int jobId) =>
            settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record)
            && record.lastRumorQuestJobId == jobId;

        public bool HasPendingServiceJob(int settlementWorldObjectId, string serviceDefName, int excludingJobId = -1)
        {
            foreach (ServiceJobRecord job in JobsForSettlement(settlementWorldObjectId))
            {
                if (job.jobId == excludingJobId || job.serviceDefName != serviceDefName) continue;
                if (job.status == ServiceJobStatus.Reserved || job.status == ServiceJobStatus.Active) return true;
            }
            return false;
        }

        public void RecordRumorQuestGenerated(int settlementWorldObjectId, int jobId, int cooldownTicks)
        {
            SettlementRecord record = GetOrCreateSettlementRecord(settlementWorldObjectId);
            record.lastRumorQuestJobId = jobId;
            record.nextRumorAvailableTick = Find.TickManager.TicksGame + cooldownTicks;
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

        public IReadOnlyList<string> GetOrRefreshSpecialtyDefNames(Settlement settlement, Func<List<string>> generator)
        {
            if (settlement == null) return Array.Empty<string>();

            SettlementRecord record = GetOrCreateSettlementRecord(settlement.ID);
            string currentFactionLoadId = settlement.Faction?.GetUniqueLoadID();
            string currentFactionDefName = settlement.Faction?.def?.defName;

            if (record.capability == null)
            {
                record.capability = new SettlementCapabilityRecord
                {
                    specialtyDefNames = generator(),
                    generatedForFactionLoadId = currentFactionLoadId,
                    generatedForFactionDefName = currentFactionDefName,
                    ownerFingerprintInitialized = true,
                };
                return record.capability.specialtyDefNames;
            }

            SettlementCapabilityRecord capability = record.capability;
            if (!capability.ownerFingerprintInitialized)
            {
                capability.generatedForFactionLoadId = currentFactionLoadId;
                capability.generatedForFactionDefName = currentFactionDefName;
                capability.ownerFingerprintInitialized = true;
                return capability.specialtyDefNames;
            }

            bool fingerprintMatches = string.Equals(capability.generatedForFactionLoadId, currentFactionLoadId, StringComparison.Ordinal)
                && string.Equals(capability.generatedForFactionDefName, currentFactionDefName, StringComparison.Ordinal);
            if (fingerprintMatches) return capability.specialtyDefNames;

            string previousFactionLoadId = capability.generatedForFactionLoadId;
            capability.specialtyDefNames = generator();
            capability.generatedForFactionLoadId = currentFactionLoadId;
            capability.generatedForFactionDefName = currentFactionDefName;

            Settlement_Services.SupportLog.Info(
                $"Settlement {settlement.LabelCap} ({settlement.ID}) specialties regenerated after an owner change from '{previousFactionLoadId ?? "none"}' to '{currentFactionLoadId ?? "none"}'.");

            return capability.specialtyDefNames;
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

        public bool TryGetCurrentCapability(Settlement settlement, out IReadOnlyList<string> specialtyDefNames)
        {
            if (settlement != null
                && settlementsByWorldObjectId.TryGetValue(settlement.ID, out SettlementRecord record)
                && record.capability != null
                && record.capability.ownerFingerprintInitialized
                && string.Equals(record.capability.generatedForFactionLoadId, settlement.Faction?.GetUniqueLoadID(), StringComparison.Ordinal)
                && string.Equals(record.capability.generatedForFactionDefName, settlement.Faction?.def?.defName, StringComparison.Ordinal))
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

        public IReadOnlyList<string> GetOrCreatePracticedIdeoIds(Settlement settlement)
        {
            if (settlement == null || !ModsConfig.IdeologyActive) return Array.Empty<string>();

            EnsureRosterInitialized(settlement);
            return GetOrCreateSettlementRecord(settlement.ID).practicedIdeoLoadIds;
        }

        public IReadOnlyList<Ideo> GetPracticedIdeos(Settlement settlement) =>
            SettlementIdeologyRoster.ResolveRoster(GetOrCreatePracticedIdeoIds(settlement));

        public void EnsureRosterInitialized(Settlement settlement, string reservedLoadId = null)
        {
            if (settlement == null || !ModsConfig.IdeologyActive) return;

            SettlementRecord record = GetOrCreateSettlementRecord(settlement.ID);
            if (record.practicedIdeosInitialized) return;

            List<string> reserved = reservedLoadId != null ? new List<string> { reservedLoadId } : null;
            record.practicedIdeoLoadIds = SettlementIdeologyRoster.GenerateRoster(settlement.ID, out int count, reserved);
            record.practicedIdeoCount = count;
            record.practicedIdeosInitialized = true;
        }

        public void ReconcilePracticedIdeoRoster(SettlementRecord record)
        {
            if (record == null || !record.practicedIdeosInitialized || !ModsConfig.IdeologyActive) return;

            List<string> valid = record.practicedIdeoLoadIds.Where(id => IdeoLookup.ResolveIdeo(id) != null).ToList();
            record.practicedIdeoLoadIds = valid.Count < record.practicedIdeoCount
                ? SettlementIdeologyRoster.FillVacancies(record.settlementWorldObjectId, valid, record.practicedIdeoCount)
                : valid;
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
                long restored = (long)stock.currentAmount + (long)intervals * Math.Max(0, refreshAmount);
                stock.currentAmount = (int)Math.Max(0L, Math.Min(restored, capacity));
                stock.lastRefreshTick += intervals * refreshIntervalTicks;
            }

            stock.currentAmount = Mathf.Min(stock.currentAmount, capacity);
            return stock.currentAmount;
        }

        public int TotalReserved(int settlementWorldObjectId, string thingDefName)
        {
            if (!settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record)) return 0;
            return record.reservations.Where(r => !r.usesDynamicStock && r.stockThingDefName == thingDefName).Sum(r => r.amountReserved);
        }

        public int TotalDynamicReserved(int settlementWorldObjectId, string thingDefName)
        {
            if (!settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record)) return 0;
            return record.reservations.Where(r => r.usesDynamicStock && r.stockThingDefName == thingDefName).Sum(r => r.amountReserved);
        }

        public void SyncDynamicStock(Settlement settlement)
        {
            if (settlement?.Faction?.def == null) return;

            int now = Find.TickManager.TicksGame;
            if (dynamicStockSyncTicks.TryGetValue(settlement.ID, out int lastSyncTick) && lastSyncTick == now) return;
            dynamicStockSyncTicks[settlement.ID] = now;

            DynamicStockEngine.Sync(GetOrCreateSettlementRecord(settlement.ID), settlement, now);
        }

        public void MarkStockRefreshDue(Settlement settlement)
        {
            if (settlement?.Faction?.def == null) return;

            SyncDynamicStock(settlement);

            SettlementRecord record = GetOrCreateSettlementRecord(settlement.ID);
            DynamicStockEngine.MarkRefreshDue(record, settlement.Faction.def.techLevel, Find.TickManager.TicksGame);

            foreach (StockRecord stock in record.stock)
            {
                SettlementStockItemReference reference = SettlementStockCatalog.ItemFor(stock.stockThingDefName);
                if (reference == null || !SettlementStockService.IsEligibleForSettlement(settlement, reference)) continue;
                stock.lastRefreshTick -= SettlementStockService.EffectiveRefreshIntervalTicks(settlement, reference);
            }

            dynamicStockSyncTicks.Remove(settlement.ID);
        }

        public IReadOnlyList<DynamicStockEntryView> OfferedDynamicStock(int settlementWorldObjectId)
        {
            if (!settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record)) return Array.Empty<DynamicStockEntryView>();

            var views = new List<DynamicStockEntryView>();
            foreach (DynamicStockPoolRecord pool in record.dynamicStockPools)
            {
                if (!pool.wasEligible) continue;

                string categoryDefName = SettlementDynamicStockCatalog.PoolFor(pool.poolKey)?.category.defName;
                if (categoryDefName == null) continue;

                foreach (DynamicStockEntryRecord entry in pool.entries)
                    if (!entry.retired) views.Add(ViewOf(pool, entry, categoryDefName));
            }
            return views;
        }

        public bool TryGetOfferedDynamicStock(int settlementWorldObjectId, string thingDefName, out DynamicStockEntryView view)
        {
            view = default(DynamicStockEntryView);
            if (!settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record)) return false;

            DynamicStockEntryRecord entry = DynamicStockEngine.FindOfferedEntry(record, thingDefName, out DynamicStockPoolRecord pool);
            if (entry == null) return false;

            string categoryDefName = SettlementDynamicStockCatalog.PoolFor(pool.poolKey)?.category.defName;
            if (categoryDefName == null) return false;

            view = ViewOf(pool, entry, categoryDefName);
            return true;
        }

        public List<int> PruneStaleDynamicStock(SettlementRecord record) => DynamicStockEngine.PruneStale(record);

        private static DynamicStockEntryView ViewOf(DynamicStockPoolRecord pool, DynamicStockEntryRecord entry, string categoryDefName) =>
            new DynamicStockEntryView
            {
                thingDefName = entry.thingDefName,
                categoryDefName = categoryDefName,
                poolKey = pool.poolKey,
                baseCapacity = entry.baseCapacity,
                currentAmount = entry.currentAmount,
            };

        public void ConsumeReservedStock(int jobId, string thingDefName)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job == null) return;
            if (!settlementsByWorldObjectId.TryGetValue(job.settlementWorldObjectId, out SettlementRecord record)) return;

            ReservationRecord reservation = record.reservations.Find(r => r.jobId == jobId && r.stockThingDefName == thingDefName);
            if (reservation == null) return;

            if (reservation.usesDynamicStock)
            {
                DynamicStockEntryRecord dynamicEntry = DynamicStockEngine.FindEntry(record, thingDefName);
                if (dynamicEntry != null) dynamicEntry.currentAmount = Mathf.Max(0, dynamicEntry.currentAmount - reservation.amountReserved);
            }
            else
            {
                StockRecord stock = record.stock.Find(s => s.stockThingDefName == thingDefName);
                if (stock != null) stock.currentAmount = Mathf.Max(0, stock.currentAmount - reservation.amountReserved);
            }

            record.reservations.Remove(reservation);
        }

        public IReadOnlyList<HiringCandidateRecord> GetOrRefreshHiringCandidates(Settlement settlement, Func<int, HiringCandidateRecord> generator)
        {
            if (settlement == null) return Array.Empty<HiringCandidateRecord>();

            SettlementRecord record = GetOrCreateSettlementRecord(settlement.ID);
            int now = Find.TickManager.TicksGame;

            record.hiringCandidates.RemoveAll(c => c.pawn == null || c.pawn.Destroyed || c.pawn.Dead);

            if (record.hiringPoolExpiryTick >= 0 && now < record.hiringPoolExpiryTick) return record.hiringCandidates;

            DisposeHiringCandidates(record);

            int rosterSize = Rand.RangeInclusive(1, 3);
            int failures = 0;
            for (int i = 0; i < rosterSize; i++)
            {
                HiringCandidateRecord candidate;
                try
                {
                    candidate = generator(record.nextHiringCandidateId++);
                }
                catch (System.Exception ex)
                {
                    failures++;
                    Settlement_Services.SupportLog.Error($"Hiring candidate generation threw for settlement {settlement.LabelCap} ({settlement.ID}): {ex}");
                    continue;
                }

                if (candidate?.pawn == null) { failures++; continue; }

                hiringCandidateCustody.TryAdd(candidate.pawn, canMergeWithExistingStacks: false);
                record.hiringCandidates.Add(candidate);
            }

            record.hiringPoolExpiryTick = now + HiringRosterDurationTicks;
            foreach (HiringCandidateRecord candidate in record.hiringCandidates) candidate.expiryTick = record.hiringPoolExpiryTick;

            if (failures > 0)
                Settlement_Services.SupportLog.Warning($"Hiring roster for settlement {settlement.LabelCap} ({settlement.ID}): {failures} of {rosterSize} candidate generation attempt(s) failed, {record.hiringCandidates.Count} succeeded.");

            return record.hiringCandidates;
        }

        public void DisposeHiringCandidates(SettlementRecord record)
        {
            if (record?.hiringCandidates == null) return;

            foreach (HiringCandidateRecord candidate in record.hiringCandidates)
            {
                if (candidate.pawn == null) continue;
                if (hiringCandidateCustody.Contains(candidate.pawn)) hiringCandidateCustody.Remove(candidate.pawn);
                if (!candidate.pawn.Destroyed) candidate.pawn.Destroy(DestroyMode.Vanish);
            }
            record.hiringCandidates.Clear();
        }

        public void ClaimHiringCandidates(int settlementWorldObjectId, IEnumerable<int> candidateIds)
        {
            if (!settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record)) return;

            foreach (int candidateId in candidateIds)
            {
                HiringCandidateRecord candidate = record.hiringCandidates.Find(c => c.candidateId == candidateId);
                if (candidate == null) continue;

                if (candidate.pawn != null && hiringCandidateCustody.Contains(candidate.pawn)) hiringCandidateCustody.Remove(candidate.pawn);
                record.hiringCandidates.Remove(candidate);
            }
        }

        public IReadOnlyList<AnimalVendorOfferRecord> GetOrRefreshAnimalVendorOffers(Settlement settlement, Func<List<Pawn>> generator)
        {
            if (settlement == null) return Array.Empty<AnimalVendorOfferRecord>();

            SettlementRecord record = GetOrCreateSettlementRecord(settlement.ID);
            int now = Find.TickManager.TicksGame;
            string factionLoadId = settlement.Faction?.GetUniqueLoadID();

            PruneAnimalVendorOffers(record);

            if (record.animalVendorNextRefreshTick >= 0
                && !string.Equals(record.animalVendorGeneratedForFactionLoadId, factionLoadId, StringComparison.Ordinal))
            {
                DisposeAnimalVendorOffers(record);
                record.animalVendorNextRefreshTick = -1;
            }

            if (record.animalVendorNextRefreshTick >= 0 && now < record.animalVendorNextRefreshTick) return record.animalVendorOffers;

            if (record.animalVendorNextRefreshTick < 0)
            {
                record.animalVendorNextRefreshTick = now + AnimalVendorRefreshPeriodTicks;
            }
            else
            {
                DisposeAnimalVendorOffers(record);
                int elapsedPeriods = (now - record.animalVendorNextRefreshTick) / AnimalVendorRefreshPeriodTicks + 1;
                record.animalVendorNextRefreshTick += elapsedPeriods * AnimalVendorRefreshPeriodTicks;
            }
            record.animalVendorGeneratedForFactionLoadId = factionLoadId;

            List<Pawn> generated = null;
            try
            {
                generated = generator();
            }
            catch (System.Exception ex)
            {
                Settlement_Services.SupportLog.Error($"Animal vendor generation threw for settlement {settlement.LabelCap} ({settlement.ID}): {ex}");
            }

            if (generated.NullOrEmpty())
            {
                Settlement_Services.SupportLog.Warning($"Animal vendor for settlement {settlement.LabelCap} ({settlement.ID}) generated no animals; the offer stays empty until the next refresh.");
                return record.animalVendorOffers;
            }

            foreach (Pawn pawn in generated)
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;

                if (record.animalVendorOffers.Count >= AnimalVendorMaxOffers || !animalVendorCustody.TryAdd(pawn, canMergeWithExistingStacks: false))
                {
                    if (!pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish);
                    continue;
                }

                record.animalVendorOffers.Add(new AnimalVendorOfferRecord { offerId = record.nextAnimalVendorOfferId++, pawn = pawn });
            }

            return record.animalVendorOffers;
        }

        public bool TryGetAnimalVendorRefreshTick(int settlementWorldObjectId, out int nextRefreshTick)
        {
            if (settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record) && record.animalVendorNextRefreshTick >= 0)
            {
                nextRefreshTick = record.animalVendorNextRefreshTick;
                return true;
            }
            nextRefreshTick = -1;
            return false;
        }

        public void MakeAnimalVendorOfferDue(int settlementWorldObjectId)
        {
            if (settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record) && record.animalVendorNextRefreshTick >= 0)
                record.animalVendorNextRefreshTick = Find.TickManager.TicksGame;
        }

        public bool TryClaimAnimalVendorOffer(int settlementWorldObjectId, int offerId, out AnimalVendorOfferRecord claimed)
        {
            claimed = null;
            if (!settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record)) return false;

            PruneAnimalVendorOffers(record);

            AnimalVendorOfferRecord offer = record.animalVendorOffers.Find(o => o.offerId == offerId);
            if (offer == null || !animalVendorCustody.Contains(offer.pawn)) return false;

            animalVendorCustody.Remove(offer.pawn);
            record.animalVendorOffers.Remove(offer);
            claimed = offer;
            return true;
        }

        public void RestoreAnimalVendorOffer(int settlementWorldObjectId, AnimalVendorOfferRecord offer)
        {
            if (offer?.pawn == null || offer.pawn.Destroyed) return;

            if (!settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record)
                || !animalVendorCustody.TryAdd(offer.pawn, canMergeWithExistingStacks: false))
            {
                Settlement_Services.SupportLog.Warning($"Animal vendor offer {offer.offerId} for settlement {settlementWorldObjectId} could not be restored to vendor custody.");
                Find.WorldPawns.PassToWorld(offer.pawn);
                return;
            }

            int insertAt = record.animalVendorOffers.FindIndex(o => o.offerId > offer.offerId);
            if (insertAt < 0) record.animalVendorOffers.Add(offer);
            else record.animalVendorOffers.Insert(insertAt, offer);
        }

        public void DisposeAnimalVendorOffers(SettlementRecord record)
        {
            if (record?.animalVendorOffers == null) return;

            foreach (AnimalVendorOfferRecord offer in record.animalVendorOffers)
            {
                if (offer?.pawn == null) continue;
                if (animalVendorCustody.Contains(offer.pawn)) animalVendorCustody.Remove(offer.pawn);
                if (!offer.pawn.Destroyed) offer.pawn.Destroy(DestroyMode.Vanish);
            }
            record.animalVendorOffers.Clear();
        }

        public void ResetAnimalVendor(SettlementRecord record)
        {
            if (record == null) return;

            DisposeAnimalVendorOffers(record);
            record.animalVendorNextRefreshTick = -1;
            record.animalVendorGeneratedForFactionLoadId = null;
        }

        private void PruneAnimalVendorOffers(SettlementRecord record)
        {
            for (int i = record.animalVendorOffers.Count - 1; i >= 0; i--)
            {
                AnimalVendorOfferRecord offer = record.animalVendorOffers[i];
                if (offer?.pawn != null && !offer.pawn.Destroyed && !offer.pawn.Dead && animalVendorCustody.Contains(offer.pawn)) continue;

                if (offer?.pawn != null && animalVendorCustody.Contains(offer.pawn)) animalVendorCustody.Remove(offer.pawn);
                record.animalVendorOffers.RemoveAt(i);
            }
        }

        internal IReadOnlyList<HiringTransitRecord> HiringTransitsRaw => hiringTransits;

        public bool BeginHiringTransit(int jobId, int settlementWorldObjectId, PlanetTile originTile, string originFactionLoadId,
            IEnumerable<HiringCandidateRecord> candidates, Map destinationMap, int arrivalTick)
        {
            if (!settlementsByWorldObjectId.TryGetValue(settlementWorldObjectId, out SettlementRecord record)) return false;

            var transitPawns = new List<Pawn>();
            foreach (HiringCandidateRecord candidate in candidates.ToList())
            {
                if (candidate.pawn == null) continue;

                if (hiringCandidateCustody.Contains(candidate.pawn)) hiringCandidateCustody.Remove(candidate.pawn);
                record.hiringCandidates.Remove(candidate);

                hiringTransitCustody.TryAdd(candidate.pawn, canMergeWithExistingStacks: false);
                transitPawns.Add(candidate.pawn);
            }

            if (transitPawns.Count == 0) return false;

            hiringTransits.Add(new HiringTransitRecord
            {
                jobId = jobId,
                originSettlementWorldObjectId = settlementWorldObjectId,
                originTile = originTile,
                originFactionLoadId = originFactionLoadId,
                destinationMapId = destinationMap.uniqueID,
                destinationTile = destinationMap.Tile,
                departureTick = Find.TickManager.TicksGame,
                arrivalTick = arrivalTick,
                pawns = transitPawns,
            });
            return true;
        }

        public bool TryGetHiringTransit(int jobId, out HiringTransitRecord record)
        {
            record = hiringTransits.Find(t => t.jobId == jobId);
            return record != null;
        }

        public List<Pawn> ReleaseHiringTransitForArrival(int jobId)
        {
            HiringTransitRecord record = hiringTransits.Find(t => t.jobId == jobId);
            if (record == null) return new List<Pawn>();

            var pawns = new List<Pawn>();
            foreach (Pawn pawn in record.pawns)
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                if (hiringTransitCustody.Contains(pawn)) hiringTransitCustody.Remove(pawn);
                pawns.Add(pawn);
            }

            hiringTransits.Remove(record);
            return pawns;
        }

        public void AbortHiringTransit(int jobId)
        {
            HiringTransitRecord record = hiringTransits.Find(t => t.jobId == jobId);
            if (record == null) return;

            Faction originFaction = Find.FactionManager.AllFactionsListForReading.Find(f => f.GetUniqueLoadID() == record.originFactionLoadId);
            foreach (Pawn pawn in record.pawns)
            {
                if (pawn == null) continue;
                if (hiringTransitCustody.Contains(pawn)) hiringTransitCustody.Remove(pawn);
                if (pawn.Destroyed || pawn.Dead) continue;

                if (originFaction != null) pawn.SetFaction(originFaction);
                Find.WorldPawns.PassToWorld(pawn);
            }

            hiringTransits.Remove(record);
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

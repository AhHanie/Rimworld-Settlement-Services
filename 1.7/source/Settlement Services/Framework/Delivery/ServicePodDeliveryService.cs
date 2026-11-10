using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Custody;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Delivery
{
    internal static class ServicePodDeliveryService
    {
        private const string NothingToDeliverKey = "SettlementServices.Error.PodDeliveryNothingToDeliver";
        private const string OverCapacityKey = "SettlementServices.Error.PodDeliveryOverCapacity";
        private const string LaunchFailedKey = "SettlementServices.Error.PodDeliveryLaunchFailed";

        internal static ServicePodDeliveryContract BuildContract(ServicePodDeliveryPlan plan, Settlement settlement)
        {
            return new ServicePodDeliveryContract
            {
                state = ServicePodDeliveryState.Pending,
                mapParentId = plan.destination.ID,
                destinationTile = plan.destination.Tile,
                cell = plan.cell,
                originTile = settlement.Tile,
                providerTechLevel = settlement.Faction.def.techLevel,
                podCount = plan.podCount,
                paidSurcharge = plan.TotalFee,
                massCapacityPerPod = plan.massCapacityPerPod,
                maxRangeTiles = plan.maxRange,
            };
        }

        internal static bool TryDispatch(SettlementServicesWorldComponent domain, ServiceJobRecord job, ServiceJobContext ctx)
        {
            ServicePodDeliveryContract contract = job.podDelivery;
            if (contract == null || contract.state != ServicePodDeliveryState.Pending) return false;

            contract.state = ServicePodDeliveryState.Dispatching;

            string failureKey;
            try
            {
                failureKey = TryLaunch(domain, job, ctx, contract);
            }
            catch (Exception ex)
            {
                SupportLog.Error($"Job {job.jobId}: pod delivery launch threw: {ex}");
                failureKey = contract.state == ServicePodDeliveryState.InFlight ? null : LaunchFailedKey;
            }

            if (failureKey == null) return true;

            FallBackToCollection(job, ctx, failureKey);
            return false;
        }

        internal static void FallBackToCollection(ServiceJobRecord job, ServiceJobContext ctx, string reasonKey)
        {
            ServicePodDeliveryContract contract = job.podDelivery;
            if (contract == null) return;

            contract.failureKey = reasonKey;
            bool refunded = RefundSurchargeOnce(job, ctx);
            if (refunded)
            {
                Messages.Message("SettlementServices.Message.PodDeliveryFellBack".Translate(reasonKey.Translate(), contract.paidSurcharge), MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        internal static bool RefundSurchargeOnce(ServiceJobRecord job, ServiceJobContext ctx)
        {
            ServicePodDeliveryContract contract = job?.podDelivery;
            if (contract == null) return false;
            if (contract.state != ServicePodDeliveryState.Pending && contract.state != ServicePodDeliveryState.Dispatching) return false;

            contract.state = ServicePodDeliveryState.FailedRefunded;
            if (contract.paidSurcharge > 0)
                SettlementServiceOrchestrator.ResolvePaymentProvider(job.requestChannel).Refund(contract.paidSurcharge, ctx);
            return true;
        }

        private static string TryLaunch(SettlementServicesWorldComponent domain, ServiceJobRecord job, ServiceJobContext ctx, ServicePodDeliveryContract contract)
        {
            Settlement settlement = ctx.ResolveSettlement();
            if (settlement == null) return "SettlementServices.Error.SettlementNoLongerExists";
            if (!ServicePodDeliveryPlanner.IsTechEligible(settlement)) return ServicePodDeliveryPlanner.TechTooLowKey;
            if (!ServicePodDeliveryPlanner.TryResolveDestination(contract.mapParentId, contract.cell, out MapParent destination, out string destinationKey)) return destinationKey;
            if (!ServicePodDeliveryPlanner.TryGetSpecs(out ServicePodSpecs specs)) return ServicePodDeliveryPlanner.UnavailableKey;

            int distance = Find.WorldGrid.TraversalDistanceBetween(settlement.Tile, destination.Tile, passImpassable: true, int.MaxValue, canTraverseLayers: true);
            if (distance < 0 || distance == int.MaxValue || distance > ServicePodDeliveryPlanner.MaxLaunchDistance(specs, destination.Tile.Layer))
                return ServicePodDeliveryPlanner.OutOfRangeKey;

            List<Thing> contents = CollectContents(domain, job);
            if (contents.Count == 0) return NothingToDeliverKey;

            float capacity = Math.Min(contract.massCapacityPerPod, specs.massCapacity);
            if (!ServicePodDeliveryPlanner.TryPartition(contents, contract.podCount, capacity, out List<List<ServicePodDeliveryPlanner.PodLoad>> pods))
                return OverCapacityKey;

            var infos = new List<ActiveTransporterInfo>();
            var splitOrigins = new Dictionary<Thing, Thing>();
            var released = new HashSet<Thing>();

            foreach (List<ServicePodDeliveryPlanner.PodLoad> load in pods)
            {
                if (load.Count == 0) continue;

                var info = new ActiveTransporterInfo { sentTransporterDef = ThingDefOf.TransportPod };
                infos.Add(info);

                foreach (ServicePodDeliveryPlanner.PodLoad entry in load)
                {
                    Thing toAdd = entry.thing;
                    if (!(toAdd is Pawn))
                    {
                        if (released.Add(entry.thing)) domain.ReleaseItemCustody(entry.thing);
                        if (entry.count < entry.thing.stackCount)
                        {
                            toAdd = entry.thing.SplitOff(entry.count);
                            splitOrigins[toAdd] = entry.thing;
                        }
                    }

                    if (!info.innerContainer.TryAdd(toAdd, canMergeWithExistingStacks: false))
                    {
                        if (splitOrigins.TryGetValue(toAdd, out Thing failedOrigin) && toAdd.holdingOwner == null)
                        {
                            failedOrigin.TryAbsorbStack(toAdd, respectStackLimit: false);
                            splitOrigins.Remove(toAdd);
                        }

                        RestoreContents(domain, infos, splitOrigins, released);
                        return LaunchFailedKey;
                    }
                }
            }

            if (infos.Count > contract.podCount)
            {
                RestoreContents(domain, infos, splitOrigins, released);
                return OverCapacityKey;
            }

            var travelling = (TravellingTransporters)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.TravellingTransporters);
            travelling.SetFaction(Faction.OfPlayer);
            travelling.destinationTile = destination.Tile;
            PlanetTile origin = settlement.Tile;
            if (origin.Layer != destination.Tile.Layer) origin = destination.Tile.Layer.GetClosestTile_NewTemp(origin);
            travelling.Tile = origin;
            travelling.arrivalAction = new TransportersArrivalAction_ServiceDelivery(destination, contract.cell, job.jobId);
            Find.WorldObjects.Add(travelling);

            foreach (ActiveTransporterInfo info in infos) travelling.AddTransporter(info, justLeftTheMap: false);

            contract.travellingObjectId = travelling.ID;
            contract.state = ServicePodDeliveryState.InFlight;
            job.targetInCustody = false;
            job.results.Clear();

            Messages.Message("SettlementServices.Message.PodDeliveryLaunched".Translate(infos.Count, destination.LabelCap), travelling, MessageTypeDefOf.PositiveEvent, historical: false);
            return null;
        }

        private static List<Thing> CollectContents(SettlementServicesWorldComponent domain, ServiceJobRecord job)
        {
            var contents = new List<Thing>();

            if (job.targetInCustody)
            {
                foreach (TargetSnapshot target in job.Targets)
                {
                    Thing thing = target?.liveThing;
                    if (TargetCustodyService.RequiresReturn(thing) && IsHeldInCustody(domain, thing)) contents.Add(thing);
                }
            }

            if (!job.results.NullOrEmpty())
            {
                foreach (TargetSnapshot result in job.results)
                {
                    Thing thing = result?.liveThing;
                    if (TargetCustodyService.RequiresReturn(thing) && IsHeldInCustody(domain, thing)) contents.Add(thing);
                }
            }

            return contents.Distinct().ToList();
        }

        private static bool IsHeldInCustody(SettlementServicesWorldComponent domain, Thing thing)
        {
            if (thing is Pawn pawn) return !pawn.Spawned && pawn.holdingOwner == null;
            return domain.IsInItemCustody(thing);
        }

        private static void RestoreContents(SettlementServicesWorldComponent domain, List<ActiveTransporterInfo> infos, Dictionary<Thing, Thing> splitOrigins, HashSet<Thing> released)
        {
            foreach (ActiveTransporterInfo info in infos)
            {
                foreach (Thing thing in info.innerContainer.ToList())
                {
                    info.innerContainer.Remove(thing);
                    if (thing is Pawn) continue;

                    if (splitOrigins.TryGetValue(thing, out Thing origin)) origin.TryAbsorbStack(thing, respectStackLimit: false);
                }
            }

            foreach (Thing item in released) domain.TakeItemCustody(item);
        }

        internal static void NotifyArrival(int jobId)
        {
            ServiceJobRecord job = SettlementServicesWorldComponent.Current?.GetJob(jobId);
            ServicePodDeliveryContract contract = job?.podDelivery;
            if (contract == null || contract.state != ServicePodDeliveryState.InFlight) return;

            contract.state = ServicePodDeliveryState.Delivered;
        }

        internal static void HoldForRecovery(List<ActiveTransporterInfo> transporters, int jobId)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            int held = 0;

            foreach (ActiveTransporterInfo info in transporters)
            {
                foreach (Thing thing in info.innerContainer.ToList())
                {
                    info.innerContainer.Remove(thing);

                    if (thing is Pawn pawn)
                    {
                        if (!Find.WorldPawns.Contains(pawn)) Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
                        domain.QueueHomeDelivery(new TargetSnapshot { kind = TargetKind.Pawn, liveThing = pawn, snapshotLabel = pawn.LabelShort });
                    }
                    else
                    {
                        domain.TakeItemCustody(thing);
                        domain.QueueHomeDelivery(new TargetSnapshot { kind = TargetKind.Item, liveThing = thing, snapshotLabel = thing.LabelCap });
                    }
                    held++;
                }
            }

            NotifyArrival(jobId);
            Find.LetterStack.ReceiveLetter(
                "SettlementServices.Letter.PodDeliveryHeldLabel".Translate(),
                "SettlementServices.Letter.PodDeliveryHeldText".Translate(held),
                LetterDefOf.NegativeEvent);
        }

        internal static void ReconcileInFlight(SettlementServicesWorldComponent domain)
        {
            foreach (ServiceJobRecord job in domain.JobsRaw)
            {
                ServicePodDeliveryContract contract = job.podDelivery;
                if (contract == null) continue;

                if (contract.state == ServicePodDeliveryState.Pending || contract.state == ServicePodDeliveryState.Dispatching)
                {
                    bool resolved = job.status == ServiceJobStatus.Completed || job.status == ServiceJobStatus.Collected
                        || job.status == ServiceJobStatus.Cancelled || job.status == ServiceJobStatus.Failed
                        || job.status == ServiceJobStatus.AwaitingCollection;
                    if (resolved) RefundSurchargeOnce(job, new ServiceJobContext(domain, job));
                    continue;
                }

                if (contract.state != ServicePodDeliveryState.InFlight) continue;
                if (Find.WorldObjects.AllWorldObjects.Any(w => w.ID == contract.travellingObjectId && w is TravellingTransporters)) continue;

                contract.state = ServicePodDeliveryState.Delivered;
            }
        }
    }
}

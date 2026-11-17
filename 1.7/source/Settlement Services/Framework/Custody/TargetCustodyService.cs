using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Compatibility;
using Settlement_Services.Framework.Compat.VehicleFramework;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Custody
{
    public static class TargetCustodyService
    {
        public static bool ValidateTargets(ServiceJobContext ctx, Caravan caravan, out string errorKey)
        {
            List<TargetSnapshot> targets = ctx.Job.Targets.ToList();
            if (targets.Count == 0) { errorKey = null; return true; }

            foreach (TargetSnapshot target in targets)
            {
                Thing thing = target?.liveThing;
                if (thing == null || thing.Destroyed) { errorKey = "SettlementServices.Error.TargetNoLongerExists"; return false; }
                if (ctx.Domain.IsTargetReserved(thing, ctx.Job.jobId)) { errorKey = "SettlementServices.Error.TargetAlreadyInUse"; return false; }

                if (thing is Pawn pawn)
                {
                    if (caravan == null || !caravan.PawnsListForReading.Contains(pawn))
                    { errorKey = "SettlementServices.Error.TargetNotInCaravan"; return false; }
                }
                else
                {
                    if (caravan == null || !CaravanInventoryUtility.AllInventoryItems(caravan).Contains(thing))
                    { errorKey = "SettlementServices.Error.TargetNotInCaravan"; return false; }
                }
            }

            errorKey = null;
            return true;
        }

        private static readonly Dictionary<int, CaravanCustodyCargoTransfer> pendingCargoTransfers = new Dictionary<int, CaravanCustodyCargoTransfer>();

        public static bool TryTakeCustody(ServiceJobContext ctx, Caravan caravan, out string errorKey)
        {
            if (!ValidateTargets(ctx, caravan, out errorKey)) return false;

            List<TargetSnapshot> targets = ctx.Job.Targets.ToList();
            if (targets.Count == 0) return true;

            List<Pawn> departing = targets.Select(t => t?.liveThing as Pawn).Where(p => p != null).ToList();
            Settlement intendedSettlement = ctx.Job.requestChannel == RequestChannel.InPerson ? ctx.ResolveSettlement() : null;
            bool parkedVehicleException = departing.Count > 0
                && CaravanOwnershipRules.GetDepartureBlockKey(caravan, departing) != null
                && CaravanOwnershipRules.CanParkVehicleCaravanForService(caravan, departing, intendedSettlement);
            int originalCaravanId = parkedVehicleException ? caravan.ID : -1;
            PlanetTile originalTile = parkedVehicleException ? caravan.Tile : PlanetTile.Invalid;
            List<Pawn> parkedVehicles = parkedVehicleException
                ? caravan.pawns.InnerListForReading.Where(p => VehicleFrameworkAdapter.IsVehicle(p)).ToList() : null;
            CaravanCustodyCargoTransfer cargo = CaravanCustodyCargoTransfer.Empty;
            if (departing.Count > 0 && !CaravanCustodyCargoTransfer.TryPrepare(caravan, departing, out cargo, out errorKey,
                intendedSettlement, allowVehicleParking: intendedSettlement != null)) return false;

            Caravan currentCaravan = caravan;
            var detached = new List<TargetSnapshot>();

            foreach (TargetSnapshot target in targets)
            {
                Thing thing = target.liveThing;
                if (thing is Pawn pawn)
                {
                    if (!TryDetachPawn(ctx, ref currentCaravan, pawn, out errorKey))
                    {
                        RevertDetachedTargets(ctx, detached, currentCaravan);
                        cargo.Rollback();
                        return false;
                    }
                }
                else
                {
                    if (!TryDetachItem(ctx, thing, out errorKey))
                    {
                        RevertDetachedTargets(ctx, detached, currentCaravan);
                        cargo.Rollback();
                        return false;
                    }
                }

                detached.Add(target);
            }

            if (parkedVehicleException && (currentCaravan != caravan || caravan.Destroyed || !caravan.Spawned
                || caravan.ID != originalCaravanId || caravan.Tile != originalTile
                || parkedVehicles.Count == 0 || parkedVehicles.Any(v => v.Destroyed || !caravan.pawns.Contains(v)
                    || !caravan.PawnsListForReading.Contains(v))
                || caravan.PawnsListForReading.Any(p => !VehicleFrameworkAdapter.IsVehicle(p))))
            {
                SupportLog.Error($"Job {ctx.Job.jobId}: the parked vehicle caravan changed while taking custody; restoring the targets.");
                RevertDetachedTargets(ctx, detached, currentCaravan);
                cargo.Rollback();
                errorKey = "SettlementServices.Error.TargetCouldNotBeDetached";
                return false;
            }

            if (currentCaravan != null && !currentCaravan.Destroyed && currentCaravan.PawnsListForReading.Count == 0) currentCaravan.Destroy();

            if (currentCaravan != caravan) ctx.Domain.UpdateJobRequesterCaravan(ctx.Job.jobId, currentCaravan);

            if (cargo.MovedCount > 0) pendingCargoTransfers[ctx.Job.jobId] = cargo;

            ctx.Job.targetInCustody = true;
            errorKey = null;
            return true;
        }

        internal static void CommitCargoTransfer(int jobId)
        {
            if (!pendingCargoTransfers.TryGetValue(jobId, out CaravanCustodyCargoTransfer cargo)) return;
            pendingCargoTransfers.Remove(jobId);
            cargo.Commit();
        }

        internal static void RollbackCargoTransfer(ServiceJobRecord job)
        {
            if (!pendingCargoTransfers.TryGetValue(job.jobId, out CaravanCustodyCargoTransfer cargo)) return;
            pendingCargoTransfers.Remove(job.jobId);

            if (job.targetInCustody) cargo.Commit();
            else cargo.Rollback();
        }

        private static bool TryDetachItem(ServiceJobContext ctx, Thing thing, out string errorKey)
        {
            errorKey = null;
            ThingOwner owner = thing.holdingOwner;
            if (owner == null || !owner.Contains(thing))
            {
                errorKey = "SettlementServices.Error.TargetCouldNotBeDetached";
                return false;
            }

            owner.Remove(thing);
            if (ctx.Domain.TryTakeItemCustody(thing)) return true;

            owner.TryAdd(thing, canMergeWithExistingStacks: false);
            errorKey = "SettlementServices.Error.TargetCouldNotBeDetached";
            return false;
        }

        internal static bool TryDetachPawn(ServiceJobContext ctx, ref Caravan currentCaravan, Pawn pawn, out string errorKey)
        {
            errorKey = null;

            bool wasInWorld = Find.WorldPawns.Contains(pawn);
            if (wasInWorld) Find.WorldPawns.RemovePawn(pawn);
            Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);

            if (SettlementServicesCompatibilityRegistry.TryGetCustodyLifecycle(ctx, currentCaravan, pawn, out ICompatibilityCustodyLifecycle lifecycle))
            {
                if (!lifecycle.TryPrepareForTargetCustody(ctx, currentCaravan, pawn, out Caravan preparedCaravan, out errorKey))
                {
                    RestoreWorldPawnState(pawn, wasInWorld);
                    return false;
                }

                currentCaravan = preparedCaravan;
            }
            else
            {
                currentCaravan?.RemovePawn(pawn);
            }

            if (pawn.holdingOwner != null)
            {
                RestoreWorldPawnState(pawn, wasInWorld);
                errorKey = "SettlementServices.Error.TargetCouldNotBeDetached";
                return false;
            }

            MothballImmediately(pawn);
            return true;
        }

        private static void RestoreWorldPawnState(Pawn pawn, bool wasInWorld)
        {
            if (pawn.holdingOwner == null || wasInWorld) return;
            if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
        }

        private static void RevertDetachedTargets(ServiceJobContext ctx, List<TargetSnapshot> detached, Caravan currentCaravan)
        {
            ReturnTargets(ctx, detached, currentCaravan, allowCreatingCaravan: true, out _);
        }

        public static void ReturnCustody(ServiceJobContext ctx, Caravan caravan)
        {
            ServiceJobRecord job = ctx.Job;
            if (!job.targetInCustody) return;

            bool allReturned = ReturnTargets(ctx, job.Targets, caravan, allowCreatingCaravan: true, out _);
            if (allReturned) job.targetInCustody = false;
            else SupportLog.Error($"Job {job.jobId}: could not verify every custody target's return to its caravan; leaving the job recoverable instead of losing it.");
        }

        private static bool ReturnTargets(ServiceJobContext ctx, IEnumerable<TargetSnapshot> snapshots, Caravan caravan, bool allowCreatingCaravan, out Caravan finalCaravan)
        {
            List<Thing> things = snapshots.Select(t => t?.liveThing).Where(RequiresReturn).ToList();
            List<Pawn> pawns = things.OfType<Pawn>().ToList();
            Caravan current = ResolveReturnCaravan(ctx, caravan, pawns, allowCreatingCaravan);
            bool allReturned = true;

            foreach (Pawn pawn in pawns)
            {
                if (current != null && TryReturnPawnToCaravan(ctx, current, pawn, out Caravan updated)) current = updated;
                else allReturned = false;
            }

            foreach (Thing item in things.Where(t => !(t is Pawn)))
            {
                if (current != null && TryReturnItem(item, current)) continue;
                allReturned = false;
            }

            finalCaravan = current;
            return allReturned;
        }

        private static Caravan ResolveReturnCaravan(ServiceJobContext ctx, Caravan current, List<Pawn> returning, bool allowCreatingCaravan)
        {
            if (current != null && !current.Destroyed)
            {
                if (returning.Count == 0) return current;
                return CaravanOwnershipRules.GroupWouldHaveCapableOwner(current, returning) ? current : null;
            }

            if (!allowCreatingCaravan || returning.Count == 0) return null;
            if (!CaravanOwnershipRules.HasCapableOwner(returning, Faction.OfPlayer)) return null;

            PlanetTile tile = ctx.ResolveSettlement()?.Tile ?? ctx.Job.settlementTile;
            if (!tile.Valid || !tile.LayerDef.canFormCaravans) return null;

            Caravan created = CaravanMaker.MakeCaravan(Enumerable.Empty<Pawn>(), Faction.OfPlayer, tile, true);
            created.Name = CaravanNameGenerator.GenerateCaravanName(created);
            return created;
        }

        internal static List<Pawn> HeldPawns(ServiceJobRecord job)
        {
            var pawns = new List<Pawn>();
            if (job == null) return pawns;
            if (job.targetInCustody)
                pawns.AddRange(job.Targets.Select(t => t?.liveThing as Pawn)
                    .Where(p => RequiresReturn(p) && p.GetCaravan() == null && !p.Spawned));
            if (!job.results.NullOrEmpty())
                pawns.AddRange(job.results.Select(t => t?.liveThing as Pawn)
                    .Where(p => RequiresReturn(p) && p.GetCaravan() == null && !p.Spawned));
            return pawns;
        }

        internal static string GetCollectBlockKey(ServiceJobRecord job, Caravan caravan, PlanetTile settlementTile)
        {
            if (caravan == null || caravan.Destroyed || !caravan.Spawned || !caravan.IsPlayerControlled) return "SettlementServices.Error.CollectCaravanIneligible";
            if (caravan.pather != null && caravan.pather.Moving) return "SettlementServices.Error.CollectCaravanMoving";
            if (caravan.Tile != settlementTile) return "SettlementServices.Error.CollectCaravanWrongTile";
            return CaravanOwnershipRules.GroupWouldHaveCapableOwner(caravan, HeldPawns(job)) ? null : CaravanOwnershipRules.NoCapableOwnerKey;
        }

        public static void CollectAll(ServiceJobContext ctx, Caravan caravan)
        {
            ServiceJobRecord job = ctx.Job;
            Caravan current = caravan;

            if (job.targetInCustody)
            {
                bool allReturned = ReturnTargets(ctx, job.Targets, current, allowCreatingCaravan: false, out Caravan updatedCaravan);
                if (updatedCaravan != null) current = updatedCaravan;

                if (allReturned) job.targetInCustody = false;
                else SupportLog.Error($"Job {job.jobId}: could not verify every custody target's return during collection; leaving the job recoverable instead of losing it.");
            }

            if (!job.results.NullOrEmpty())
            {
                var remainingResults = new List<TargetSnapshot>();
                foreach (TargetSnapshot result in job.results)
                {
                    if (!RequiresReturn(result.liveThing)) continue;

                    if (TryReturnThing(ctx, result.liveThing, current, out Caravan updated)) current = updated;
                    else remainingResults.Add(result);
                }
                job.results.Clear();
                job.results.AddRange(remainingResults);
            }
        }

        public static void QueueHomeDeliveryForJob(SettlementServicesWorldComponent domain, ServiceJobRecord job)
        {
            if (job.targetInCustody)
            {
                foreach (TargetSnapshot target in job.Targets)
                {
                    Thing thing = target?.liveThing;
                    if (!RequiresReturn(thing)) continue;
                    if (thing is Pawn pawn && (pawn.Spawned || pawn.holdingOwner != null)) continue;
                    domain.QueueHomeDelivery(target);
                }
                job.targetInCustody = false;
            }

            if (!job.results.NullOrEmpty())
            {
                foreach (TargetSnapshot result in job.results) domain.QueueHomeDelivery(result);
                job.results.Clear();
            }
        }

        public static bool TryCreateRecoveryCaravanAndCollectAll(SettlementServicesWorldComponent domain, List<ServiceJobRecord> jobs, PlanetTile tile)
        {
            if (!tile.Valid || !tile.LayerDef.canFormCaravans) return false;

            var pawns = new List<(ServiceJobRecord job, Pawn pawn)>();
            var items = new List<Thing>();
            foreach (ServiceJobRecord job in jobs)
            {
                CollectRecoverable(job, job.Targets, pawns, items);
                CollectRecoverable(job, job.results, pawns, items);
            }

            if (pawns.Count == 0) return false;

            List<Pawn> pawnList = pawns.Select(p => p.pawn).ToList();
            if (!CaravanOwnershipRules.HasCapableOwner(pawnList, Faction.OfPlayer)) return false;
            foreach (Thing item in items)
            {
                if (CaravanInventoryUtility.FindPawnToMoveInventoryTo(item, pawnList, null) == null) return false;
            }

            Caravan caravan = CaravanMaker.MakeCaravan(Enumerable.Empty<Pawn>(), Faction.OfPlayer, tile, true);
            var failedJobs = new HashSet<ServiceJobRecord>();
            foreach ((ServiceJobRecord job, Pawn pawn) in pawns)
            {
                if (TryReturnPawnToCaravan(new ServiceJobContext(domain, job), caravan, pawn, out Caravan updated)) caravan = updated;
                else failedJobs.Add(job);
            }
            if (failedJobs.Count > 0) SupportLog.Error("Recovery caravan creation could not verify every pawn's return; the affected jobs will be delivered home instead.");

            foreach (Thing item in items)
            {
                SettlementServicesWorldComponent.Current.ReleaseItemCustody(item);
                CaravanInventoryUtility.GiveThing(caravan, item);
            }

            caravan.Name = CaravanNameGenerator.GenerateCaravanName(caravan);

            foreach (ServiceJobRecord job in jobs)
            {
                if (!failedJobs.Contains(job)) job.targetInCustody = false;
                job.results.Clear();
            }

            return true;
        }

        internal static bool RequiresReturn(Thing thing)
        {
            if (thing == null || thing.Destroyed) return false;
            return !(thing is Pawn pawn && pawn.Dead);
        }

        private static void CollectRecoverable(ServiceJobRecord job, IEnumerable<TargetSnapshot> snapshots, List<(ServiceJobRecord job, Pawn pawn)> pawns, List<Thing> items)
        {
            if (snapshots == null) return;

            foreach (TargetSnapshot snapshot in snapshots)
            {
                Thing thing = snapshot?.liveThing;
                if (thing == null || thing.Destroyed) continue;

                if (thing is Pawn pawn)
                {
                    if (!pawn.Dead) pawns.Add((job, pawn));
                }
                else
                {
                    items.Add(thing);
                }
            }
        }

        public static bool TryDeliverHome(TargetSnapshot snapshot)
        {
            Thing thing = snapshot.liveThing;
            if (!RequiresReturn(thing)) return true;

            Map homeMap = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
            if (homeMap == null) return false;

            if (thing is Pawn pawn)
            {
                if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
                GenSpawn.Spawn(pawn, DropCellFinder.TradeDropSpot(homeMap), homeMap);
            }
            else
            {
                SettlementServicesWorldComponent.Current.ReleaseItemCustody(thing);
                GenPlace.TryPlaceThing(thing, homeMap.Center, homeMap, ThingPlaceMode.Near);
            }
            return true;
        }

        private static bool TryReturnThing(ServiceJobContext ctx, Thing thing, Caravan caravan, out Caravan resultCaravan)
        {
            if (thing is Pawn pawn) return TryReturnPawnToCaravan(ctx, caravan, pawn, out resultCaravan);

            resultCaravan = caravan;
            return TryReturnItem(thing, caravan);
        }

        private static bool TryReturnItem(Thing thing, Caravan caravan)
        {
            if (thing == null || caravan == null || caravan.Destroyed) return false;

            Pawn receiver = CaravanInventoryUtility.FindPawnToMoveInventoryTo(thing, caravan.PawnsListForReading, null);
            if (receiver?.inventory?.innerContainer == null) return false;

            SettlementServicesWorldComponent.Current.ReleaseItemCustody(thing);
            if (receiver.inventory.innerContainer.TryAdd(thing)) return true;

            SettlementServicesWorldComponent.Current.TakeItemCustody(thing);
            return false;
        }

        internal static bool TryReturnPawnToCaravan(ServiceJobContext ctx, Caravan caravan, Pawn pawn, out Caravan resultCaravan)
        {
            resultCaravan = caravan;
            if (pawn == null || caravan == null) return false;

            if (pawn.holdingOwner != null)
            {
                Caravan owner = pawn.holdingOwner.Owner as Caravan;
                return owner == caravan && !caravan.Destroyed && caravan.pawns.Contains(pawn);
            }

            resultCaravan = SettlementServicesCompatibilityRegistry.TryGetCustodyLifecycle(ctx, caravan, pawn, out ICompatibilityCustodyLifecycle lifecycle)
                ? lifecycle.ReturnPawnToCaravan(ctx, caravan, pawn)
                : AddReturningPawnToCaravan(caravan, pawn);

            if (resultCaravan == null || resultCaravan.Destroyed || !resultCaravan.pawns.Contains(pawn)) return false;

            if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
            Find.WorldPawns.PassToWorld(pawn);
            return true;
        }

        private static Caravan AddReturningPawnToCaravan(Caravan caravan, Pawn pawn)
        {
            if (pawn.RaceProps.Humanlike && pawn.Faction != caravan.Faction && pawn.guest != null && pawn.guest.HostFaction == caravan.Faction)
            {
                if (pawn.Spawned) pawn.DeSpawnOrDeselect();
                caravan.pawns.TryAdd(pawn);
            }
            else
            {
                caravan.AddPawn(pawn, true);
            }
            return caravan;
        }

        internal static void ParkPawnInWorld(Pawn pawn)
        {
            if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);

            bool wasTeleporting = pawn.teleporting;
            pawn.teleporting = true;
            try
            {
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }
            finally
            {
                pawn.teleporting = wasTeleporting;
            }

            MothballImmediately(pawn);
        }

        private static AccessTools.FieldRef<WorldPawns, HashSet<Pawn>> aliveFieldRef;
        private static AccessTools.FieldRef<WorldPawns, HashSet<Pawn>> mothballedFieldRef;
        private static bool mothballReflectionFailed;

        private static void MothballImmediately(Pawn pawn)
        {
            if (mothballReflectionFailed) return;
            try
            {
                if (aliveFieldRef == null)
                {
                    aliveFieldRef = AccessTools.FieldRefAccess<WorldPawns, HashSet<Pawn>>("pawnsAlive");
                    mothballedFieldRef = AccessTools.FieldRefAccess<WorldPawns, HashSet<Pawn>>("pawnsMothballed");
                }
                HashSet<Pawn> alive = aliveFieldRef(Find.WorldPawns);
                HashSet<Pawn> mothballed = mothballedFieldRef(Find.WorldPawns);
                if (alive.Remove(pawn)) mothballed.Add(pawn);
            }
            catch (Exception ex)
            {
                mothballReflectionFailed = true;
                Logger.Exception(ex, "Could not force-mothball a custody-held pawn; it will mothball naturally within ~15,000 ticks instead");
            }
        }
    }
}

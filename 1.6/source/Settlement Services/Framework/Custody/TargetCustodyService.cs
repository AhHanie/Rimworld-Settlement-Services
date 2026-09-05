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
                    if (thing.holdingOwner?.Owner != caravan)
                    { errorKey = "SettlementServices.Error.TargetNotInCaravan"; return false; }
                }
            }

            errorKey = null;
            return true;
        }

        public static bool TryTakeCustody(ServiceJobContext ctx, Caravan caravan, out string errorKey)
        {
            if (!ValidateTargets(ctx, caravan, out errorKey)) return false;

            List<TargetSnapshot> targets = ctx.Job.Targets.ToList();
            if (targets.Count == 0) return true;

            Caravan currentCaravan = caravan;
            foreach (TargetSnapshot target in targets)
            {
                Thing thing = target.liveThing;
                if (thing is Pawn pawn)
                {
                    if (SettlementServicesCompatibilityRegistry.TryGetCustodyLifecycle(ctx, thing, out ICompatibilityCustodyLifecycle lifecycle))
                    {
                        if (!lifecycle.TryPrepareForTargetCustody(ctx, currentCaravan, thing, out Caravan preparedCaravan, out errorKey))
                            return false;

                        currentCaravan = preparedCaravan;
                        Find.WorldPawns.RemovePawn(pawn);
                        Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
                        MothballImmediately(pawn);
                    }
                    else
                    {
                        Find.WorldPawns.RemovePawn(pawn);
                        Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
                        currentCaravan?.RemovePawn(pawn);
                        MothballImmediately(pawn);
                    }
                }
                else
                {
                    thing.holdingOwner.Remove(thing);
                    ctx.Domain.TakeItemCustody(thing);
                }
            }

            if (currentCaravan != null && !currentCaravan.Destroyed && currentCaravan.PawnsListForReading.Count == 0) currentCaravan.Destroy();

            if (currentCaravan != caravan) ctx.Domain.UpdateJobRequesterCaravan(ctx.Job.jobId, currentCaravan);

            ctx.Job.targetInCustody = true;
            errorKey = null;
            return true;
        }

        public static void ReturnCustody(ServiceJobContext ctx, Caravan caravan)
        {
            ServiceJobRecord job = ctx.Job;
            if (!job.targetInCustody) return;

            Caravan current = caravan;
            foreach (TargetSnapshot target in job.Targets)
            {
                if (target?.liveThing == null) continue;
                current = EnsureLiveCaravan(ctx, current);
                current = ReturnThing(ctx, target.liveThing, current);
            }
            job.targetInCustody = false;
        }

        private static Caravan EnsureLiveCaravan(ServiceJobContext ctx, Caravan current)
        {
            if (current != null && !current.Destroyed) return current;

            PlanetTile tile = ctx.ResolveSettlement()?.Tile ?? ctx.Job.settlementTile;
            if (!tile.Valid || !tile.LayerDef.canFormCaravans) return null;

            Caravan created = CaravanMaker.MakeCaravan(Enumerable.Empty<Pawn>(), Faction.OfPlayer, tile, true);
            created.Name = CaravanNameGenerator.GenerateCaravanName(created);
            return created;
        }

        public static void CollectAll(ServiceJobContext ctx, Caravan caravan)
        {
            ServiceJobRecord job = ctx.Job;
            Caravan current = caravan;

            if (job.targetInCustody)
            {
                foreach (TargetSnapshot target in job.Targets)
                    if (target?.liveThing != null) current = ReturnThing(ctx, target.liveThing, current);
                job.targetInCustody = false;
            }

            if (!job.results.NullOrEmpty())
            {
                foreach (TargetSnapshot result in job.results)
                {
                    if (result.liveThing != null && !result.liveThing.Destroyed)
                        current = ReturnThing(ctx, result.liveThing, current);
                }
                job.results.Clear();
            }
        }

        public static void QueueHomeDeliveryForJob(SettlementServicesWorldComponent domain, ServiceJobRecord job)
        {
            if (job.targetInCustody)
            {
                foreach (TargetSnapshot target in job.Targets)
                    if (target != null) domain.QueueHomeDelivery(target);
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
            foreach (Thing item in items)
            {
                if (CaravanInventoryUtility.FindPawnToMoveInventoryTo(item, pawnList, null) == null) return false;
            }

            Caravan caravan = CaravanMaker.MakeCaravan(Enumerable.Empty<Pawn>(), Faction.OfPlayer, tile, true);
            foreach ((ServiceJobRecord job, Pawn pawn) in pawns)
                caravan = ReturnPawnToCaravan(new ServiceJobContext(domain, job), caravan, pawn);

            foreach (Thing item in items)
            {
                SettlementServicesWorldComponent.Current.ReleaseItemCustody(item);
                CaravanInventoryUtility.GiveThing(caravan, item);
            }

            caravan.Name = CaravanNameGenerator.GenerateCaravanName(caravan);

            foreach (ServiceJobRecord job in jobs)
            {
                job.targetInCustody = false;
                job.results.Clear();
            }

            return true;
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
            if (thing == null || thing.Destroyed) return true;

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

        private static Caravan ReturnThing(ServiceJobContext ctx, Thing thing, Caravan caravan)
        {
            if (thing is Pawn pawn)
            {
                return ReturnPawnToCaravan(ctx, caravan, pawn);
            }

            SettlementServicesWorldComponent.Current.ReleaseItemCustody(thing);
            CaravanInventoryUtility.GiveThing(caravan, thing);
            return caravan;
        }

        internal static Caravan ReturnPawnToCaravan(ServiceJobContext ctx, Caravan caravan, Pawn pawn)
        {
            Caravan resultCaravan = SettlementServicesCompatibilityRegistry.TryGetCustodyLifecycle(ctx, pawn, out ICompatibilityCustodyLifecycle lifecycle)
                ? lifecycle.ReturnPawnToCaravan(ctx, caravan, pawn)
                : AddReturningPawnToCaravan(caravan, pawn);

            if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
            Find.WorldPawns.PassToWorld(pawn);
            return resultCaravan;
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

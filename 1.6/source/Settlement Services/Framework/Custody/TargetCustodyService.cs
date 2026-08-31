using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Custody
{
    public static class TargetCustodyService
    {
        public static bool TryTakeCustody(ServiceJobContext ctx, Caravan caravan, out string errorKey)
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

            foreach (TargetSnapshot target in targets)
            {
                Thing thing = target.liveThing;
                if (thing is Pawn pawn)
                {
                    Find.WorldPawns.RemovePawn(pawn);
                    Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
                    caravan.RemovePawn(pawn);
                    MothballImmediately(pawn);
                }
                else
                {
                    thing.holdingOwner.Remove(thing);
                    ctx.Domain.TakeItemCustody(thing);
                }
            }

            if (caravan != null && !caravan.Destroyed && caravan.PawnsListForReading.Count == 0) caravan.Destroy();

            ctx.Job.targetInCustody = true;
            errorKey = null;
            return true;
        }

        public static void ReturnCustody(ServiceJobRecord job, Caravan caravan)
        {
            if (!job.targetInCustody) return;
            foreach (TargetSnapshot target in job.Targets)
                if (target?.liveThing != null) ReturnThing(target.liveThing, caravan);
            job.targetInCustody = false;
        }

        public static void CollectAll(ServiceJobRecord job, Caravan caravan)
        {
            if (job.targetInCustody)
            {
                foreach (TargetSnapshot target in job.Targets)
                    if (target?.liveThing != null) ReturnThing(target.liveThing, caravan);
                job.targetInCustody = false;
            }

            if (!job.results.NullOrEmpty())
            {
                foreach (TargetSnapshot result in job.results)
                {
                    if (result.liveThing != null && !result.liveThing.Destroyed)
                        ReturnThing(result.liveThing, caravan);
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

        public static bool TryCreateRecoveryCaravanAndCollectAll(List<ServiceJobRecord> jobs, PlanetTile tile)
        {
            if (!tile.Valid || !tile.LayerDef.canFormCaravans) return false;

            var pawns = new List<Pawn>();
            var items = new List<Thing>();
            foreach (ServiceJobRecord job in jobs)
            {
                CollectRecoverable(job.Targets, pawns, items);
                CollectRecoverable(job.results, pawns, items);
            }

            if (pawns.Count == 0) return false;

            foreach (Thing item in items)
            {
                if (CaravanInventoryUtility.FindPawnToMoveInventoryTo(item, pawns, null) == null) return false;
            }

            Caravan caravan = CaravanMaker.MakeCaravan(Enumerable.Empty<Pawn>(), Faction.OfPlayer, tile, true);
            foreach (Pawn pawn in pawns) ReturnPawnToCaravan(caravan, pawn);

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

        private static void CollectRecoverable(IEnumerable<TargetSnapshot> snapshots, List<Pawn> pawns, List<Thing> items)
        {
            if (snapshots == null) return;

            foreach (TargetSnapshot snapshot in snapshots)
            {
                Thing thing = snapshot?.liveThing;
                if (thing == null || thing.Destroyed) continue;

                if (thing is Pawn pawn)
                {
                    if (!pawn.Dead) pawns.Add(pawn);
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

        private static void ReturnThing(Thing thing, Caravan caravan)
        {
            if (thing is Pawn pawn)
            {
                ReturnPawnToCaravan(caravan, pawn);
            }
            else
            {
                SettlementServicesWorldComponent.Current.ReleaseItemCustody(thing);
                CaravanInventoryUtility.GiveThing(caravan, thing);
            }
        }

        internal static void ReturnPawnToCaravan(Caravan caravan, Pawn pawn)
        {
            AddReturningPawnToCaravan(caravan, pawn);
            if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
            Find.WorldPawns.PassToWorld(pawn);
        }

        private static void AddReturningPawnToCaravan(Caravan caravan, Pawn pawn)
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

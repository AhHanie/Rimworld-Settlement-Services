using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Custody
{
    internal static class ServicePawnCaravanReturn
    {
        internal static bool TryReturnCompletedPawn(ServiceJobContext ctx)
        {
            ServiceJobRecord job = ctx.Job;
            if (!job.targetInCustody) return false;

            List<Pawn> pawns = job.Targets
                .Select(t => t?.liveThing as Pawn)
                .Where(p => p != null && !p.Destroyed && !p.Dead)
                .ToList();
            if (pawns.Count == 0) return false;

            Settlement settlement = ctx.ResolveSettlement();
            if (settlement == null) return false;

            PlanetTile tile = settlement.Tile;
            if (!tile.Valid || !tile.LayerDef.canFormCaravans) return false;

            List<Caravan> candidates = SnapshotWaitingCaravans(tile);
            Caravan receiver = ResolveOriginalRequester(job, candidates) ?? SelectDeterministicReceiver(candidates);

            bool createdCaravan = receiver == null;
            if (createdCaravan) receiver = CaravanMaker.MakeCaravan(Enumerable.Empty<Pawn>(), Faction.OfPlayer, tile, true);
            foreach (Pawn pawn in pawns) TargetCustodyService.ReturnPawnToCaravan(receiver, pawn);
            if (createdCaravan) receiver.Name = CaravanNameGenerator.GenerateCaravanName(receiver);

            job.targetInCustody = false;

            List<Caravan> mergeSources = SnapshotWaitingCaravans(tile).Where(c => c != receiver).ToList();
            MergeIntoReceiver(receiver, mergeSources, job);

            return true;
        }

        private static bool IsWaitingPlayerCaravan(Caravan c) =>
            c != null && !c.Destroyed && c.Spawned && c.IsPlayerControlled && !c.pather.Moving;

        private static List<Caravan> SnapshotWaitingCaravans(PlanetTile tile) =>
            Find.WorldObjects.Caravans.Where(c => IsWaitingPlayerCaravan(c) && c.Tile == tile).ToList();

        private static Caravan ResolveOriginalRequester(ServiceJobRecord job, List<Caravan> candidates) =>
            job.requesterCaravanId < 0 ? null : candidates.FirstOrDefault(c => c.ID == job.requesterCaravanId);

        private static Caravan SelectDeterministicReceiver(List<Caravan> candidates) =>
            candidates.Count == 0 ? null : candidates.OrderByDescending(c => c.PawnsListForReading.Count).ThenBy(c => c.ID).First();

        private static void MergeIntoReceiver(Caravan receiver, List<Caravan> sources, ServiceJobRecord job)
        {
            if (sources.Count == 0) return;

            bool odyssey = ModsConfig.OdysseyActive;
            int shuttleCount = (odyssey && receiver.Shuttle != null ? 1 : 0) + (odyssey ? sources.Count(c => c.Shuttle != null) : 0);

            List<Caravan> mergeable = sources;
            bool anySkipped = false;
            if (shuttleCount >= 2)
            {
                mergeable = sources.Where(c => c.Shuttle == null).ToList();
                anySkipped = mergeable.Count < sources.Count;
            }

            if (mergeable.Count > 0)
            {
                var merged = new List<Caravan> { receiver };
                foreach (Caravan source in mergeable)
                {
                    source.pawns.TryTransferAllToContainer(receiver.pawns);
                    merged.Add(source);
                    source.Destroy();
                }

                receiver.hasShuttleDirty = true;
                receiver.Notify_Merged(merged);
            }

            if (anySkipped) SettlementServiceNotifier.NotifyShuttleMergeSkipped(job);
        }
    }
}

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

            if (!TryReturnPawns(ctx, pawns, settlement.Tile, job.requesterCaravanId, $"Job {job.jobId}", () => SettlementServiceNotifier.NotifyShuttleMergeSkipped(job), out _))
                return false;

            job.targetInCustody = false;
            return true;
        }

        internal static bool TryReturnPawns(ServiceJobContext ctx, List<Pawn> pawns, PlanetTile tile, int preferredCaravanId, string logContext, System.Action onShuttleMergeSkipped, out Caravan receiverCaravan)
        {
            receiverCaravan = null;
            if (pawns.Count == 0) return false;
            if (!tile.Valid || !tile.LayerDef.canFormCaravans) return false;

            List<Caravan> candidates = SnapshotWaitingCaravans(tile);
            Caravan receiver = ResolvePreferredCaravan(preferredCaravanId, candidates) ?? SelectDeterministicReceiver(candidates);

            bool createdCaravan = receiver == null;
            if (createdCaravan) receiver = CaravanMaker.MakeCaravan(Enumerable.Empty<Pawn>(), Faction.OfPlayer, tile, true);

            bool allReturned = true;
            foreach (Pawn pawn in pawns)
            {
                if (TargetCustodyService.TryReturnPawnToCaravan(ctx, receiver, pawn, out Caravan updated)) receiver = updated;
                else allReturned = false;
            }

            if (!allReturned)
            {
                SupportLog.Error($"{logContext}: automatic return could not verify every pawn back into a caravan; leaving the pawns recoverable.");
                if (createdCaravan && receiver != null && !receiver.Destroyed && receiver.PawnsListForReading.Count == 0) receiver.Destroy();
                return false;
            }

            if (createdCaravan) receiver.Name = CaravanNameGenerator.GenerateCaravanName(receiver);

            List<Caravan> mergeSources = SnapshotWaitingCaravans(tile).Where(c => c != receiver).ToList();
            MergeIntoReceiver(receiver, mergeSources, onShuttleMergeSkipped);

            receiverCaravan = receiver;
            return true;
        }

        private static bool IsWaitingPlayerCaravan(Caravan c) =>
            c != null && !c.Destroyed && c.Spawned && c.IsPlayerControlled && !c.pather.Moving;

        private static List<Caravan> SnapshotWaitingCaravans(PlanetTile tile) =>
            Find.WorldObjects.Caravans.Where(c => IsWaitingPlayerCaravan(c) && c.Tile == tile).ToList();

        private static Caravan ResolvePreferredCaravan(int caravanId, List<Caravan> candidates) =>
            caravanId < 0 ? null : candidates.FirstOrDefault(c => c.ID == caravanId);

        private static Caravan SelectDeterministicReceiver(List<Caravan> candidates) =>
            candidates.Count == 0 ? null : candidates.OrderByDescending(c => c.PawnsListForReading.Count).ThenBy(c => c.ID).First();

        private static void MergeIntoReceiver(Caravan receiver, List<Caravan> sources, System.Action onShuttleMergeSkipped)
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

            if (anySkipped) onShuttleMergeSkipped?.Invoke();
        }
    }
}

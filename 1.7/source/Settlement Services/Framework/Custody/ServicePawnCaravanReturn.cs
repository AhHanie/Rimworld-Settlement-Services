using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework;
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
                .Where(TargetCustodyService.RequiresReturn)
                .ToList();
            if (pawns.Count == 0)
            {
                if (job.Targets.Any(t => !(t?.liveThing is Pawn) && TargetCustodyService.RequiresReturn(t?.liveThing))) return false;

                job.targetInCustody = false;
                return true;
            }

            Settlement settlement = ctx.ResolveSettlement();
            if (settlement == null) return false;

            if (settlement.Faction != null && settlement.Faction.HostileTo(Faction.OfPlayer)) return false;
            if (job.requestChannel != RequestChannel.InPerson) return false;

            if (!TryReturnPawns(ctx, pawns, settlement.Tile, job.requesterCaravanId, $"Job {job.jobId}", out Caravan receiver, createIfMissing: true))
                return false;

            // Item targets still need collection even when every pawn has returned.
            job.targetInCustody = job.Targets.Any(t => TargetCustodyService.RequiresReturn(t?.liveThing)
                && (!(t.liveThing is Pawn pawn) || !receiver.PawnsListForReading.Contains(pawn)));
            return true;
        }

        internal static bool TryReturnPawns(ServiceJobContext ctx, List<Pawn> pawns, PlanetTile tile, int preferredCaravanId, string logContext, out Caravan receiverCaravan, bool createIfMissing = false)
        {
            receiverCaravan = null;
            if (pawns.Count == 0) return false;
            if (!tile.Valid || !tile.LayerDef.canFormCaravans) return false;
            if (preferredCaravanId < 0) return false;

            Caravan receiver = Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == preferredCaravanId);
            bool created = false;
            if (receiver == null && createIfMissing)
            {
                ServiceJobRecord job = ctx.Job;
                if (job == null || !job.targetInCustody || job.requestChannel != RequestChannel.InPerson
                    || pawns.Any(p => !TargetCustodyService.RequiresReturn(p) || p.Spawned || p.holdingOwner != null
                        || !Find.WorldPawns.Contains(p) || !job.Targets.Any(t => ReferenceEquals(t?.liveThing, p)))
                    || !CaravanOwnershipRules.HasCapableOwner(pawns, Faction.OfPlayer)) return false;

                receiver = CaravanMaker.MakeCaravan(Enumerable.Empty<Pawn>(), Faction.OfPlayer, tile, true);
                if (receiver == null) return false;
                created = true;
            }
            if (receiver == null || receiver.Tile != tile || !CaravanOwnershipRules.IsEligibleReceiver(receiver, pawns))
            {
                if (created && receiver != null && receiver.PawnsListForReading.Count == 0) receiver.Destroy();
                return false;
            }

            bool allReturned = true;
            foreach (Pawn pawn in pawns)
            {
                if (pawn.holdingOwner != null && !receiver.PawnsListForReading.Contains(pawn))
                {
                    allReturned = false;
                    SupportLog.Error($"{logContext}: pawn {pawn.thingIDNumber} is held by another owner; automatic return was skipped.");
                    continue;
                }

                if (TargetCustodyService.TryReturnPawnToCaravan(ctx, receiver, pawn, out Caravan updated)
                    && updated != null && updated.PawnsListForReading.Contains(pawn))
                {
                    receiver = updated;
                    if (ctx.Job != null && receiver.ID != ctx.Job.requesterCaravanId)
                        ctx.Domain.UpdateJobRequesterCaravan(ctx.Job.jobId, receiver);
                }
                else
                {
                    allReturned = false;
                    SupportLog.Error($"{logContext}: pawn {pawn.thingIDNumber} could not be verified in the receiving caravan.");
                }
            }

            if (created && receiver != null && !receiver.Destroyed && receiver.PawnsListForReading.Count > 0)
            {
                receiver.Name = CaravanNameGenerator.GenerateCaravanName(receiver);
                SettlementServiceNotifier.NotifyServiceCaravanCreated(receiver);
            }

            if (!allReturned)
            {
                if (created && receiver != null && !receiver.Destroyed && receiver.PawnsListForReading.Count == 0) receiver.Destroy();
                return false;
            }

            receiverCaravan = receiver;
            return true;
        }

        internal static bool IsWaitingPlayerCaravan(Caravan c) =>
            c != null && !c.Destroyed && c.Spawned && c.IsPlayerControlled && !c.pather.Moving;
    }
}

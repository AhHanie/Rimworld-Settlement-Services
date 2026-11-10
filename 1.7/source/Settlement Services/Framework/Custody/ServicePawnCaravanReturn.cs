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

            if (!TryReturnPawns(ctx, pawns, settlement.Tile, job.requesterCaravanId, $"Job {job.jobId}", out _))
                return false;

            job.targetInCustody = false;
            return true;
        }

        internal static bool TryReturnPawns(ServiceJobContext ctx, List<Pawn> pawns, PlanetTile tile, int preferredCaravanId, string logContext, out Caravan receiverCaravan)
        {
            receiverCaravan = null;
            if (pawns.Count == 0) return false;
            if (!tile.Valid || !tile.LayerDef.canFormCaravans) return false;
            if (preferredCaravanId < 0) return false;

            Caravan receiver = Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == preferredCaravanId);
            if (receiver == null || receiver.Tile != tile || !CaravanOwnershipRules.IsEligibleReceiver(receiver, pawns)) return false;

            bool allReturned = true;
            foreach (Pawn pawn in pawns)
            {
                if (TargetCustodyService.TryReturnPawnToCaravan(ctx, receiver, pawn, out Caravan updated)) receiver = updated;
                else allReturned = false;
            }

            if (!allReturned)
            {
                SupportLog.Error($"{logContext}: automatic return could not verify every pawn back into the original caravan; leaving the pawns recoverable.");
                return false;
            }

            receiverCaravan = receiver;
            return true;
        }

        internal static bool IsWaitingPlayerCaravan(Caravan c) =>
            c != null && !c.Destroyed && c.Spawned && c.IsPlayerControlled && !c.pather.Moving;
    }
}

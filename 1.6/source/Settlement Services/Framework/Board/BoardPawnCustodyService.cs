using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Custody;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Board
{
    internal static class BoardPawnCustodyService
    {
        internal static bool TryDispatch(SettlementServicesWorldComponent domain, Caravan caravan, IReadOnlyList<Pawn> pawns, out string errorKey)
        {
            errorKey = null;
            var ctx = new ServiceJobContext(domain, null);
            PlanetTile tile = caravan.Tile;
            Caravan current = caravan;
            var touched = new List<Pawn>();

            foreach (Pawn pawn in pawns)
            {
                touched.Add(pawn);
                if (!TargetCustodyService.TryDetachPawn(ctx, ref current, pawn, out errorKey))
                {
                    Rollback(ctx, ref current, touched, tile);
                    return false;
                }

                if (pawn.Faction != Faction.OfPlayer)
                {
                    SupportLog.Error($"Board dispatch: {pawn.LabelShort} changed faction while entering custody; restoring player ownership.");
                    pawn.SetFaction(Faction.OfPlayer);
                }
            }

            if (current != null && !current.Destroyed)
            {
                if (current.PawnsListForReading.Count == 0) current.Destroy();
                else OffloadInventory(pawns, current);
            }

            return true;
        }

        internal static bool TryReturn(SettlementServicesWorldComponent domain, BoardJobRecord job, out Caravan receiver)
        {
            receiver = null;

            List<Pawn> living = job.assignedPawns.Where(p => p != null && !p.Destroyed && !p.Dead).ToList();
            if (living.Count == 0)
            {
                job.pawnsReturned = true;
                return true;
            }

            Settlement settlement = WorldObjectLookup.ResolveSettlement(job.settlementWorldObjectId);
            PlanetTile tile = settlement != null ? settlement.Tile : job.settlementTile;

            if (!tile.Valid || !tile.LayerDef.canFormCaravans)
            {
                foreach (Pawn pawn in living)
                {
                    domain.QueueHomeDelivery(new TargetSnapshot
                    {
                        kind = TargetKind.Pawn,
                        liveThing = pawn,
                        snapshotLabel = pawn.LabelShort,
                    });
                }
                job.pawnsReturned = true;
                return true;
            }

            var ctx = new ServiceJobContext(domain, null);
            if (!ServicePawnCaravanReturn.TryReturnPawns(ctx, living, tile, job.sourceCaravanId, $"Board job {job.boardJobId}", null, out receiver))
                return false;

            job.pawnsReturned = true;
            return true;
        }

        internal static Caravan FindHoldingCaravan(BoardJobRecord job)
        {
            foreach (Pawn pawn in job.assignedPawns)
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead) continue;
                Caravan caravan = pawn.GetCaravan();
                if (caravan != null && !caravan.Destroyed) return caravan;
            }
            return null;
        }

        private static void Rollback(ServiceJobContext ctx, ref Caravan current, List<Pawn> touched, PlanetTile tile)
        {
            foreach (Pawn pawn in touched)
            {
                if (pawn == null || pawn.Destroyed) continue;

                if (current == null || current.Destroyed)
                {
                    if (!tile.Valid || !tile.LayerDef.canFormCaravans) continue;
                    current = CaravanMaker.MakeCaravan(Enumerable.Empty<Pawn>(), Faction.OfPlayer, tile, true);
                    current.Name = CaravanNameGenerator.GenerateCaravanName(current);
                }

                if (TargetCustodyService.TryReturnPawnToCaravan(ctx, current, pawn, out Caravan updated)) current = updated;
            }
        }

        private static void OffloadInventory(IReadOnlyList<Pawn> departing, Caravan remaining)
        {
            List<Pawn> receivers = remaining.PawnsListForReading.Where(p => !departing.Contains(p)).ToList();
            if (receivers.Count == 0) return;

            foreach (Pawn pawn in departing)
            {
                if (pawn.inventory?.innerContainer == null || pawn.inventory.innerContainer.Count == 0) continue;
                CaravanInventoryUtility.MoveAllInventoryToSomeoneElse(pawn, receivers);
            }
        }
    }
}

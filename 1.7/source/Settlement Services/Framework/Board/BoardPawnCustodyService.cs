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

            if (!CaravanCustodyCargoTransfer.TryPrepare(caravan, pawns.ToList(), out CaravanCustodyCargoTransfer cargo, out errorKey))
                return false;

            foreach (Pawn pawn in pawns)
            {
                touched.Add(pawn);
                if (!TargetCustodyService.TryDetachPawn(ctx, ref current, pawn, out errorKey))
                {
                    Rollback(ctx, ref current, touched, tile);
                    cargo.Rollback();
                    return false;
                }

                if (pawn.Faction != Faction.OfPlayer)
                {
                    SupportLog.Error($"Board dispatch: {pawn.LabelShort} changed faction while entering custody; restoring player ownership.");
                    pawn.SetFaction(Faction.OfPlayer);
                }
            }

            if (current != null && !current.Destroyed && current.PawnsListForReading.Count == 0) current.Destroy();

            cargo.Commit();
            return true;
        }

        internal static bool TryCollect(SettlementServicesWorldComponent domain, BoardJobRecord job, Caravan caravan, out string errorKey)
        {
            errorKey = null;
            if (job.status != BoardJobStatus.ReturnPending || job.pawnsReturned) return false;

            Settlement settlement = WorldObjectLookup.ResolveSettlement(job.settlementWorldObjectId);
            if (settlement == null) return false;

            List<Pawn> living = job.assignedPawns.Where(p => p != null && !p.Destroyed && !p.Dead).ToList();
            errorKey = GetCollectBlockKey(caravan, settlement.Tile, living);
            if (errorKey != null) return false;

            var ctx = new ServiceJobContext(domain, null);
            bool allReturned = true;
            Caravan receiver = caravan;
            foreach (Pawn pawn in living)
            {
                if (TargetCustodyService.TryReturnPawnToCaravan(ctx, receiver, pawn, out Caravan updated)) receiver = updated;
                else allReturned = false;
            }

            if (!allReturned)
            {
                errorKey = "SettlementServices.Error.CollectIncomplete";
                return false;
            }

            job.pawnsReturned = true;
            return true;
        }

        internal static string GetCollectBlockKey(Caravan caravan, PlanetTile settlementTile, List<Pawn> held)
        {
            if (caravan == null || caravan.Destroyed || !caravan.Spawned || !caravan.IsPlayerControlled) return "SettlementServices.Error.CollectCaravanIneligible";
            if (caravan.pather != null && caravan.pather.Moving) return "SettlementServices.Error.CollectCaravanMoving";
            if (caravan.Tile != settlementTile) return "SettlementServices.Error.CollectCaravanWrongTile";
            return CaravanOwnershipRules.GroupWouldHaveCapableOwner(caravan, held) ? null : CaravanOwnershipRules.NoCapableOwnerKey;
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

            if (settlement == null || !tile.Valid || !tile.LayerDef.canFormCaravans)
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
            if (!ServicePawnCaravanReturn.TryReturnPawns(ctx, living, tile, job.sourceCaravanId, $"Board job {job.boardJobId}", out receiver))
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
    }
}

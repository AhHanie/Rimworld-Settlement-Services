using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Framework.Custody
{
    internal sealed class CaravanCustodyCargoTransfer
    {
        internal const string TransferFailedKey = "SettlementServices.Error.CargoTransferFailed";

        private struct Move
        {
            public Thing thing;
            public Pawn from;
            public Pawn to;
        }

        private readonly Caravan caravan;
        private readonly List<Move> ledger = new List<Move>();
        private bool finished;

        private CaravanCustodyCargoTransfer(Caravan caravan)
        {
            this.caravan = caravan;
        }

        internal int MovedCount => ledger.Count;

        internal static CaravanCustodyCargoTransfer Empty => new CaravanCustodyCargoTransfer(null) { finished = true };

        internal static bool TryPrepare(Caravan caravan, IReadOnlyCollection<Pawn> departing, out CaravanCustodyCargoTransfer transfer, out string errorKey)
        {
            transfer = new CaravanCustodyCargoTransfer(caravan);
            errorKey = CaravanOwnershipRules.GetDepartureBlockKey(caravan, departing);
            if (errorKey != null) return false;

            if (caravan == null || caravan.Destroyed || departing == null || departing.Count == 0) return true;

            List<Pawn> receivers = caravan.PawnsListForReading.Where(p => !departing.Contains(p)).ToList();
            if (receivers.Count == 0) return true;

            var planned = new List<(Pawn from, Thing thing)>();
            foreach (Pawn donor in departing)
            {
                ThingOwner<Thing> inventory = donor?.inventory?.innerContainer;
                if (inventory == null || inventory.Count == 0) continue;

                foreach (Thing thing in inventory.ToList())
                {
                    if (thing == null || thing.Destroyed) continue;
                    if (CustodyCargoPolicy.ShouldRetainWithPawn(thing, donor)) continue;
                    planned.Add((donor, thing));
                }
            }

            foreach ((Pawn from, Thing thing) in planned)
            {
                Pawn receiver = CaravanInventoryUtility.FindPawnToMoveInventoryTo(thing, receivers, null, from);
                ThingOwner target = receiver?.inventory?.innerContainer;
                int count = thing.stackCount;

                if (target == null
                    || from.inventory.innerContainer.TryTransferToContainer(thing, target, count, out Thing moved, canMergeWithExistingStacks: false) != count
                    || moved != thing)
                {
                    SupportLog.Error($"Custody cargo transfer could not move {thing.LabelCap} from {from.LabelShortCap} to a remaining caravan member; restoring all moved cargo.");
                    transfer.Rollback();
                    errorKey = TransferFailedKey;
                    return false;
                }

                transfer.ledger.Add(new Move { thing = thing, from = from, to = receiver });
            }

            caravan.RecacheInventory();
            return true;
        }

        internal void Commit()
        {
            if (finished) return;
            finished = true;

            if (caravan == null || caravan.Destroyed || ledger.Count == 0) return;

            caravan.RecacheInventory();

            Pawn overburdened = ledger.Select(m => m.to).Distinct().FirstOrDefault(p => p != null && !p.Destroyed && MassUtility.IsOverEncumbered(p));
            if (overburdened != null)
                Messages.Message("SettlementServices.Message.CargoOverburdened".Translate(overburdened.LabelShortCap), caravan, MessageTypeDefOf.CautionInput, historical: false);
        }

        internal void Rollback()
        {
            if (finished && ledger.Count == 0) return;
            finished = true;

            for (int i = ledger.Count - 1; i >= 0; i--)
            {
                Move move = ledger[i];
                Thing thing = move.thing;
                if (thing == null || thing.Destroyed) continue;

                ThingOwner current = thing.holdingOwner;
                ThingOwner receiverInventory = move.to?.inventory?.innerContainer;
                ThingOwner donorInventory = move.from?.inventory?.innerContainer;
                if (current == null || current != receiverInventory || donorInventory == null) continue;

                int count = thing.stackCount;
                if (current.TryTransferToContainer(thing, donorInventory, count, out _, canMergeWithExistingStacks: false) != count)
                    SupportLog.Error($"Custody cargo rollback could not return {thing.LabelCap} to {move.from.LabelShortCap}; it remains with {move.to.LabelShortCap}.");
            }

            ledger.Clear();
            if (caravan != null && !caravan.Destroyed) caravan.RecacheInventory();
        }
    }
}

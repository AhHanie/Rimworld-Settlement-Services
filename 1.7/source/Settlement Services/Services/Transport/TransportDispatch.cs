using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Custody;
using Verse;

namespace Settlement_Services.Services.Transport
{
    internal static class TransportDispatch
    {
        private readonly record struct Transfer(Thing Item, ThingOwner Source, int Count);

        internal static bool TryBook(TransportRequest request, TransportQuote displayed, out string error)
        {
            error = TransportBooking.BuildQuote(request, out TransportQuote quote, out _);
            if (error != null) return false;
            if (!quote.Matches(displayed)) { error = TransportBooking.Changed; return false; }

            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            Caravan caravan = request.Caravan;
            var payload = new ActiveTransporterInfo();
            var escrow = new ActiveTransporterInfo();
            var ledger = new List<Transfer>();
            TransportContractRecord contract = null;
            SettlementTransportFlight flight = null;
            try
            {
                contract = domain.CreateTransportContract(request, quote);
                flight = (SettlementTransportFlight)WorldObjectMaker.MakeWorldObject(quote.Specs.Flight);
                flight.contractId = contract.id;
                flight.Tile = contract.origin;
                flight.destinationTile = contract.tile;
                flight.arrivalAction = new TransportersArrivalAction_SettlementTransport(contract.id);
                flight.SetFaction(Faction.OfPlayer);

                foreach (TransportStack stack in quote.Payment) Stage(stack, escrow.innerContainer, ledger);
                if (escrow.innerContainer.Sum(t => t.stackCount) != quote.Fare.Total)
                    throw new InvalidOperationException("The escrow does not match the accepted fare.");
                foreach (TransportStack stack in quote.Cargo) Stage(stack, payload.innerContainer, ledger);
                foreach (TransportRemainder remainder in quote.Remainders)
                    Stage(remainder.Stack, remainder.Receiver ?? escrow.innerContainer, ledger);
                foreach (TransportPassenger passenger in quote.Passengers)
                    Stage(new TransportStack(passenger.Pawn, passenger.Owner, 1), payload.innerContainer, ledger);

                float stagedMass = CollectionsMassCalculator.MassUsage(payload.innerContainer, IgnorePawnsInventoryMode.DontIgnore, includePawnsMass: true);
                if (!float.IsFinite(stagedMass) || stagedMass > quote.Specs.Capacity + TransportRoute.MassTolerance
                    || Math.Abs(stagedMass - quote.Mass) > TransportRoute.MassTolerance)
                    throw new InvalidOperationException("Staged transport mass differs from the accepted manifest.");

                bool empty = caravan.pawns.Count == 0;
                if (!empty && !CaravanOwnershipRules.CaravanHasCapableOwner(caravan))
                    throw new InvalidOperationException("No capable owner remains in the source caravan.");
                if (!empty && quote.Remainders.Any(r => r.IsAbandoned))
                    throw new InvalidOperationException("The caravan must be empty before abandoning its remaining cargo.");

                contract.flightId = flight.ID;
                Find.WorldObjects.Add(flight);
                flight.AddTransporter(payload, justLeftTheMap: false);
                if (!flight.Spawned || domain.FindTransportContract(contract.id) != contract || !flight.Owns(payload)
                    || payload.innerContainer.Count != quote.Passengers.Count + quote.Cargo.Count
                    || quote.Passengers.Any(p => !flight.ContainsPawn(p.Pawn) || !p.Pawn.IsWorldPawn()))
                    throw new InvalidOperationException("The provisional flight does not match the accepted booking.");

                // Hold the fare and abandoned cargo until the payload is safely in flight.
                escrow.parent = contract;
                contract.dispatchEscrow = escrow;
                contract.payment = TransportPaymentState.Paid;
                contract.ChangePhase(TransportPhase.InFlight);
            }
            catch (Exception ex)
            {
                SupportLog.Error($"Could not dispatch settlement transport: {ex}");
                RollBack(domain, contract, flight, payload, escrow, ledger, caravan);
                error = TransportBooking.TransferError;
                return false;
            }

            try
            {
                if (caravan.pawns.Count == 0) caravan.Destroy();
                else caravan.RecacheInventory();
                ClearDispatchEscrow(contract);
            }
            catch (Exception ex)
            {
                SupportLog.Error($"Transport {contract.id} launched but post-dispatch cleanup was interrupted: {ex}");
            }
            Messages.Message((TransportBooking.Key + "Dispatched").Translate(flight.LabelCap, quote.Fare.Total), flight, MessageTypeDefOf.TaskCompletion);
            return true;
        }

        internal static void ClearDispatchEscrow(TransportContractRecord contract)
        {
            if (contract.dispatchEscrow == null || contract.payment != TransportPaymentState.Paid) return;
            contract.dispatchEscrow.innerContainer.ClearAndDestroyContents();
            contract.dispatchEscrow = null;
        }

        private static void Stage(TransportStack stack, ThingOwner destination, List<Transfer> ledger)
        {
            Thing item = stack.Item;
            if (item.holdingOwner != stack.Owner || !stack.Owner.Contains(item) || item.stackCount < stack.Count)
                throw new InvalidOperationException($"Transport stack changed before staging: {item}");

            // If a contents-changed callback throws, the transfer can already have changed ownership.
            var before = destination.ToHashSet();
            Thing actual = null;
            int moved;
            try
            {
                moved = stack.Owner.TryTransferToContainer(item, destination, stack.Count, out actual, false);
            }
            catch
            {
                foreach (Thing added in destination.Where(t => !before.Contains(t)))
                    ledger.Add(new Transfer(added, stack.Owner, added.stackCount));
                if (item.holdingOwner == null && !item.Spawned && !item.Destroyed)
                    ledger.Add(new Transfer(item, stack.Owner, item.stackCount));
                throw;
            }
            if (actual != null && destination.Contains(actual)) ledger.Add(new Transfer(actual, stack.Owner, moved));
            if (moved != stack.Count || actual == null || actual.holdingOwner != destination || actual.stackCount != stack.Count)
                throw new InvalidOperationException($"Could not stage {stack.Count} of {item} for settlement transport.");
        }

        internal static bool Move(Thing thing, ThingOwner receiver)
        {
            ThingOwner source = thing.holdingOwner;
            if (receiver == null || source == null || !receiver.CanAcceptAnyOf(thing, false)) return false;
            int count = thing.stackCount;
            try
            {
                return source.TryTransferToContainer(thing, receiver, count, out Thing moved, false) == count
                    && moved == thing && thing.holdingOwner == receiver && receiver.Contains(thing);
            }
            catch (Exception ex)
            {
                SupportLog.Warning($"A contents change callback interrupted transport handling for {thing}: {ex}");
                if (thing.holdingOwner == receiver && receiver.Contains(thing) && thing.stackCount == count) return true;
                if (thing.holdingOwner == null && !thing.Spawned && !thing.Destroyed)
                    source.TryAdd(thing, canMergeWithExistingStacks: false);
                return false;
            }
        }

        private static void RollBack(SettlementServicesWorldComponent domain, TransportContractRecord contract,
            SettlementTransportFlight flight, ActiveTransporterInfo payload, ActiveTransporterInfo escrow,
            List<Transfer> ledger, Caravan caravan)
        {
            var recovery = new ActiveTransporterInfo();
            for (int i = ledger.Count - 1; i >= 0; i--)
            {
                Transfer transfer = ledger[i];
                Thing item = transfer.Item;
                if (item.Destroyed || item.holdingOwner == transfer.Source) continue;
                try
                {
                    if (item.holdingOwner == null && !item.Spawned && transfer.Source.TryAdd(item, canMergeWithExistingStacks: false)) continue;
                    if (Move(item, transfer.Source)) continue;
                }
                catch (Exception ex)
                {
                    SupportLog.Error($"Could not restore {transfer.Count} of {item} to its original holder: {ex}");
                    if (item.holdingOwner == transfer.Source) continue;
                }
                try
                {
                    if (!Move(item, recovery.innerContainer) && item.holdingOwner == null && !item.Spawned)
                        recovery.innerContainer.TryAdd(item, canMergeWithExistingStacks: false);
                }
                catch (Exception ex)
                {
                    SupportLog.Error($"Could not move {item} into transport rollback custody: {ex}");
                }
            }

            if (contract != null)
            {
                contract.dispatchEscrow = null;
                contract.payment = TransportPaymentState.Unpaid;
                contract.phase = TransportPhase.Preparing;
                foreach (ActiveTransporterInfo holder in new[] { payload, escrow, recovery })
                    if (holder.innerContainer.Count > 0) contract.Adopt(holder);
                if (contract.Payload.Any()) contract.ChangePhase(TransportPhase.Recovery);
            }
            if (flight?.Spawned == true) flight.Destroy();
            if (contract != null && !contract.Payload.Any()) domain.RemoveTransportContract(contract);
            if (caravan.Spawned && caravan.pawns.Count > 0) caravan.RecacheInventory();
        }
    }
}

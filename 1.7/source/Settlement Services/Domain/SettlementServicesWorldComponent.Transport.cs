using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain.Records;
using Settlement_Services.Services.Transport;
using Verse;

namespace Settlement_Services.Domain
{
    public partial class SettlementServicesWorldComponent
    {
        private int nextTransportId = 1;
        private List<TransportContractRecord> transportContracts = new List<TransportContractRecord>();

        internal TransportContractRecord FindTransportContract(int id) => transportContracts?.Find(t => t.id == id);

        private void ExposeTransportContracts()
        {
            Scribe_Values.Look(ref nextTransportId, "nextTransportId", 1);
            Scribe_Collections.Look(ref transportContracts, "transportContracts", LookMode.Deep);
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            transportContracts ??= new List<TransportContractRecord>();
            transportContracts.RemoveAll(c => c == null);
            foreach (TransportContractRecord contract in transportContracts)
                if (contract.id >= nextTransportId) nextTransportId = Math.Min(contract.id, int.MaxValue - 1) + 1;
        }

        private TransportContractRecord NewTransportContract()
        {
            if (nextTransportId < 1 || nextTransportId == int.MaxValue) throw new InvalidOperationException("Transport contract IDs exhausted.");
            var contract = new TransportContractRecord { id = nextTransportId++ };
            transportContracts.Add(contract);
            return contract;
        }

        internal TransportContractRecord CreateTransportContract(TransportRequest request, TransportQuote quote)
        {
            TransportContractRecord contract = NewTransportContract();
            contract.providerId = request.Provider.ID;
            contract.providerFaction = request.Provider.Faction;
            contract.sourceId = request.Caravan.ID;
            contract.origin = request.Provider.Tile;
            contract.mode = quote.Mode;
            contract.tile = quote.Destination.Tile;
            contract.mapParentId = quote.Destination.MapParentId;
            contract.bookedCell = quote.Destination.Cell;
            contract.fare = quote.Fare.Total;
            contract.baseFare = quote.Fare.BaseFare;
            contract.materialFare = quote.Fare.MaterialFare;
            contract.wealthFare = quote.Fare.WealthFare;
            contract.fuelFare = quote.Fare.FuelFare;
            contract.fuel = quote.Fare.Fuel;
            contract.fuelPrice = quote.Fare.FuelPrice;
            contract.layerFactor = quote.Fare.LayerFactor;
            contract.distance = quote.Fare.Distance;
            contract.range = quote.Fare.Range;
            contract.ChangePhase(TransportPhase.Preparing);
            return contract;
        }

        internal void RemoveTransportContract(TransportContractRecord contract) => transportContracts.Remove(contract);

        internal void CaptureTransportArrival(int id, IEnumerable<ActiveTransporterInfo> holders, PlanetTile actualTile, bool interrupted)
        {
            TransportContractRecord contract = FindTransportContract(id);
            if (contract == null)
            {
                contract = NewTransportContract();
                contract.tile = actualTile;
                interrupted = true;
            }
            contract.ChangePhase(TransportPhase.Recovery);
            try
            {
                foreach (ActiveTransporterInfo holder in holders)
                    if (holder?.innerContainer != null) contract.Adopt(holder);
                if (!interrupted && actualTile == contract.tile && contract.Payload.Any())
                    TransportArrival.Start(contract);
            }
            catch (Exception ex)
            {
                SupportLog.Error($"Transport {contract.id} could not start arrival: {ex}");
                contract.ChangePhase(TransportPhase.Recovery);
            }
        }

        private void TickTransportContracts()
        {
            for (int i = transportContracts.Count - 1; i >= 0; i--)
            {
                TransportContractRecord contract = transportContracts[i];
                try
                {
                    TransportDispatch.ClearDispatchEscrow(contract);
                    if (TransportArrival.Tick(this, contract)) transportContracts.RemoveAt(i);
                }
                catch (Exception ex)
                {
                    SupportLog.Error($"Transport {contract.id} could not advance {contract.phase}: {ex}");
                    if (contract.dispatchEscrow == null) contract.ChangePhase(TransportPhase.Recovery);
                }
            }
        }

        internal bool QueueTransportHomeDelivery(TransportContractRecord contract, Thing item)
        {
            var snapshot = new TargetSnapshot { kind = item is Pawn ? TargetKind.Pawn : TargetKind.Item,
                liveThing = item, snapshotLabel = item.LabelCap, snapshotDefName = item.def.defName, transportContractId = contract.id };
            if (!TransportDispatch.Move(item, itemCustody)) return false;
            QueueHomeDelivery(snapshot);
            return true;
        }

        internal bool HasQueuedTransportDelivery(int id) => pendingHomeDeliveries.Any(d => d.transportContractId == id);

        private void FlushTransportHomeSilverRefunds()
        {
            if (Find.TickManager.TicksGame % 60 != 0) return;
            foreach (TransportContractRecord contract in transportContracts)
            {
                // Keep the unplaced count on the contract so blocked cells and partial placement can be retried.
                if (contract.payment == TransportPaymentState.RefundQueued && contract.refundRemaining > 0)
                    TransportArrival.TryDeliverQueuedRefund(contract);
            }
        }

        internal bool HasTransportHeldFreeColonist() => transportContracts.Any(c => c.Payload.OfType<Pawn>().Any(IsFreeColonist))
            || pendingHomeDeliveries.Any(d => d.transportContractId > 0 && d.liveThing is Pawn p && itemCustody.Contains(p) && IsFreeColonist(p));

        private static bool IsFreeColonist(Pawn pawn) => !pawn.Destroyed && !pawn.Dead && pawn.IsColonist && pawn.HostFaction == null;
    }
}

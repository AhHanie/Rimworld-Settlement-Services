using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Services.Transport;
using Verse;

namespace Settlement_Services.Domain.Records
{
    internal enum TransportPhase { Preparing, InFlight, ArrivalAnimation, Unloading, DepartureAnimation, Recovery, Delivered }
    internal enum TransportPaymentState { Unpaid, Paid, Refunded, RefundQueued }

    internal sealed class TransportContractRecord : IExposable, IThingHolder
    {
        internal int id;
        internal int providerId = -1;
        internal Faction providerFaction;
        internal int sourceId = -1;
        internal PlanetTile origin = PlanetTile.Invalid;
        internal TransportMode mode;
        internal PlanetTile tile = PlanetTile.Invalid;
        internal int mapParentId = -1;
        internal IntVec3 bookedCell = IntVec3.Invalid;
        internal IntVec3 landingCell = IntVec3.Invalid;
        internal int fare;
        internal int baseFare;
        internal int materialFare;
        internal int wealthFare;
        internal int fuelFare;
        internal float fuel;
        internal float fuelPrice;
        internal float layerFactor;
        internal int distance;
        internal int range;
        internal TransportPaymentState payment;
        internal int refundRemaining = -1;
        internal TransportPhase phase;
        internal int phaseTick;
        internal int flightId = -1;
        internal int markerId = -1;
        internal int receiverId = -1;
        internal Thing localVehicle;
        internal Thing departureVisual;
        internal TransportShip ship;
        internal bool worldArrival;
        internal bool deliveredAny;
        internal bool recoveryReported;
        internal bool arrivalReported;
        internal string recoveryManifest;
        internal int landingAttempts;
        internal ActiveTransporterInfo dispatchEscrow;

        private List<ActiveTransporterInfo> arrivalHolders = new List<ActiveTransporterInfo>();

        internal TransportDestination Destination => new TransportDestination(tile, mapParentId, bookedCell);
        internal IEnumerable<Thing> Payload => arrivalHolders.SelectMany(h => h.innerContainer);
        public IThingHolder ParentHolder => SettlementServicesWorldComponent.Current;
        public ThingOwner GetDirectlyHeldThings() => null;
        public void GetChildHolders(List<IThingHolder> children)
        {
            children.AddRange(arrivalHolders);
            if (dispatchEscrow != null) children.Add(dispatchEscrow);
        }

        internal void Adopt(ActiveTransporterInfo holder)
        {
            if (!arrivalHolders.Contains(holder)) arrivalHolders.Add(holder);
            holder.parent = this;
            holder.savePawnsWithReferenceMode = false;
            foreach (Pawn pawn in holder.innerContainer.OfType<Pawn>())
                if (pawn.IsWorldPawn()) Find.WorldPawns.RemovePawn(pawn);
        }

        internal void ChangePhase(TransportPhase next)
        {
            phase = next;
            phaseTick = Find.TickManager.TicksGame;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref providerId, "providerId", -1);
            Scribe_References.Look(ref providerFaction, "providerFaction");
            Scribe_Values.Look(ref sourceId, "sourceId", -1);
            Scribe_Values.Look(ref origin, "origin", PlanetTile.Invalid);
            Scribe_Values.Look(ref mode, "mode");
            Scribe_Values.Look(ref tile, "tile", PlanetTile.Invalid);
            Scribe_Values.Look(ref mapParentId, "mapParentId", -1);
            Scribe_Values.Look(ref bookedCell, "bookedCell", IntVec3.Invalid);
            Scribe_Values.Look(ref landingCell, "landingCell", IntVec3.Invalid);
            Scribe_Values.Look(ref fare, "fare");
            Scribe_Values.Look(ref baseFare, "baseFare");
            Scribe_Values.Look(ref materialFare, "materialFare");
            Scribe_Values.Look(ref wealthFare, "wealthFare");
            Scribe_Values.Look(ref fuelFare, "fuelFare");
            Scribe_Values.Look(ref fuel, "fuel");
            Scribe_Values.Look(ref fuelPrice, "fuelPrice");
            Scribe_Values.Look(ref layerFactor, "layerFactor");
            Scribe_Values.Look(ref distance, "distance");
            Scribe_Values.Look(ref range, "range");
            Scribe_Values.Look(ref payment, "payment");
            Scribe_Values.Look(ref refundRemaining, "refundRemaining", -1);
            Scribe_Values.Look(ref phase, "phase");
            Scribe_Values.Look(ref phaseTick, "phaseTick");
            Scribe_Values.Look(ref flightId, "flightId", -1);
            Scribe_Values.Look(ref markerId, "markerId", -1);
            Scribe_Values.Look(ref receiverId, "receiverId", -1);
            Scribe_Values.Look(ref worldArrival, "worldArrival");
            Scribe_Values.Look(ref deliveredAny, "deliveredAny");
            Scribe_Values.Look(ref recoveryReported, "recoveryReported");
            Scribe_Values.Look(ref arrivalReported, "arrivalReported");
            Scribe_Values.Look(ref recoveryManifest, "recoveryManifest");
            Scribe_Values.Look(ref landingAttempts, "landingAttempts");
            Scribe_References.Look(ref localVehicle, "localVehicle");
            Scribe_References.Look(ref departureVisual, "departureVisual");
            Scribe_References.Look(ref ship, "ship");
            Scribe_Deep.Look(ref dispatchEscrow, "fareEscrow");
            Scribe_Collections.Look(ref arrivalHolders, "arrivalHolders", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                arrivalHolders ??= new List<ActiveTransporterInfo>();
                arrivalHolders.RemoveAll(h => h?.innerContainer == null);
                foreach (ActiveTransporterInfo holder in arrivalHolders) holder.parent = this;
                if (dispatchEscrow != null) dispatchEscrow.parent = this;
            }
        }
    }
}

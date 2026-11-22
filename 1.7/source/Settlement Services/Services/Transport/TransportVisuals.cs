using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Settlement_Services.Services.Transport
{
    public sealed class SettlementTransportFlight : TravellingTransporters
    {
        internal int contractId;

        internal bool Owns(ActiveTransporterInfo payload)
        {
            var holders = new List<IThingHolder>();
            GetChildHolders(holders);
            return payload.parent == this && holders.Contains(payload);
        }

        public override void PostRemove()
        {
            try
            {
                var domain = SettlementServicesWorldComponent.Current;
                TransportContractRecord contract = domain?.FindTransportContract(contractId);
                if (contract != null && contract.phase != TransportPhase.InFlight) return;
                var holders = new List<IThingHolder>();
                GetChildHolders(holders);
                if (holders.Count > 0)
                    domain?.CaptureTransportArrival(contractId, holders.OfType<ActiveTransporterInfo>(), destinationTile, interrupted: true);
            }
            finally { base.PostRemove(); }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref contractId, "settlementTransportId");
        }
    }

    public sealed class TransportersArrivalAction_SettlementTransport : TransportersArrivalAction
    {
        private int contractId;
        public override bool GeneratesMap => false;
        public TransportersArrivalAction_SettlementTransport() { }
        internal TransportersArrivalAction_SettlementTransport(int contractId) => this.contractId = contractId;

        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            SettlementServicesWorldComponent.Current.CaptureTransportArrival(contractId, transporters, tile, interrupted: false);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref contractId, "contractId");
        }
    }

    public sealed class SettlementTransportMarker : WorldObject
    {
        internal int contractId;
        private readonly MaterialPropertyBlock tint = new MaterialPropertyBlock();

        public override void Draw()
        {
            TransportContractRecord contract = SettlementServicesWorldComponent.Current?.FindTransportContract(contractId);
            if (contract == null || Material == null) return;

            float elapsed = Find.TickManager.TicksGame - contract.phaseTick;
            float offset = 0f;
            float alpha = 1f;
            float altitude = DrawAltitude + 0.02f;
            if (contract.phase == TransportPhase.ArrivalAnimation)
            {
                float approach = 1f - Mathf.Clamp01(elapsed / TransportArrival.ApproachTicks);
                offset = -approach;
                altitude += approach * 0.13f;
            }
            else if (contract.phase == TransportPhase.DepartureAnimation)
            {
                float departure = Mathf.Clamp01(elapsed / TransportArrival.DepartureTicks);
                offset = departure;
                alpha = 1f - departure;
                altitude += departure * 0.13f;
            }
            else if (contract.phase == TransportPhase.Unloading)
            {
                altitude += 0.025f * (1f - Mathf.Clamp01(elapsed / 12f));
            }

            Vector3 center = DrawPos;
            Vector3 tangent = Vector3.Cross(center, Vector3.up).normalized;
            if (tangent.sqrMagnitude < 0.01f) tangent = Vector3.Cross(center, Vector3.forward).normalized;
            float size = Tile.Layer.AverageTileSize;
            Vector3 position = (center + tangent * offset * size * 1.3f).normalized * center.magnitude;
            float zoom = ExpandableWorldObjectsUtility.RawTransitionPct;
            tint.SetColor(ShaderPropertyIDs.Color, new Color(1f, 1f, 1f, alpha));
            WorldRendererUtility.DrawQuadTangentialToPlanet(position, Mathf.Lerp(0.8f, 2f, zoom) * size, altitude,
                Material, propertyBlock: tint);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref contractId, "settlementTransportId");
        }
    }

    public sealed class SettlementTransportPod : ActiveTransporter
    {
        internal bool ReadyToOpen => Contents != null && age > Contents.openDelay;
        protected override void Tick() => age++;

        internal void Open()
        {
            if (def.soundOpen != null) def.soundOpen.PlayOneShot(new TargetInfo(Position, Map));
            Destroy();
        }
    }

    public sealed class SettlementTransportIncomingPod : DropPodIncoming
    {
        protected override void HitRoof() { }

        protected override void Impact()
        {
            if (TransportRoute.IsSafeCell(TransportMode.Pod, null, Position, Map, this)) base.Impact();
            else Destroy();
        }

        protected override void SpawnThings() => GenSpawn.Spawn(GetDirectlyHeldThings()[0], Position, Map);
    }

    public sealed class SettlementTransportShuttle : Building_PassengerShuttle
    {
        public override IEnumerable<Gizmo> GetGizmos() => Array.Empty<Gizmo>();
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn pawn) => Array.Empty<FloatMenuOption>();
        public override IEnumerable<FloatMenuOption> GetMultiSelectFloatMenuOptions(IEnumerable<Pawn> pawns) => Array.Empty<FloatMenuOption>();
    }

    public sealed class SettlementTransportIncomingShuttle : PassengerShuttleIncoming
    {
        protected override void HitRoof() { }

        protected override void Impact()
        {
            if (Shuttle != null && TransportRoute.IsSafeCell(TransportMode.Shuttle, Shuttle.def, Position, Map, this)) base.Impact();
            else
            {
                hasImpacted = true;
                Destroy();
            }
        }

        protected override void SpawnThings() => GenSpawn.Spawn(Shuttle, Position, Map, TransportRoute.LandingRotation);
    }

    public sealed class ShipJob_SettlementTransportUnload : ShipJob
    {
        internal int contractId;
        public override bool Interruptible => false;
        public override bool ShowGizmos => false;
        protected override bool ShouldEnd => transportShip?.shipThing == null || transportShip.shipThing.Destroyed;

        public override bool TryStart()
        {
            if (!base.TryStart()) return false;
            TransportContractRecord contract = SettlementServicesWorldComponent.Current?.FindTransportContract(contractId);
            if (contract?.phase == TransportPhase.ArrivalAnimation && transportShip.shipThing.Spawned && contract.localVehicle == transportShip.shipThing)
                contract.ChangePhase(TransportPhase.Unloading);
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref contractId, "settlementTransportId");
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Framework.Delivery
{
    public class TransportersArrivalAction_ServiceDelivery : TransportersArrivalAction
    {
        private MapParent mapParent;
        private IntVec3 cell = IntVec3.Invalid;
        private int jobId = -1;

        public override bool GeneratesMap => false;

        public TransportersArrivalAction_ServiceDelivery()
        {
        }

        public TransportersArrivalAction_ServiceDelivery(MapParent mapParent, IntVec3 cell, int jobId)
        {
            this.mapParent = mapParent;
            this.cell = cell;
            this.jobId = jobId;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref mapParent, "mapParent");
            Scribe_Values.Look(ref cell, "cell", IntVec3.Invalid);
            Scribe_Values.Look(ref jobId, "jobId", -1);
        }

        public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            Map map = ResolveLandingMap(tile, out IntVec3 landingCell, out bool rerouted);
            Thing lookTarget = TransportersArrivalActionUtility.GetLookTarget(transporters);

            if (map == null)
            {
                ServicePodDeliveryService.HoldForRecovery(transporters, jobId);
                return;
            }

            TransportersArrivalActionUtility.DropTravellingDropPods(transporters, landingCell, map);
            Messages.Message((rerouted ? "SettlementServices.Message.PodDeliveryArrivedRerouted" : "SettlementServices.Message.PodDeliveryArrived").Translate(), lookTarget, MessageTypeDefOf.TaskCompletion);
            ServicePodDeliveryService.NotifyArrival(jobId);
        }

        private Map ResolveLandingMap(PlanetTile arrivalTile, out IntVec3 landingCell, out bool rerouted)
        {
            rerouted = false;
            landingCell = IntVec3.Invalid;

            if (mapParent != null && mapParent.Spawned && mapParent.HasMap && mapParent.Map.IsPlayerHome)
            {
                Map map = mapParent.Map;
                landingCell = cell.IsValid && cell.InBounds(map) ? cell : DropCellFinder.RandomDropSpot(map);
                return map;
            }

            Map fallback = Find.Maps
                .Where(m => m.IsPlayerHome)
                .OrderBy(m => Find.WorldGrid.TraversalDistanceBetween(arrivalTile, m.Tile, passImpassable: true, int.MaxValue, canTraverseLayers: true))
                .FirstOrDefault();
            if (fallback == null) return null;

            rerouted = true;
            landingCell = DropCellFinder.RandomDropSpot(fallback);
            return fallback;
        }
    }
}

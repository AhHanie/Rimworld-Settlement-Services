using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Framework.Delivery
{
    public enum ServicePodDeliveryState
    {
        Pending,
        Dispatching,
        InFlight,
        Delivered,
        FailedRefunded,
    }

    public class ServicePodDeliveryContract : IExposable
    {
        public ServicePodDeliveryState state;
        public int mapParentId = -1;
        public PlanetTile destinationTile = PlanetTile.Invalid;
        public IntVec3 cell = IntVec3.Invalid;
        public PlanetTile originTile = PlanetTile.Invalid;
        public TechLevel providerTechLevel;
        public int podCount;
        public int paidSurcharge;
        public float massCapacityPerPod;
        public int maxRangeTiles;
        public int travellingObjectId = -1;
        public string failureKey;

        public bool IsOutstanding =>
            state == ServicePodDeliveryState.Pending
            || state == ServicePodDeliveryState.Dispatching
            || state == ServicePodDeliveryState.InFlight;

        public void ExposeData()
        {
            Scribe_Values.Look(ref state, "state");
            Scribe_Values.Look(ref mapParentId, "mapParentId", -1);
            Scribe_Values.Look(ref destinationTile, "destinationTile", PlanetTile.Invalid);
            Scribe_Values.Look(ref cell, "cell", IntVec3.Invalid);
            Scribe_Values.Look(ref originTile, "originTile", PlanetTile.Invalid);
            Scribe_Values.Look(ref providerTechLevel, "providerTechLevel");
            Scribe_Values.Look(ref podCount, "podCount");
            Scribe_Values.Look(ref paidSurcharge, "paidSurcharge");
            Scribe_Values.Look(ref massCapacityPerPod, "massCapacityPerPod");
            Scribe_Values.Look(ref maxRangeTiles, "maxRangeTiles");
            Scribe_Values.Look(ref travellingObjectId, "travellingObjectId", -1);
            Scribe_Values.Look(ref failureKey, "failureKey");
        }
    }
}

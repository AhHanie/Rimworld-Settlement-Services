using RimWorld.Planet;
using Settlement_Services.Services.Land;
using Verse;

namespace Settlement_Services.Domain.Records
{
    internal sealed class LandPlotRecord : IExposable
    {
        public int id;
        public int hostId = -1;
        public string hostLabel;
        public PlanetTile hostTile = PlanetTile.Invalid;
        public int side;
        public bool bunkerInterior;
        public bool rented;
        public long createdTick;
        public long paidThrough;
        public long graceThrough;
        public bool warned;
        public bool expiredNotice;
        public bool hostLost;
        public bool generated;
        public bool recoveryError;
        public bool relinquished;
        public SettlementLandMapParent parent;

        internal bool Due(long now) => relinquished || (rented && now >= graceThrough);
        internal bool RecoveryOnly(long now) => hostLost || (rented && now >= paidThrough);
        internal Map Map => parent?.Map;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref hostId, "hostId", -1);
            Scribe_Values.Look(ref hostLabel, "hostLabel");
            Scribe_Values.Look(ref hostTile, "hostTile", PlanetTile.Invalid);
            Scribe_Values.Look(ref side, "side");
            Scribe_Values.Look(ref bunkerInterior, "bunkerInterior", false);
            Scribe_Values.Look(ref rented, "rented");
            Scribe_Values.Look(ref createdTick, "createdTick");
            Scribe_Values.Look(ref paidThrough, "paidThrough");
            Scribe_Values.Look(ref graceThrough, "graceThrough");
            Scribe_Values.Look(ref warned, "warned");
            Scribe_Values.Look(ref expiredNotice, "expiredNotice");
            Scribe_Values.Look(ref hostLost, "hostLost");
            Scribe_Values.Look(ref generated, "generated");
            Scribe_Values.Look(ref recoveryError, "recoveryError");
            Scribe_Values.Look(ref relinquished, "relinquished");
            Scribe_References.Look(ref parent, "parent");
        }
    }
}

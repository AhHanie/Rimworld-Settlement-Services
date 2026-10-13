using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Domain.Records
{
    public class HiringTransitRecord : IExposable
    {
        public int jobId = -1;
        public int originSettlementWorldObjectId = -1;
        public PlanetTile originTile = PlanetTile.Invalid;
        public string originFactionLoadId;

        public int destinationMapId = -1;
        public PlanetTile destinationTile = PlanetTile.Invalid;

        public int departureTick = -1;
        public int arrivalTick = -1;

        public List<Pawn> pawns = new List<Pawn>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref jobId, "jobId", -1);
            Scribe_Values.Look(ref originSettlementWorldObjectId, "originSettlementWorldObjectId", -1);
            Scribe_Values.Look(ref originTile, "originTile", PlanetTile.Invalid);
            Scribe_Values.Look(ref originFactionLoadId, "originFactionLoadId");
            Scribe_Values.Look(ref destinationMapId, "destinationMapId", -1);
            Scribe_Values.Look(ref destinationTile, "destinationTile", PlanetTile.Invalid);
            Scribe_Values.Look(ref departureTick, "departureTick", -1);
            Scribe_Values.Look(ref arrivalTick, "arrivalTick", -1);
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && pawns == null) pawns = new List<Pawn>();
        }
    }
}

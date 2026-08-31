using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.UI.Interaction
{
    public class CaravanArrivalAction_VisitServices : CaravanArrivalAction
    {
        private Settlement settlement;

        public CaravanArrivalAction_VisitServices() { }
        public CaravanArrivalAction_VisitServices(Settlement settlement) { this.settlement = settlement; }

        public override string Label => "SettlementServices.Command.Services".Translate();
        public override string ReportString => "SettlementServices.Command.TravelingToVisitServices".Translate();

        public override FloatMenuAcceptanceReport StillValid(Caravan caravan, PlanetTile destinationTile)
        {
            FloatMenuAcceptanceReport baseReport = base.StillValid(caravan, destinationTile);
            return !baseReport.Accepted ? baseReport : CanVisit(caravan, settlement);
        }

        public override void Arrived(Caravan caravan)
        {
            CameraJumper.TryJumpAndSelect(caravan);
            Find.WindowStack.Add(new Dialog_SettlementServices(ServiceRequestSession.ForInPersonVisit(settlement, caravan)));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref settlement, "settlement");
        }

        public static FloatMenuAcceptanceReport CanVisit(Caravan caravan, Settlement settlement)
        {
            if (settlement == null || !settlement.Spawned || settlement.HasMap) return false;
            if (settlement.Faction != null && settlement.Faction.HostileTo(Faction.OfPlayer))
                return FloatMenuAcceptanceReport.WithFailReason("SettlementServices.Error.FactionHostile".Translate());
            if (BestCaravanPawnUtility.FindBestNegotiator(caravan) == null)
                return FloatMenuAcceptanceReport.WithFailReason("SettlementServices.Command.NoNegotiator".Translate());
            return true;
        }

        public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan, Settlement settlement)
        {
            return CaravanArrivalActionUtility.GetFloatMenuOptions(
                () => CanVisit(caravan, settlement),
                () => new CaravanArrivalAction_VisitServices(settlement),
                "SettlementServices.Command.Services".Translate(),
                caravan, settlement.Tile, settlement);
        }
    }
}

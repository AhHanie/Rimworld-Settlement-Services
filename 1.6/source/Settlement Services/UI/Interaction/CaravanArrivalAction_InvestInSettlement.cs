using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.UI.Interaction
{
    public class CaravanArrivalAction_InvestInSettlement : CaravanArrivalAction
    {
        private Settlement settlement;

        public CaravanArrivalAction_InvestInSettlement() { }
        public CaravanArrivalAction_InvestInSettlement(Settlement settlement) { this.settlement = settlement; }

        public override string Label => "SettlementServices.Command.Invest".Translate();
        public override string ReportString => "SettlementServices.Command.TravelingToInvest".Translate();

        public override FloatMenuAcceptanceReport StillValid(Caravan caravan, PlanetTile destinationTile)
        {
            FloatMenuAcceptanceReport baseReport = base.StillValid(caravan, destinationTile);
            return !baseReport.Accepted ? baseReport : CaravanArrivalAction_VisitServices.CanVisit(caravan, settlement);
        }

        public override void Arrived(Caravan caravan)
        {
            CameraJumper.TryJumpAndSelect(caravan);
            Find.WindowStack.Add(new Dialog_SettlementInvestment(settlement, caravan));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref settlement, "settlement");
        }

        public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan, Settlement settlement)
        {
            return CaravanArrivalActionUtility.GetFloatMenuOptions(
                () => CaravanArrivalAction_VisitServices.CanVisit(caravan, settlement),
                () => new CaravanArrivalAction_InvestInSettlement(settlement),
                "SettlementServices.Command.Invest".Translate(),
                caravan, settlement.Tile, settlement);
        }
    }
}

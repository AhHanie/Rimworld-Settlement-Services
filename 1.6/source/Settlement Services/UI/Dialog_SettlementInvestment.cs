using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Investment;

namespace Settlement_Services.UI
{
    public class Dialog_SettlementInvestment : Window
    {
        private readonly Settlement settlement;
        private readonly Caravan caravan;

        public override Vector2 InitialSize => new Vector2(420f, 260f);

        public Dialog_SettlementInvestment(Settlement settlement, Caravan caravan)
        {
            this.settlement = settlement;
            this.caravan = caravan;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Medium;
            listing.Label("SettlementServices.Label.InvestmentDialogTitle".Translate(settlement.LabelCap));
            Text.Font = GameFont.Small;
            listing.GapLine();

            listing.Label(StatusLabel());
            listing.Gap();
            listing.Label("SettlementServices.Label.InvestmentCost".Translate(SettlementInvestmentService.InvestCost()));

            if (listing.ButtonText("SettlementServices.Button.Invest".Translate()))
            {
                if (SettlementInvestmentService.TryInvest(settlement, caravan, out string errorKey))
                {
                    Messages.Message("SettlementServices.Message.InvestmentMade".Translate(settlement.LabelCap), MessageTypeDefOf.PositiveEvent, historical: false);
                    Close();
                }
                else
                {
                    Messages.Message(errorKey.Translate(), MessageTypeDefOf.RejectInput, historical: false);
                }
            }

            listing.End();
        }

        private string StatusLabel()
        {
            InvestmentRecord record = SettlementInvestmentService.GetRecord(settlement);
            if (record == null) return "SettlementServices.Label.InvestmentStatusNone".Translate();

            float pct = SettlementInvestmentService.CurrentDiscountPct(settlement);
            if (pct <= 0f) return "SettlementServices.Label.InvestmentStatusExpired".Translate();

            int daysRemaining = SettlementInvestmentService.TicksRemaining(settlement) / GenDate.TicksPerDay;
            return "SettlementServices.Label.InvestmentStatusActive".Translate(pct.ToStringPercent(), daysRemaining);
        }
    }
}

using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Payment;
using Settlement_Services.Framework.Pricing;

namespace Settlement_Services.Framework.Investment
{
    public static class SettlementInvestmentService
    {
        public static InvestmentRecord GetRecord(Settlement settlement) =>
            settlement == null ? null : SettlementServicesWorldComponent.Current?.GetInvestment(settlement.ID);

        public static float CurrentDiscountPct(Settlement settlement) =>
            InvestmentPricing.CurrentDiscountPct(GetRecord(settlement), Find.TickManager.TicksGame);

        public static int TicksRemaining(Settlement settlement) =>
            InvestmentPricing.TicksRemaining(GetRecord(settlement), Find.TickManager.TicksGame);

        public static int InvestCost()
        {
            IServicePricingContext ctx = ServicePricingContext.Current;
            return InvestmentPricing.Cost(ctx.TotalPlayerWealth, ctx.DifficultyMultiplier);
        }

        public static bool TryInvest(Settlement settlement, Caravan caravan, out string errorKey)
        {
            if (settlement?.Faction != null && settlement.Faction.HostileTo(Faction.OfPlayer))
            { errorKey = "SettlementServices.Error.FactionHostile"; return false; }

            Pawn negotiator = BestCaravanPawnUtility.FindBestNegotiator(caravan);
            if (negotiator == null) { errorKey = "SettlementServices.Command.NoNegotiator"; return false; }

            var request = new SettlementServiceRequest { negotiator = negotiator };
            if (!CaravanSilverPaymentProvider.Instance.TryDebit(InvestCost(), request, out errorKey)) return false;

            SettlementServicesWorldComponent.Current.Invest(settlement.ID, InvestmentPricing.FullStrengthDiscountPct, InvestmentPricing.DecayDurationTicks);
            errorKey = null;
            return true;
        }
    }
}

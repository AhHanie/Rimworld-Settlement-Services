using UnityEngine;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Pricing;

namespace Settlement_Services.Framework.Investment
{
    internal static class InvestmentPricing
    {
        private const float BaseCostWealthScale = 0.01f;
        private const float BaseDiscountPct = 0.15f;
        private const int BaseDecayDurationTicks = 1800000;

        public static int Cost(float wealth, float difficultyMultiplier) =>
            Mathf.RoundToInt(BaseCostWealthScale * ModSettings.Current.investmentCostScalePct * WealthScaling.EffectiveWealth(wealth) * difficultyMultiplier);

        public static float FullStrengthDiscountPct => BaseDiscountPct * ModSettings.Current.investmentDiscountScalePct;

        public static int DecayDurationTicks => Mathf.RoundToInt(BaseDecayDurationTicks * ModSettings.Current.investmentDecayDurationScalePct);

        public static float CurrentDiscountPct(InvestmentRecord investment, int nowTick)
        {
            if (investment == null || investment.decayDurationTicks <= 0) return 0f;
            int elapsed = nowTick - investment.investedTick;
            if (elapsed >= investment.decayDurationTicks) return 0f;
            if (elapsed <= 0) return investment.investedDiscountPct;
            return investment.investedDiscountPct * (1f - (float)elapsed / investment.decayDurationTicks);
        }

        public static int TicksRemaining(InvestmentRecord investment, int nowTick) =>
            investment == null ? 0 : Mathf.Max(0, investment.investedTick + investment.decayDurationTicks - nowTick);
    }
}

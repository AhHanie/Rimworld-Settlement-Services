using UnityEngine;
using Settlement_Services.Framework.Pricing;

namespace Settlement_Services.Services.Hospitality
{
    internal static class HospitalityPackagePricing
    {
        public static int ScaledCost(HospitalityPackageDef pkg, IServicePricingContext pricing, bool difficultyScaling, float specialtyPriceModifierPct)
        {
            float wealthDerivedCost = pkg.wealthScale * WealthScaling.EffectiveWealth(pricing.TotalPlayerWealth) * pricing.WealthPriceScalePct
                * (difficultyScaling ? pricing.DifficultyMultiplier : 1f);
            int baseCost = Mathf.RoundToInt(Mathf.Max(pkg.minimumCost, wealthDerivedCost));
            return baseCost + Mathf.RoundToInt(baseCost * specialtyPriceModifierPct);
        }
    }
}

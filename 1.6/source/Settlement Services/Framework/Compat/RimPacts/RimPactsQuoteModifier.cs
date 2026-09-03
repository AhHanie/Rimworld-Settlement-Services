using UnityEngine;
using Settlement_Services.Framework.Compatibility;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.Framework.Compat.RimPacts
{
    internal sealed class RimPactsQuoteModifier : ICompatibilityQuoteModifier
    {
        public int Order => 0;

        public void Modify(CompatibilityQuoteContext context)
        {
            RimPactsMarketSnapshot snapshot = RimPactsAdapter.GetMarketSnapshot();
            if (!snapshot.pricingActive) return;

            float scale = ModSettings.Current.compatibilitySettings.GetFloat(RimPactsSettingsSection.MarketPriceScalePctKey, 1f);
            float effectiveOffset = snapshot.priceOffset * scale;
            int marketDelta = Mathf.RoundToInt(context.totalCost * effectiveOffset);
            if (marketDelta == 0) return;

            context.lineItems.Add(new ServiceLineItem("SettlementServices.LineItem.RimPactsMarket", marketDelta, isModifier: true));
            context.totalCost += marketDelta;
        }
    }
}

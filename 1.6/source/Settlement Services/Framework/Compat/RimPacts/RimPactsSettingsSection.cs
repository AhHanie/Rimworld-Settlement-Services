using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Compatibility;

namespace Settlement_Services.Framework.Compat.RimPacts
{
    internal sealed class RimPactsSettingsSection : ICompatibilitySettingsSection
    {
        internal const string MarketPriceScalePctKey = "rimpacts.marketPriceScalePct";
        internal const string TrustRewardsEnabledKey = "rimpacts.trustRewardsEnabled";
        internal const string TrustRewardCooldownDaysKey = "rimpacts.trustRewardCooldownDays";

        public string ModuleId => RimPactsCompatibilityModule.Id;

        public void Draw(Listing_Standard listing, CompatibilitySettingsStore settings)
        {
            Text.Font = GameFont.Medium;
            listing.Label("SettlementServices.Settings.SectionRimPacts".Translate());
            Text.Font = GameFont.Small;

            float marketScale = settings.GetFloat(MarketPriceScalePctKey, 1f);
            float updatedMarketScale = listing.SliderLabeled(
                "SettlementServices.Settings.RimPactsMarketPriceScalePct".Translate(marketScale.ToStringPercent()),
                marketScale, 0f, 2f,
                tooltip: "SettlementServices.Settings.RimPactsMarketPriceScalePct.Tooltip".Translate());
            if (!Mathf.Approximately(updatedMarketScale, marketScale)) settings.SetFloat(MarketPriceScalePctKey, updatedMarketScale);

            bool trustEnabled = settings.GetBool(TrustRewardsEnabledKey, true);
            bool updatedTrustEnabled = trustEnabled;
            listing.CheckboxLabeled(
                "SettlementServices.Settings.RimPactsTrustRewardsEnabled".Translate(), ref updatedTrustEnabled,
                "SettlementServices.Settings.RimPactsTrustRewardsEnabled.Tooltip".Translate());
            if (updatedTrustEnabled != trustEnabled) settings.SetBool(TrustRewardsEnabledKey, updatedTrustEnabled);

            if (updatedTrustEnabled)
            {
                int cooldownDays = settings.GetInt(TrustRewardCooldownDaysKey, 10);
                int updatedCooldownDays = Mathf.RoundToInt(listing.SliderLabeled(
                    "SettlementServices.Settings.RimPactsTrustRewardCooldownDays".Translate(cooldownDays),
                    cooldownDays, 1f, 60f,
                    tooltip: "SettlementServices.Settings.RimPactsTrustRewardCooldownDays.Tooltip".Translate()));
                if (updatedCooldownDays != cooldownDays) settings.SetInt(TrustRewardCooldownDaysKey, updatedCooldownDays);
            }
        }
    }
}

using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Pricing;
using Settlement_Services.UI.Audio;

namespace Settlement_Services
{
    public static class ModSettingsWindow
    {
        private static Vector2 scrollPosition = Vector2.zero;

        private static float contentHeight = 800f;

        public static void Draw(Rect parent)
        {
            ModSettings settings = ModSettings.Current;
            Rect viewRect = new Rect(0f, 0f, parent.width - 24f, contentHeight);

            Widgets.BeginScrollView(parent, ref scrollPosition, viewRect);
            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            DrawPricingSection(listing, settings);
            listing.GapLine();
            DrawDifficultySection(listing, settings);
            listing.GapLine();
            DrawFrameworkSection(listing, settings);

            contentHeight = listing.CurHeight;
            listing.End();
            Widgets.EndScrollView();
        }

        private static void DrawPricingSection(Listing_Standard listing, ModSettings settings)
        {
            Text.Font = GameFont.Medium;
            listing.Label("SettlementServices.Settings.SectionPricing".Translate());
            Text.Font = GameFont.Small;

            settings.wealthPriceScalePct = listing.SliderLabeled(
                "SettlementServices.Settings.WealthPriceScalePct".Translate(settings.wealthPriceScalePct.ToStringPercent()),
                settings.wealthPriceScalePct, 0f, 3f,
                tooltip: "SettlementServices.Settings.WealthPriceScalePct.Tooltip".Translate());

            settings.goodwillDiscountScalePct = listing.SliderLabeled(
                "SettlementServices.Settings.GoodwillDiscountScalePct".Translate(settings.goodwillDiscountScalePct.ToStringPercent()),
                settings.goodwillDiscountScalePct, 0f, 2f,
                tooltip: "SettlementServices.Settings.GoodwillDiscountScalePct.Tooltip".Translate());

            // TODO: Re-enable investment settings after investment testing is complete.
            //settings.investmentCostScalePct = listing.SliderLabeled(
            //    "SettlementServices.Settings.InvestmentCostScalePct".Translate(settings.investmentCostScalePct.ToStringPercent()),
            //    settings.investmentCostScalePct, 0f, 3f,
            //    tooltip: "SettlementServices.Settings.InvestmentCostScalePct.Tooltip".Translate());

            //settings.investmentDiscountScalePct = listing.SliderLabeled(
            //    "SettlementServices.Settings.InvestmentDiscountScalePct".Translate(settings.investmentDiscountScalePct.ToStringPercent()),
            //    settings.investmentDiscountScalePct, 0f, 2f,
            //    tooltip: "SettlementServices.Settings.InvestmentDiscountScalePct.Tooltip".Translate());

            //settings.investmentDecayDurationScalePct = listing.SliderLabeled(
            //    "SettlementServices.Settings.InvestmentDecayDurationScalePct".Translate(settings.investmentDecayDurationScalePct.ToStringPercent()),
            //    settings.investmentDecayDurationScalePct, 0f, 3f,
            //    tooltip: "SettlementServices.Settings.InvestmentDecayDurationScalePct.Tooltip".Translate());
        }

        private static void DrawDifficultySection(Listing_Standard listing, ModSettings settings)
        {
            Text.Font = GameFont.Medium;
            listing.Label("SettlementServices.Settings.SectionDifficulty".Translate());
            Text.Font = GameFont.Small;

            foreach (DifficultyDef def in DefDatabase<DifficultyDef>.AllDefsListForReading)
            {
                float current = settings.difficultyMultiplierOverrides.TryGetValue(def.defName, out float overridden)
                    ? overridden
                    : DefaultMultiplierFor(def);

                float updated = listing.SliderLabeled(
                    "SettlementServices.Settings.DifficultyMultiplier".Translate(def.LabelCap, current.ToStringPercent()),
                    current, 0f, 3f,
                    tooltip: "SettlementServices.Settings.DifficultyMultiplier.Tooltip".Translate());

                if (!Mathf.Approximately(updated, current))
                    settings.difficultyMultiplierOverrides[def.defName] = updated;
            }
        }

        private static void DrawFrameworkSection(Listing_Standard listing, ModSettings settings)
        {
            Text.Font = GameFont.Medium;
            listing.Label("SettlementServices.Settings.SectionFramework".Translate());
            Text.Font = GameFont.Small;

            // TODO: Re-enable the service-event setting after event testing is complete.
            //settings.serviceEventFrequencyPct = listing.SliderLabeled(
            //    "SettlementServices.Settings.ServiceEventFrequencyPct".Translate(settings.serviceEventFrequencyPct.ToStringPercent()),
            //    settings.serviceEventFrequencyPct, 0f, 2f,
            //    tooltip: "SettlementServices.Settings.ServiceEventFrequencyPct.Tooltip".Translate());

            bool soundtrackEnabled = settings.soundtrackEnabled;
            listing.CheckboxLabeled(
                "SettlementServices.Settings.SoundtrackEnabled".Translate(), ref soundtrackEnabled,
                "SettlementServices.Settings.SoundtrackEnabled.Tooltip".Translate());
            if (soundtrackEnabled != settings.soundtrackEnabled)
            {
                settings.soundtrackEnabled = soundtrackEnabled;
                ServiceSoundtrackController.RefreshForSettingsChange();
            }
        }

        private static float DefaultMultiplierFor(DifficultyDef def)
        {
            SettlementServiceDifficultyExtension ext = def.GetModExtension<SettlementServiceDifficultyExtension>();
            return ext?.settlementPriceMultiplier ?? 1f;
        }
    }
}

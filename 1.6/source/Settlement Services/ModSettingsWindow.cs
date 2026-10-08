using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Compatibility;
using Settlement_Services.Framework.Pricing;
using Settlement_Services.Framework.Specialty;
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
            listing.maxOneColumn = true;
            listing.Begin(viewRect);

            DrawPricingSection(listing, settings);
            listing.GapLine();
            DrawStockSection(listing, settings);
            listing.GapLine();
            DrawSpecialtySection(listing, settings);
            listing.GapLine();
            DrawDifficultySection(listing, settings);
            listing.GapLine();
            DrawMoodBuffsSection(listing, settings);
            listing.GapLine();
            DrawFrameworkSection(listing, settings);

            if (SettlementServicesCompatibilityRegistry.HasSettingsSections)
            {
                listing.GapLine();
                SettlementServicesCompatibilityRegistry.DrawSettingsSections(listing);
            }

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

            bool negotiatorSocialDiscountEnabled = settings.negotiatorSocialDiscountEnabled;
            listing.CheckboxLabeled(
                "SettlementServices.Settings.NegotiatorSocialDiscountEnabled".Translate(), ref negotiatorSocialDiscountEnabled,
                "SettlementServices.Settings.NegotiatorSocialDiscountEnabled.Tooltip".Translate());
            settings.negotiatorSocialDiscountEnabled = negotiatorSocialDiscountEnabled;

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

        private static void DrawStockSection(Listing_Standard listing, ModSettings settings)
        {
            Text.Font = GameFont.Medium;
            listing.Label("SettlementServices.Settings.SectionStock".Translate());
            Text.Font = GameFont.Small;

            settings.stockQuantityMultiplier = SliderLabeledInt(
                listing,
                "SettlementServices.Settings.StockQuantityMultiplier".Translate(settings.stockQuantityMultiplier + "×"),
                settings.stockQuantityMultiplier, ModSettings.MinStockQuantityMultiplier, ModSettings.MaxStockQuantityMultiplier,
                tooltip: "SettlementServices.Settings.StockQuantityMultiplier.Tooltip".Translate());
        }

        private static void DrawSpecialtySection(Listing_Standard listing, ModSettings settings)
        {
            Text.Font = GameFont.Medium;
            listing.Label("SettlementServices.Settings.SectionSpecialties".Translate());
            Text.Font = GameFont.Small;

            listing.Label("SettlementServices.Settings.SpecialtiesNotice".Translate());

            int offset = settings.specialtyCountOffset;
            string signedOffset = (offset > 0 ? "+" : "") + offset;
            settings.specialtyCountOffset = SliderLabeledInt(
                listing,
                "SettlementServices.Settings.SpecialtyCountOffset".Translate(signedOffset),
                offset, ModSettings.MinSpecialtyCountOffset, ModSettings.MaxSpecialtyCountOffset,
                tooltip: "SettlementServices.Settings.SpecialtyCountOffset.Tooltip".Translate());

            settings.specialtyChanceOnePct = SliderLabeledInt(
                listing,
                "SettlementServices.Settings.SpecialtyChanceOne".Translate(settings.specialtyChanceOnePct),
                settings.specialtyChanceOnePct, 0, 100,
                tooltip: "SettlementServices.Settings.SpecialtyChances.Tooltip".Translate());

            int maxChanceTwo = 100 - settings.specialtyChanceOnePct;
            settings.specialtyChanceTwoPct = Mathf.Min(settings.specialtyChanceTwoPct, maxChanceTwo);
            settings.specialtyChanceTwoPct = SliderLabeledInt(
                listing,
                "SettlementServices.Settings.SpecialtyChanceTwo".Translate(settings.specialtyChanceTwoPct),
                settings.specialtyChanceTwoPct, 0, maxChanceTwo,
                tooltip: "SettlementServices.Settings.SpecialtyChances.Tooltip".Translate());

            Rect chanceThreeRect = listing.GetRect(30f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(chanceThreeRect, "SettlementServices.Settings.SpecialtyChanceThree".Translate(settings.SpecialtyChanceThreePct));
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(chanceThreeRect, "SettlementServices.Settings.SpecialtyChances.Tooltip".Translate());
            listing.Gap(listing.verticalSpacing);

            Rect resetRect = listing.GetRect(30f);
            if (Widgets.ButtonText(resetRect.LeftPart(0.25f), "SettlementServices.Settings.SpecialtyResetChances".Translate()))
            {
                settings.specialtyChanceOnePct = ModSettings.DefaultSpecialtyChanceOnePct;
                settings.specialtyChanceTwoPct = ModSettings.DefaultSpecialtyChanceTwoPct;
            }
            listing.Gap(listing.verticalSpacing);

            int adjustedOne = SettlementSpecialtyGenerator.ResolveCount(1, settings.specialtyCountOffset, int.MaxValue);
            int adjustedTwo = SettlementSpecialtyGenerator.ResolveCount(2, settings.specialtyCountOffset, int.MaxValue);
            int adjustedThree = SettlementSpecialtyGenerator.ResolveCount(3, settings.specialtyCountOffset, int.MaxValue);
            listing.Label("SettlementServices.Settings.SpecialtyPreview".Translate(adjustedOne, adjustedTwo, adjustedThree));
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

        private static void DrawMoodBuffsSection(Listing_Standard listing, ModSettings settings)
        {
            Text.Font = GameFont.Medium;
            listing.Label("SettlementServices.Settings.SectionMoodBuffs".Translate());
            Text.Font = GameFont.Small;

            listing.Label("SettlementServices.Settings.MoodBuffsRestartNotice".Translate());

            foreach (MoodThoughtCatalog.Entry entry in MoodThoughtCatalog.Entries)
            {
                ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(entry.defName);
                if (thought == null || thought.stages.NullOrEmpty()) continue;

                int current = Mathf.RoundToInt(MoodThoughtCatalog.GetValue(settings, entry.defName));
                string thoughtLabel = thought.stages[0].label.CapitalizeFirst();
                string signedValue = (current >= 0 ? "+" : "") + current;

                int updated = SliderLabeledInt(
                    listing,
                    "SettlementServices.Settings.MoodBuff".Translate(thoughtLabel, signedValue),
                    current, Mathf.RoundToInt(MoodThoughtCatalog.MinMoodEffect), Mathf.RoundToInt(MoodThoughtCatalog.MaxMoodEffect),
                    tooltip: "SettlementServices.Settings.MoodBuff.Tooltip".Translate());

                if (updated != current)
                    settings.moodThoughtOverrides[entry.defName] = updated;
            }
        }

        private static void DrawFrameworkSection(Listing_Standard listing, ModSettings settings)
        {
            Text.Font = GameFont.Medium;
            listing.Label("SettlementServices.Settings.SectionFramework".Translate());
            Text.Font = GameFont.Small;

            settings.serviceEventFrequencyPct = listing.SliderLabeled(
                "SettlementServices.Settings.ServiceEventFrequencyPct".Translate(settings.serviceEventFrequencyPct.ToStringPercent()),
                settings.serviceEventFrequencyPct, 0f, 2f,
                tooltip: "SettlementServices.Settings.ServiceEventFrequencyPct.Tooltip".Translate());

            bool soundtrackEnabled = settings.soundtrackEnabled;
            listing.CheckboxLabeled(
                "SettlementServices.Settings.SoundtrackEnabled".Translate(), ref soundtrackEnabled,
                "SettlementServices.Settings.SoundtrackEnabled.Tooltip".Translate());
            if (soundtrackEnabled != settings.soundtrackEnabled)
            {
                settings.soundtrackEnabled = soundtrackEnabled;
                ServiceSoundtrackController.RefreshForSettingsChange();
            }

            bool verboseLoggingEnabled = settings.verboseLoggingEnabled;
            listing.CheckboxLabeled(
                "SettlementServices.Settings.VerboseLoggingEnabled".Translate(), ref verboseLoggingEnabled,
                "SettlementServices.Settings.VerboseLoggingEnabled.Tooltip".Translate());
            settings.verboseLoggingEnabled = verboseLoggingEnabled;
        }

        private static int SliderLabeledInt(Listing_Standard listing, string label, int val, int min, int max, float labelPct = 0.5f, string tooltip = null)
        {
            Rect rect = listing.GetRect(30f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(rect.LeftPart(labelPct), label);
            if (tooltip != null) TooltipHandler.TipRegion(rect.LeftPart(labelPct), tooltip);
            Text.Anchor = TextAnchor.UpperLeft;
            if (max <= min)
            {
                listing.Gap(listing.verticalSpacing);
                return min;
            }
            float result = Widgets.HorizontalSlider(rect.RightPart(1f - labelPct), val, min, max, middleAlignment: true, roundTo: 1f);
            listing.Gap(listing.verticalSpacing);
            return Mathf.RoundToInt(result);
        }

        private static float DefaultMultiplierFor(DifficultyDef def)
        {
            SettlementServiceDifficultyExtension ext = def.GetModExtension<SettlementServiceDifficultyExtension>();
            return ext?.settlementPriceMultiplier ?? 1f;
        }
    }
}

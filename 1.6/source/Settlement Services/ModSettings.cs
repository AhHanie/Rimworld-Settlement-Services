using System.Collections.Generic;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Compatibility;

namespace Settlement_Services
{
    public class ModSettings : Verse.ModSettings
    {
        private static ModSettings current;

        public static ModSettings Current =>
            current ?? (current = LoadedModManager.GetMod<Mod>().GetSettings<ModSettings>());

        public float wealthPriceScalePct = 1f;

        public Dictionary<string, float> difficultyMultiplierOverrides = new Dictionary<string, float>();

        public float serviceEventFrequencyPct = 1f;

        public float goodwillDiscountScalePct = 1f;

        public float investmentCostScalePct = 1f;

        public float investmentDiscountScalePct = 1f;

        public float investmentDecayDurationScalePct = 1f;

        public CompatibilitySettingsStore compatibilitySettings = new CompatibilitySettingsStore();

        public bool soundtrackEnabled = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref wealthPriceScalePct, "wealthPriceScalePct", 1f);
            Scribe_Collections.Look(ref difficultyMultiplierOverrides, "difficultyMultiplierOverrides", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref serviceEventFrequencyPct, "serviceEventFrequencyPct", 1f);
            Scribe_Values.Look(ref goodwillDiscountScalePct, "goodwillDiscountScalePct", 1f);
            Scribe_Values.Look(ref investmentCostScalePct, "investmentCostScalePct", 1f);
            Scribe_Values.Look(ref investmentDiscountScalePct, "investmentDiscountScalePct", 1f);
            Scribe_Values.Look(ref investmentDecayDurationScalePct, "investmentDecayDurationScalePct", 1f);
            Scribe_Deep.Look(ref compatibilitySettings, "compatibilitySettings");
            Scribe_Values.Look(ref soundtrackEnabled, "soundtrackEnabled", false);

            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;

            if (difficultyMultiplierOverrides == null) difficultyMultiplierOverrides = new Dictionary<string, float>();
            if (compatibilitySettings == null) compatibilitySettings = new CompatibilitySettingsStore();

            wealthPriceScalePct = Mathf.Clamp(wealthPriceScalePct, 0f, 3f);
            serviceEventFrequencyPct = Mathf.Clamp(serviceEventFrequencyPct, 0f, 2f);
            goodwillDiscountScalePct = Mathf.Clamp(goodwillDiscountScalePct, 0f, 2f);
            investmentCostScalePct = Mathf.Clamp(investmentCostScalePct, 0f, 3f);
            investmentDiscountScalePct = Mathf.Clamp(investmentDiscountScalePct, 0f, 2f);
            investmentDecayDurationScalePct = Mathf.Clamp(investmentDecayDurationScalePct, 0f, 3f);
            var keys = new List<string>(difficultyMultiplierOverrides.Keys);
            foreach (string key in keys) difficultyMultiplierOverrides[key] = Mathf.Clamp(difficultyMultiplierOverrides[key], 0f, 3f);
        }
    }
}

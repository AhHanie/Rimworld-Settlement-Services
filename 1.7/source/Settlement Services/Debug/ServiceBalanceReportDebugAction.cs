using System.Linq;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Pricing;
using Settlement_Services.Services.Education;

namespace Settlement_Services.Debug
{
    public static class ServiceBalanceReportDebugAction
    {
        private static readonly float[] WealthSamples = { 10000f, 50000f, 150000f, 400000f };
        private static readonly int[] GoodwillSamples = { 0, 20, 40, 60, 80, 100 };

        [DebugAction("Settlement Services", "Log price/goodwill balance report", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        private static void LogBalanceReport()
        {
            foreach (SettlementServiceDef def in DefDatabase<SettlementServiceDef>.AllDefsListForReading)
            {
                if (def is SkillLessonServiceDef lessonDef && !lessonDef.lessonOptions.NullOrEmpty())
                {
                    foreach (SkillLessonOption option in lessonDef.lessonOptions)
                        LogRows($"{def.defName} [{option.key}]", def, option.priceMultiplier);
                    continue;
                }

                LogRows(def.defName, def, 0f);
            }
        }

        private static void LogRows(string label, SettlementServiceDef def, float wealthScaleAddition)
        {
            Logger.Message($"=== {label} (MinCost {def.minimumCost}) ===");
            foreach (DifficultyDef difficulty in DefDatabase<DifficultyDef>.AllDefsListForReading)
            {
                float difficultyMultiplier = DifficultyMultiplierFor(difficulty);
                foreach (float wealth in WealthSamples)
                {
                    float baseCost = ServicePricingEngine.ScaledCost(def, wealth, difficultyMultiplier, ModSettings.Current.wealthPriceScalePct, wealthScaleAddition);
                    string row = string.Join(" | ", GoodwillSamples.Select(g =>
                    {
                        float pct = GoodwillBands.DiscountPctFor(g);
                        int total = Mathf.Max(def.minimumCost, Mathf.RoundToInt(baseCost + baseCost * pct));
                        return $"gw{g}={total}";
                    }));
                    Logger.Message($"  {difficulty.defName} wealth={wealth:0}: {row}");
                }
            }
        }

        private static float DifficultyMultiplierFor(DifficultyDef def)
        {
            if (ModSettings.Current.difficultyMultiplierOverrides.TryGetValue(def.defName, out float overridden)) return overridden;
            SettlementServiceDifficultyExtension ext = def.GetModExtension<SettlementServiceDifficultyExtension>();
            return ext?.settlementPriceMultiplier ?? 1f;
        }
    }
}

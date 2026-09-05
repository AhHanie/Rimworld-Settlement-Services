using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Compatibility;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Investment;
using Settlement_Services.Framework.Specialty;
using Settlement_Services;

namespace Settlement_Services.Framework.Pricing
{
    public class ServicePricingContext : IServicePricingContext
    {
        public static readonly ServicePricingContext Current = new ServicePricingContext();

        public float TotalPlayerWealth => Find.Maps.Where(m => m.IsPlayerHome).Sum(m => m.wealthWatcher.WealthTotal);

        public float DifficultyMultiplier
        {
            get
            {
                DifficultyDef def = Find.Storyteller?.difficultyDef;
                if (def != null && ModSettings.Current.difficultyMultiplierOverrides.TryGetValue(def.defName, out float overridden))
                    return overridden;

                SettlementServiceDifficultyExtension ext = def?.GetModExtension<SettlementServiceDifficultyExtension>();
                if (ext != null) return ext.settlementPriceMultiplier;
                if (def != null && def.isCustom) return Find.Storyteller.difficulty.threatScale;
                return 1f;
            }
        }

        public float WealthPriceScalePct => ModSettings.Current.wealthPriceScalePct;

        public float ReputationModifierPct(Faction faction) =>
            faction == null ? 0f : GoodwillBands.DiscountPctFor(faction.PlayerGoodwill);
    }

    public static class ServicePricingEngine
    {
        public static SettlementServiceQuote Finalize(SettlementServiceDef def, SettlementServiceRequest request, List<ServiceLineItem> workerLineItems)
        {
            IServicePricingContext ctx = ServicePricingContext.Current;
            float wealthScaleAddition = def.Worker.WealthScaleAdditionFor(request);
            float scaled = ScaledCost(def, ctx.TotalPlayerWealth, ctx.DifficultyMultiplier, ctx.WealthPriceScalePct, wealthScaleAddition);
            scaled *= def.Worker.BasePriceMultiplierFor(request);

            var lineItems = new List<ServiceLineItem>(workerLineItems);
            Settlement settlement = request.settlement;
            Faction faction = settlement?.Faction;

            AddPctModifier(lineItems, scaled, settlement != null ? SettlementSpecialtyService.TotalPriceModifierPct(settlement, def) : 0f, "SettlementServices.LineItem.SpecialtyModifier");
            AddPctModifier(lineItems, scaled, ctx.ReputationModifierPct(faction), "SettlementServices.LineItem.ReputationModifier");
            AddPctModifier(lineItems, scaled, settlement != null ? -SettlementInvestmentService.CurrentDiscountPct(settlement) : 0f, "SettlementServices.LineItem.InvestmentDiscount");

            ServicePriorityTierDef tier = ResolveTier(def, request.selectedTierKey);
            AddPctModifier(lineItems, scaled, tier?.costSurchargePct ?? 0f, "SettlementServices.LineItem.PriorityTier");

            float preMarketTotal = Mathf.Max(def.minimumCost, lineItems.Aggregate(scaled, (acc, li) => acc + li.amount));

            var compatibilityContext = new CompatibilityQuoteContext(def, request, lineItems, Mathf.RoundToInt(preMarketTotal));
            SettlementServicesCompatibilityRegistry.ModifyQuote(compatibilityContext);

            return new SettlementServiceQuote
            {
                lineItems = lineItems,
                totalCost = compatibilityContext.totalCost,
                selectedTierKey = request.selectedTierKey,
                expectedDurationTicks = EffectiveDuration(def, request, settlement, tier),
            };
        }

        private static void AddPctModifier(List<ServiceLineItem> lineItems, float scaled, float pct, string labelKey)
        {
            if (Mathf.Approximately(pct, 0f)) return;
            lineItems.Add(new ServiceLineItem(labelKey, Mathf.RoundToInt(scaled * pct), isModifier: true));
        }

        internal static float ScaledCost(SettlementServiceDef def, float wealth, float difficultyMultiplier, float wealthPriceScalePct, float wealthScaleAddition = 0f)
        {
            float wealthDerivedCost = (def.wealthScale + wealthScaleAddition) * WealthScaling.EffectiveWealth(wealth) * wealthPriceScalePct * (def.difficultyScaling ? difficultyMultiplier : 1f);
            return Mathf.Max(def.minimumCost, wealthDerivedCost);
        }

        internal static ServicePriorityTierDef ResolveTier(SettlementServiceDef def, string tierKey) =>
            tierKey != null ? def.priorityTiers?.Find(t => t.key == tierKey) : null;

        private static int EffectiveDuration(SettlementServiceDef def, SettlementServiceRequest request, Settlement settlement, ServicePriorityTierDef tier)
        {
            int? finalDuration = def.Worker.ExpectedDurationTicksFor(request);
            if (finalDuration.HasValue) return Mathf.Max(1, finalDuration.Value);

            float specialtyOffset = settlement != null ? SettlementSpecialtyService.TotalDurationMultiplierOffset(settlement, def) : 0f;
            float tierMultiplier = tier?.durationMultiplier ?? 1f;
            float workerMultiplier = def.Worker.DurationMultiplierFor(request);
            float combined = tierMultiplier * workerMultiplier * Mathf.Max(0.1f, 1f + specialtyOffset);
            int baseDuration = def.Worker.BaseDurationTicksFor(request) ?? def.duration;
            if (baseDuration <= 0) return 0;
            return Mathf.Max(1, Mathf.RoundToInt(baseDuration * combined));
        }
    }
}

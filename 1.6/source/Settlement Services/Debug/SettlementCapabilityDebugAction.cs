using System;
using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Registry;
using Settlement_Services.Framework.Specialty;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Services.Crafting;
using Settlement_Services.UI;

namespace Settlement_Services.Debug
{
    public static class SettlementCapabilityDebugAction
    {
        [DebugAction("Settlement Services", "Log settlement capability + stock", actionType = DebugActionType.ToolWorld, allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        private static void LogSettlementCapability()
        {
            Settlement settlement = Find.WorldObjects.SettlementAt(GenWorld.MouseTile());
            if (settlement == null)
            {
                Logger.Message("No settlement under mouse.");
                return;
            }

            var specialtyLabels = SettlementSpecialtyService.GetSpecialties(settlement).Select(d => d.defName);
            Logger.Message($"{settlement.Name}: specialties = [{string.Join(", ", specialtyLabels)}]");
            Logger.Message($"  Faction tech level: {settlement.Faction?.def.techLevel}");

            foreach (ServiceCategoryDef category in DefDatabase<ServiceCategoryDef>.AllDefsListForReading)
            {
                float quality = SettlementSpecialtyService.TotalQualityOffset(settlement, category);
                float price = SettlementSpecialtyService.TotalPriceModifierPct(settlement, category);
                float duration = SettlementSpecialtyService.TotalDurationMultiplierOffset(settlement, category);
                if (quality == 0f && price == 0f && duration == 0f) continue;
                Logger.Message($"  {category.defName}: quality {quality:+0.00;-0.00}, price {price:+0%;-0%}, duration {duration:+0%;-0%}");
            }

            foreach (SettlementStockCategoryDef category in DefDatabase<SettlementStockCategoryDef>.AllDefsListForReading)
            {
                int available = SettlementStockService.GetAvailableStock(settlement, category);
                int capacity = SettlementStockService.EffectiveCapacity(settlement, category);
                Logger.Message($"  {category.defName}: {available}/{capacity}");

                foreach (SettlementStockItemReference reference in SettlementStockService.ItemsFor(settlement, category)
                             .OrderBy(r => r.thing.defName, StringComparer.Ordinal))
                {
                    SettlementStockEffectiveSettings settings = SettlementStockService.EffectiveSettings(settlement, reference);
                    Logger.Message($"    {reference.thing.defName}: tier={settings.selectedThreshold} capacity={settings.capacity} refreshAmount={settings.refreshAmount} refreshIntervalTicks={settings.refreshIntervalTicks}");
                }
            }

            Logger.Message($"  Service availability:");
            foreach (SettlementServiceDef def in SettlementServiceRegistry.AllValid.Where(d => ServiceCandidacyService.IsCandidate(d, settlement)))
            {
                bool unavailable = ServiceCandidacyService.TryGetUnavailableReason(def, settlement, out string reasonKey);
                string status = unavailable ? $"unavailable ({reasonKey.Translate()})" : "available";

                float price = SettlementSpecialtyService.TotalPriceModifierPct(settlement, def);
                float duration = SettlementSpecialtyService.TotalDurationMultiplierOffset(settlement, def);
                string modifiers = price == 0f && duration == 0f ? "" : $", price {price:+0%;-0%}, duration {duration:+0%;-0%}";
                Logger.Message($"    {def.defName}: {status}{modifiers}");
            }
        }

        [DebugAction("Settlement Services", "Log crafting recipe eligibility", actionType = DebugActionType.ToolWorld, allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        private static void LogCraftingRecipeEligibility()
        {
            Settlement settlement = Find.WorldObjects.SettlementAt(GenWorld.MouseTile());
            if (settlement == null)
            {
                Logger.Message("No settlement under mouse.");
                return;
            }

            CraftingRecipeReachability reachability = CraftingCommissionCatalog.ReachabilityFor(settlement);
            Logger.Message($"{settlement.Name} ({settlement.Faction?.def.defName}, techLevel {settlement.Faction?.def.techLevel}):");
            Logger.Message($"  reachable things = [{string.Join(", ", reachability.reachableThings.Select(t => t.defName).OrderBy(n => n, StringComparer.Ordinal))}]");

            foreach (CraftingCommissionRecipe recipe in CraftingRecipeDependencyIndex.AllIndexedEntries()
                         .OrderBy(r => r.identity.kind).ThenBy(r => r.identity.defName, StringComparer.Ordinal))
            {
                bool allowed = CraftingCommissionCatalog.IsAllowedAtSettlement(recipe, settlement);
                bool reachable = reachability.reachableRecipes.Contains(recipe.identity);
                if (allowed && reachable) continue;

                TechLevel ceiling = CraftingCommissionCatalog.EffectiveTechCeiling(recipe, settlement);
                Logger.Message($"  {recipe.identity} ({recipe.primaryOutput?.LabelCap}): allowed={allowed} (requestedTechLevel={recipe.techLevel}, ceiling={ceiling}), reachable={reachable}");
            }
        }
    }
}

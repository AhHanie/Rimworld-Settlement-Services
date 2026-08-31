using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Specialty;
using Settlement_Services.Framework.Stock;

namespace Settlement_Services.Services.Crafting
{
    public static class CraftingCommissionCatalog
    {
        public static bool IsStructurallyEligible(RecipeDef recipe) => CraftingRecipeDependencyIndex.IsStructurallyEligible(recipe);

        public static bool IsAllowedAtSettlement(CraftingCommissionRecipe recipe, Settlement settlement)
        {
            if (recipe?.primaryOutput == null || settlement?.Faction == null) return false;

            if (recipe.Kind == CraftingRecipeKind.Vanilla)
            {
                CraftingCommissionExtension ext = recipe.VanillaRecipeDef.GetModExtension<CraftingCommissionExtension>();
                if (ext?.commissionable == false) return false;
                if (ext?.commissionable == true) return true;
            }
            else if (!recipe.commissionable)
            {
                return false;
            }

            Faction faction = settlement.Faction;
            if (recipe.techLevel > EffectiveTechCeiling(recipe, settlement)) return false;

            if (!recipe.factionPrerequisiteTags.NullOrEmpty()
                && recipe.factionPrerequisiteTags.Any(tag => faction.def.recipePrerequisiteTags == null || !faction.def.recipePrerequisiteTags.Contains(tag)))
                return false;

            return true;
        }

        public static bool CanBeNestedProducer(CraftingCommissionRecipe recipe, Settlement settlement)
        {
            RecipeNode node = recipe != null ? CraftingRecipeDependencyIndex.NodeFor(recipe.identity) : null;
            if (node == null || !node.isNestedProducerCandidate) return false;
            return IsAllowedAtSettlement(recipe, settlement);
        }

        public static IEnumerable<CraftingCommissionRecipe> EligibleRecipes(Settlement settlement)
        {
            if (settlement?.Faction == null) yield break;

            CraftingRecipeReachability reachability = ReachabilityFor(settlement);
            foreach (CraftingCommissionRecipe recipe in CraftingRecipeDependencyIndex.AllIndexedEntries()
                         .OrderBy(r => r.label, StringComparer.Ordinal)
                         .ThenBy(r => r.identity.kind)
                         .ThenBy(r => r.identity.defName, StringComparer.Ordinal))
            {
                if (!reachability.reachableRecipes.Contains(recipe.identity)) continue;
                if (IsAllowedAtSettlement(recipe, settlement)) yield return recipe;
            }
        }

        public static bool IsEligible(CraftingCommissionRecipe recipe, Settlement settlement)
        {
            if (recipe == null || !IsAllowedAtSettlement(recipe, settlement)) return false;
            return ReachabilityFor(settlement).reachableRecipes.Contains(recipe.identity);
        }

        internal static CraftingRecipeReachability ReachabilityFor(Settlement settlement)
        {
            IEnumerable<ThingDef> stockedThings = SettlementStockService.AllStockedThingDefsFor(settlement)
                .Where(t => SettlementStockService.GetAvailableStock(settlement, t) > 0);
            return CraftingRecipeDependencyIndex.ReachableFrom(stockedThings);
        }

        internal static TechLevel EffectiveTechCeiling(CraftingCommissionRecipe recipe, Settlement settlement)
        {
            TechLevel baseline = settlement.Faction.def.techLevel;
            if (SettlementSpecialtyService.HasCapabilityTag(settlement, "EliteCrafting")) return RaiseOneStep(baseline);
            if (recipe.primaryOutput.IsWeapon && SettlementSpecialtyService.HasCapabilityTag(settlement, "WeaponCommission")) return RaiseOneStep(baseline);
            return baseline;
        }

        private static TechLevel RaiseOneStep(TechLevel level) => (TechLevel)Mathf.Min((int)level + 1, (int)TechLevel.Archotech);
    }
}

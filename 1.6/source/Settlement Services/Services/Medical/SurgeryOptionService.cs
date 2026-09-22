using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Stock;

namespace Settlement_Services.Services.Medical
{
    internal static class SurgeryOptionService
    {
        public const string ProstheticsCategoryDefName = "SettlementStock_ProstheticsAndBionics";

        public readonly struct ImplantOption
        {
            public readonly RecipeDef recipe;
            public readonly BodyPartRecord part;
            public readonly ThingDef itemDef;

            public ImplantOption(RecipeDef recipe, BodyPartRecord part, ThingDef itemDef)
            {
                this.recipe = recipe;
                this.part = part;
                this.itemDef = itemDef;
            }

            public string Key => $"{recipe.defName}|{part.Index}|{itemDef.defName}";
            public string Label => $"{recipe.LabelCap} ({part.LabelCap})";
        }

        private static SettlementStockCategoryDef ProstheticsCategory =>
            DefDatabase<SettlementStockCategoryDef>.GetNamedSilentFail(ProstheticsCategoryDefName);

        public static IEnumerable<ThingDef> ConfiguredThingDefs()
        {
            SettlementStockCategoryDef category = ProstheticsCategory;
            if (category == null) yield break;

            foreach (SettlementStockItemReference reference in SettlementStockCatalog.ItemsFor(category))
                yield return reference.thing;
        }

        private static bool IsSupportedImplantRecipe(RecipeDef recipe, ThingDef item)
        {
            if (recipe == null || item == null) return false;
            if (!recipe.IsSurgery || !recipe.targetsBodyPart || recipe.addsHediff == null) return false;

            IngredientCount fixedIngredient = null;
            foreach (IngredientCount ingredient in recipe.ingredients)
            {
                if (!ingredient.IsFixedIngredient) continue;
                if (fixedIngredient != null) return false;
                fixedIngredient = ingredient;
            }

            if (fixedIngredient == null) return false;
            if (fixedIngredient.FixedIngredient != item || fixedIngredient.GetBaseCount() != 1f) return false;

            return recipe.fixedIngredientFilter.Allows(item);
        }

        private static List<RecipeDef> FindSupportedInstallRecipes(ThingDef item)
        {
            var result = new List<RecipeDef>();
            if (item == null) return result;

            foreach (RecipeDef candidate in DefDatabase<RecipeDef>.AllDefsListForReading)
                if (IsSupportedImplantRecipe(candidate, item)) result.Add(candidate);
            return result;
        }

        private static bool TryGetLegacyInstallRecipe(ThingDef item, out RecipeDef recipe)
        {
            recipe = item != null ? DefDatabase<RecipeDef>.GetNamedSilentFail("Install" + item.defName) : null;
            return recipe != null;
        }

        private static IEnumerable<ImplantOption> OptionsForRecipeAndItem(Pawn pawn, RecipeDef recipe, ThingDef item)
        {
            if (!recipe.Worker.AvailableOnNow(pawn)) yield break;

            foreach (BodyPartRecord part in recipe.Worker.GetPartsToApplyOn(pawn, recipe))
            {
                if (!recipe.Worker.AvailableOnNow(pawn, part)) continue;
                yield return new ImplantOption(recipe, part, item);
            }
        }

        public static IEnumerable<ImplantOption> FindOptions(Pawn pawn, IEnumerable<ThingDef> candidateItems)
        {
            if (pawn == null || candidateItems == null) yield break;

            var configuredStock = new HashSet<ThingDef>(ConfiguredThingDefs());
            var seenKeys = new HashSet<string>();

            foreach (ThingDef item in candidateItems.Distinct())
            {
                List<RecipeDef> recipes = FindSupportedInstallRecipes(item);
                if (recipes.Count == 0 && configuredStock.Contains(item) && TryGetLegacyInstallRecipe(item, out RecipeDef legacyRecipe))
                    recipes = new List<RecipeDef> { legacyRecipe };

                foreach (RecipeDef recipe in recipes)
                    foreach (ImplantOption option in OptionsForRecipeAndItem(pawn, recipe, item))
                        if (seenKeys.Add(option.Key)) yield return option;
            }
        }

        public static List<ImplantOption> FindOfferedOptions(Pawn pawn, Settlement settlement, Caravan caravan)
        {
            var result = new List<ImplantOption>();
            if (pawn == null) return result;

            var candidateItems = new HashSet<ThingDef>();
            SettlementStockCategoryDef category = ProstheticsCategory;
            if (category != null)
                foreach (SettlementStockItemReference reference in SettlementStockService.ItemsFor(settlement, category))
                    candidateItems.Add(reference.thing);

            if (caravan != null)
                foreach (Thing thing in CaravanInventoryUtility.AllInventoryItems(caravan))
                    candidateItems.Add(thing.def);

            result.AddRange(FindOptions(pawn, candidateItems));
            result.Sort((a, b) =>
            {
                int labelCompare = string.Compare(a.Label, b.Label, StringComparison.Ordinal);
                if (labelCompare != 0) return labelCompare;
                int itemCompare = string.Compare(a.itemDef.defName, b.itemDef.defName, StringComparison.Ordinal);
                return itemCompare != 0 ? itemCompare : string.Compare(a.recipe.defName, b.recipe.defName, StringComparison.Ordinal);
            });
            return result;
        }

        private static ImplantOption? ResolveThreeFieldKey(Pawn pawn, string recipeDefName, string partIndexText, string itemDefName)
        {
            if (!int.TryParse(partIndexText, out int partIndex)) return null;

            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(recipeDefName);
            ThingDef item = DefDatabase<ThingDef>.GetNamedSilentFail(itemDefName);
            if (recipe == null || item == null) return null;

            bool supported = IsSupportedImplantRecipe(recipe, item);
            if (!supported)
            {
                supported = ConfiguredThingDefs().Contains(item)
                    && TryGetLegacyInstallRecipe(item, out RecipeDef legacyRecipe)
                    && legacyRecipe == recipe;
            }
            if (!supported) return null;

            foreach (ImplantOption option in OptionsForRecipeAndItem(pawn, recipe, item))
                if (option.part.Index == partIndex) return option;
            return null;
        }

        private static ImplantOption? ResolveLegacyTwoFieldKey(Pawn pawn, string recipeDefName, string partIndexText)
        {
            if (!int.TryParse(partIndexText, out int partIndex)) return null;

            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(recipeDefName);
            if (recipe == null) return null;

            var matches = new List<ImplantOption>();
            foreach (ThingDef stockItem in ConfiguredThingDefs())
            {
                if (!TryGetLegacyInstallRecipe(stockItem, out RecipeDef legacyRecipe) || legacyRecipe != recipe) continue;

                foreach (ImplantOption option in OptionsForRecipeAndItem(pawn, legacyRecipe, stockItem))
                    if (option.part.Index == partIndex) matches.Add(option);
            }
            return matches.Count == 1 ? matches[0] : (ImplantOption?)null;
        }

        private static ImplantOption? ResolveKey(Pawn pawn, string key)
        {
            if (pawn == null || string.IsNullOrEmpty(key)) return null;

            string[] parts = key.Split('|');
            if (parts.Length == 3) return ResolveThreeFieldKey(pawn, parts[0], parts[1], parts[2]);
            if (parts.Length == 2) return ResolveLegacyTwoFieldKey(pawn, parts[0], parts[1]);
            return null;
        }

        public static ImplantOption? FindByKey(Pawn pawn, string key) => ResolveKey(pawn, key);

        public static bool ConflictsWith(ImplantOption a, ImplantOption b)
        {
            if (a.part.Index == b.part.Index) return true;
            return IsAncestorOf(a.part, b.part) || IsAncestorOf(b.part, a.part);
        }

        private static bool IsAncestorOf(BodyPartRecord candidateAncestor, BodyPartRecord part)
        {
            for (BodyPartRecord current = part.parent; current != null; current = current.parent)
                if (current.Index == candidateAncestor.Index) return true;
            return false;
        }

        public static List<string> ConflictingKeysFor(ImplantOption option, List<ImplantOption> allOptions) =>
            allOptions.Where(o => o.Key != option.Key && ConflictsWith(option, o)).Select(o => o.Key).ToList();

        public static List<ImplantOption> ResolveAvailable(Pawn pawn, IReadOnlyList<string> keys)
        {
            var result = new List<ImplantOption>();
            if (pawn == null || keys == null || keys.Count == 0) return result;

            foreach (string key in keys)
            {
                ImplantOption? option = ResolveKey(pawn, key);
                if (option != null) result.Add(option.Value);
            }
            return result;
        }

        public static bool TryResolveSelected(Pawn pawn, IReadOnlyList<string> keys, out List<ImplantOption> resolved, out string errorKey)
        {
            resolved = new List<ImplantOption>();
            errorKey = null;
            if (pawn == null || keys == null || keys.Count == 0) return true;

            var seenKeys = new HashSet<string>();

            foreach (string key in keys)
            {
                if (!seenKeys.Add(key)) { errorKey = "SettlementServices.Error.ConflictingSurgeries"; return false; }

                ImplantOption? match = ResolveKey(pawn, key);
                if (match == null) { errorKey = "SettlementServices.Error.NoCompatibleImplants"; return false; }
                resolved.Add(match.Value);
            }

            for (int i = 0; i < resolved.Count; i++)
                for (int j = i + 1; j < resolved.Count; j++)
                    if (ConflictsWith(resolved[i], resolved[j])) { errorKey = "SettlementServices.Error.ConflictingSurgeries"; return false; }

            return true;
        }
    }
}

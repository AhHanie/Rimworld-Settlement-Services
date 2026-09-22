using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
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

        private static bool TryGetSupportedImplantItem(RecipeDef recipe, out ThingDef item)
        {
            item = null;
            if (recipe == null) return false;
            if (!recipe.IsSurgery || !recipe.targetsBodyPart || recipe.addsHediff == null) return false;

            IngredientCount fixedIngredient = null;
            foreach (IngredientCount ingredient in recipe.ingredients)
            {
                if (!ingredient.IsFixedIngredient) continue;
                if (fixedIngredient != null) return false;
                fixedIngredient = ingredient;
            }

            if (fixedIngredient == null) return false;

            ThingDef candidateItem = fixedIngredient.FixedIngredient;
            if (candidateItem == null || fixedIngredient.GetBaseCount() != 1f) return false;
            if (!recipe.fixedIngredientFilter.Allows(candidateItem)) return false;

            item = candidateItem;
            return true;
        }

        private static bool IsSupportedImplantRecipe(RecipeDef recipe, ThingDef item) =>
            TryGetSupportedImplantItem(recipe, out ThingDef resolvedItem) && resolvedItem == item;

        private static Dictionary<ThingDef, List<RecipeDef>> _supportedInstallRecipeIndex;

        private static Dictionary<ThingDef, List<RecipeDef>> SupportedInstallRecipeIndex
        {
            get
            {
                if (_supportedInstallRecipeIndex == null)
                {
                    var index = new Dictionary<ThingDef, List<RecipeDef>>();
                    foreach (RecipeDef candidate in DefDatabase<RecipeDef>.AllDefsListForReading)
                    {
                        if (!TryGetSupportedImplantItem(candidate, out ThingDef item)) continue;

                        if (!index.TryGetValue(item, out List<RecipeDef> recipes))
                        {
                            recipes = new List<RecipeDef>();
                            index[item] = recipes;
                        }
                        recipes.Add(candidate);
                    }
                    _supportedInstallRecipeIndex = index;
                }
                return _supportedInstallRecipeIndex;
            }
        }

        private static IReadOnlyList<RecipeDef> FindSupportedInstallRecipes(ThingDef item)
        {
            if (item == null) return Array.Empty<RecipeDef>();
            return SupportedInstallRecipeIndex.TryGetValue(item, out List<RecipeDef> recipes)
                ? (IReadOnlyList<RecipeDef>)recipes
                : Array.Empty<RecipeDef>();
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
                IReadOnlyList<RecipeDef> recipes = FindSupportedInstallRecipes(item);
                if (recipes.Count == 0 && configuredStock.Contains(item) && TryGetLegacyInstallRecipe(item, out RecipeDef legacyRecipe))
                    recipes = new List<RecipeDef> { legacyRecipe };

                foreach (RecipeDef recipe in recipes)
                    foreach (ImplantOption option in OptionsForRecipeAndItem(pawn, recipe, item))
                        if (seenKeys.Add(option.Key)) yield return option;
            }
        }

        private static List<ImplantOption> ComputeOfferedOptions(Pawn pawn, Settlement settlement, Caravan caravan)
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

        private readonly struct OfferedOptionsCacheKey : IEquatable<OfferedOptionsCacheKey>
        {
            private readonly Pawn pawn;
            private readonly Settlement settlement;
            private readonly Caravan caravan;

            public OfferedOptionsCacheKey(Pawn pawn, Settlement settlement, Caravan caravan)
            {
                this.pawn = pawn;
                this.settlement = settlement;
                this.caravan = caravan;
            }

            public bool Equals(OfferedOptionsCacheKey other) =>
                pawn == other.pawn && settlement == other.settlement && caravan == other.caravan;

            public override bool Equals(object obj) => obj is OfferedOptionsCacheKey other && Equals(other);

            public override int GetHashCode()
            {
                int hash = 17;
                hash = hash * 31 + (pawn?.thingIDNumber ?? 0);
                hash = hash * 31 + (settlement?.ID ?? 0);
                hash = hash * 31 + (caravan?.ID ?? 0);
                return hash;
            }
        }

        private static int _cacheTick = int.MinValue;
        private static Dictionary<OfferedOptionsCacheKey, IReadOnlyList<ImplantOption>> _offeredOptionsCache;
        private static Dictionary<(OfferedOptionsCacheKey key, string groupKey), IReadOnlyList<ServiceDisplayOption>> _displayOptionsCache;

        private static bool TryEnterCacheScope()
        {
            if (Current.ProgramState != ProgramState.Playing) return false;

            int currentTick = Find.TickManager.TicksGame;
            if (currentTick != _cacheTick)
            {
                _cacheTick = currentTick;
                _offeredOptionsCache?.Clear();
                _displayOptionsCache?.Clear();
            }
            return true;
        }

        public static IReadOnlyList<ImplantOption> FindOfferedOptions(Pawn pawn, Settlement settlement, Caravan caravan)
        {
            if (pawn == null) return Array.Empty<ImplantOption>();
            if (!TryEnterCacheScope()) return ComputeOfferedOptions(pawn, settlement, caravan);

            var key = new OfferedOptionsCacheKey(pawn, settlement, caravan);
            if (_offeredOptionsCache == null)
                _offeredOptionsCache = new Dictionary<OfferedOptionsCacheKey, IReadOnlyList<ImplantOption>>();

            if (!_offeredOptionsCache.TryGetValue(key, out IReadOnlyList<ImplantOption> cached))
            {
                cached = ComputeOfferedOptions(pawn, settlement, caravan);
                _offeredOptionsCache[key] = cached;
            }
            return cached;
        }

        public static IReadOnlyList<ServiceDisplayOption> GetOfferedDisplayOptions(Pawn pawn, Settlement settlement, Caravan caravan, string groupKey)
        {
            IReadOnlyList<ImplantOption> offered = FindOfferedOptions(pawn, settlement, caravan);
            if (offered.Count == 0) return Array.Empty<ServiceDisplayOption>();

            if (!TryEnterCacheScope()) return BuildDisplayOptions(offered, groupKey);

            var cacheKey = (new OfferedOptionsCacheKey(pawn, settlement, caravan), groupKey);
            if (_displayOptionsCache == null)
                _displayOptionsCache = new Dictionary<(OfferedOptionsCacheKey, string), IReadOnlyList<ServiceDisplayOption>>();

            if (!_displayOptionsCache.TryGetValue(cacheKey, out IReadOnlyList<ServiceDisplayOption> cached))
            {
                cached = BuildDisplayOptions(offered, groupKey);
                _displayOptionsCache[cacheKey] = cached;
            }
            return cached;
        }

        private static List<ServiceDisplayOption> BuildDisplayOptions(IReadOnlyList<ImplantOption> offered, string groupKey)
        {
            var result = new List<ServiceDisplayOption>(offered.Count);
            foreach (ImplantOption option in offered)
                result.Add(new ServiceDisplayOption
                {
                    key = option.Key,
                    label = option.Label,
                    groupKey = groupKey,
                    allowMultipleSelectionInGroup = true,
                    conflictingOptionKeys = ConflictingKeysFor(option, offered),
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

        public static List<string> ConflictingKeysFor(ImplantOption option, IReadOnlyList<ImplantOption> allOptions) =>
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

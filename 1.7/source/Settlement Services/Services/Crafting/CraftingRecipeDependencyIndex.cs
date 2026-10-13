using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Services.Crafting
{
    public class RecipeNode
    {
        public readonly CraftingCommissionRecipe recipe;
        public readonly ThingDef product;
        public readonly List<RecipeIngredientSlot> ingredientSlots;
        public readonly bool isNestedProducerCandidate;

        public RecipeNode(CraftingCommissionRecipe recipe, ThingDef product, List<RecipeIngredientSlot> ingredientSlots, bool isNestedProducerCandidate)
        {
            this.recipe = recipe;
            this.product = product;
            this.ingredientSlots = ingredientSlots;
            this.isNestedProducerCandidate = isNestedProducerCandidate;
        }
    }

    public class CraftingRecipeReachability
    {
        public readonly HashSet<CraftingRecipeIdentity> reachableRecipes;
        public readonly HashSet<ThingDef> reachableThings;

        public CraftingRecipeReachability(HashSet<CraftingRecipeIdentity> reachableRecipes, HashSet<ThingDef> reachableThings)
        {
            this.reachableRecipes = reachableRecipes;
            this.reachableThings = reachableThings;
        }
    }

    public static class CraftingRecipeDependencyIndex
    {
        private const int MaxCacheEntries = 64;

        private static Dictionary<CraftingRecipeIdentity, RecipeNode> nodesByIdentity;
        private static Dictionary<ThingDef, List<RecipeNode>> consumersOf;
        private static Dictionary<ThingDef, List<RecipeNode>> producersOf;
        private static List<CraftingCommissionRecipe> allIndexedRecipes;

        private static readonly Dictionary<string, CraftingRecipeReachability> reachabilityCache = new Dictionary<string, CraftingRecipeReachability>();

        public static bool IsStructurallyEligible(RecipeDef recipe)
        {
            if (recipe?.ProducedThingDef == null) return false;
            if (recipe.IsSurgery) return false;
            if (recipe.mechanitorOnlyRecipe || recipe.gestationCycles > 0) return false;
            if (recipe.fromIdeoBuildingPreceptOnly || !recipe.memePrerequisitesAny.NullOrEmpty()) return false;
            if (recipe.specialProducts != null) return false;

            ThingDef produced = recipe.ProducedThingDef;
            if (produced.race != null) return false;
            if (produced.category == ThingCategory.Building && !produced.Minifiable) return false;
            return true;
        }

        public static RecipeNode NodeFor(CraftingRecipeIdentity identity)
        {
            EnsureBuilt();
            return nodesByIdentity.TryGetValue(identity, out RecipeNode node) ? node : null;
        }

        public static IReadOnlyList<CraftingCommissionRecipe> AllIndexedEntries()
        {
            EnsureBuilt();
            return allIndexedRecipes;
        }

        public static IReadOnlyList<RecipeNode> ProducersOf(ThingDef thingDef)
        {
            EnsureBuilt();
            return thingDef != null && producersOf.TryGetValue(thingDef, out List<RecipeNode> nodes)
                ? nodes
                : (IReadOnlyList<RecipeNode>)Array.Empty<RecipeNode>();
        }

        public static CraftingRecipeReachability ReachableFrom(IEnumerable<ThingDef> availableThings)
        {
            EnsureBuilt();

            List<string> signatureNames = availableThings.Select(t => t.defName).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
            string signature = string.Join("|", signatureNames);
            if (reachabilityCache.TryGetValue(signature, out CraftingRecipeReachability cached)) return cached;
            if (reachabilityCache.Count >= MaxCacheEntries) reachabilityCache.Clear();

            var reachableThings = new HashSet<ThingDef>();
            var reachableRecipes = new HashSet<CraftingRecipeIdentity>();
            var queue = new Queue<ThingDef>();

            foreach (string name in signatureNames)
            {
                ThingDef thing = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                if (thing == null || !reachableThings.Add(thing)) continue;
                queue.Enqueue(thing);
            }

            while (queue.Count > 0)
            {
                ThingDef newlyReachable = queue.Dequeue();
                if (!consumersOf.TryGetValue(newlyReachable, out List<RecipeNode> candidateNodes)) continue;

                foreach (RecipeNode node in candidateNodes)
                {
                    if (reachableRecipes.Contains(node.recipe.identity)) continue;
                    if (!node.ingredientSlots.All(slot => slot.candidates.Any(reachableThings.Contains))) continue;

                    reachableRecipes.Add(node.recipe.identity);
                    if (node.isNestedProducerCandidate && node.product != null && reachableThings.Add(node.product))
                        queue.Enqueue(node.product);
                }
            }

            var result = new CraftingRecipeReachability(reachableRecipes, reachableThings);
            reachabilityCache[signature] = result;
            return result;
        }

        private static void EnsureBuilt()
        {
            if (nodesByIdentity != null) return;

            nodesByIdentity = new Dictionary<CraftingRecipeIdentity, RecipeNode>();
            consumersOf = new Dictionary<ThingDef, List<RecipeNode>>();
            producersOf = new Dictionary<ThingDef, List<RecipeNode>>();
            allIndexedRecipes = new List<CraftingCommissionRecipe>();

            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading.OrderBy(r => r.defName, StringComparer.Ordinal))
                IndexVanillaRecipe(recipe);

            foreach (CraftingCommissionRecipeDef def in DefDatabase<CraftingCommissionRecipeDef>.AllDefsListForReading.OrderBy(d => d.defName, StringComparer.Ordinal))
                IndexCustomRecipe(def);
        }

        private static void IndexVanillaRecipe(RecipeDef recipe)
        {
            if (recipe.ingredients.NullOrEmpty()) return;
            if (!IsStructurallyEligible(recipe)) return;

            var slots = new List<RecipeIngredientSlot>();
            foreach (IngredientCount ingredient in recipe.ingredients)
            {
                List<ThingDef> allowed = ingredient.filter?.AllowedThingDefs?.Where(t => ingredient.IsFixedIngredient || recipe.fixedIngredientFilter.Allows(t)).ToList();
                if (allowed == null || allowed.Count == 0) return;
                slots.Add(new RecipeIngredientSlot(allowed, ingredient, recipe));
            }

            CraftingCommissionRecipe descriptor = CraftingCommissionRecipe.ForVanilla(recipe, slots);
            bool isNestedCandidate = recipe.products != null && recipe.products.Count == 1
                && recipe.products[0].thingDef == recipe.ProducedThingDef && recipe.products[0].count == 1
                && !recipe.ProducedThingDef.MadeFromStuff;

            RegisterNode(descriptor, recipe.ProducedThingDef, slots, isNestedCandidate);
        }

        private static void IndexCustomRecipe(CraftingCommissionRecipeDef def)
        {
            List<string> errors = def.ConfigErrors().ToList();
            if (errors.Count > 0)
            {
                string label = def.defName ?? "<unnamed>";
                foreach (string error in errors)
                    Settlement_Services.SupportLog.Error($"CraftingCommissionRecipeDef {label}: {error}");
                return;
            }

            List<RecipeIngredientSlot> slots = def.inputs.Select(i => new RecipeIngredientSlot(i.thingDef, i.count)).ToList();
            CraftingCommissionRecipe descriptor = CraftingCommissionRecipe.ForCustom(def, slots);
            bool isNestedCandidate = def.output.count == 1 && !def.output.thingDef.MadeFromStuff;

            RegisterNode(descriptor, def.output.thingDef, slots, isNestedCandidate);
        }

        private static void RegisterNode(CraftingCommissionRecipe descriptor, ThingDef product, List<RecipeIngredientSlot> slots, bool isNestedCandidate)
        {
            var node = new RecipeNode(descriptor, product, slots, isNestedCandidate);
            nodesByIdentity[descriptor.identity] = node;
            allIndexedRecipes.Add(descriptor);

            foreach (RecipeIngredientSlot slot in slots)
                foreach (ThingDef candidate in slot.candidates)
                {
                    if (!consumersOf.TryGetValue(candidate, out List<RecipeNode> list))
                    {
                        list = new List<RecipeNode>();
                        consumersOf[candidate] = list;
                    }
                    if (!list.Contains(node)) list.Add(node);
                }

            if (isNestedCandidate)
            {
                if (!producersOf.TryGetValue(product, out List<RecipeNode> producers))
                {
                    producers = new List<RecipeNode>();
                    producersOf[product] = producers;
                }
                producers.Add(node);
            }
        }
    }
}

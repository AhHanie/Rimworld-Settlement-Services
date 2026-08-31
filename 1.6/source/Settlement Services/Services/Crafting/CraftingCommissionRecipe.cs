using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Services.Crafting
{
    public enum CraftingRecipeKind
    {
        Vanilla,
        Custom,
    }

    public struct CraftingRecipeIdentity : IEquatable<CraftingRecipeIdentity>
    {
        public readonly CraftingRecipeKind kind;
        public readonly string defName;

        public CraftingRecipeIdentity(CraftingRecipeKind kind, string defName)
        {
            this.kind = kind;
            this.defName = defName;
        }

        public bool Equals(CraftingRecipeIdentity other) => kind == other.kind && defName == other.defName;
        public override bool Equals(object obj) => obj is CraftingRecipeIdentity other && Equals(other);
        public override int GetHashCode() => (kind, defName ?? string.Empty).GetHashCode();
        public override string ToString() => $"{kind}:{defName}";
    }

    public class RecipeIngredientSlot
    {
        public readonly List<ThingDef> candidates;

        private readonly IngredientCount vanillaIngredient;
        private readonly RecipeDef vanillaRecipe;
        private readonly int fixedCount;

        public RecipeIngredientSlot(List<ThingDef> candidates, IngredientCount vanillaIngredient, RecipeDef vanillaRecipe)
        {
            this.candidates = candidates;
            this.vanillaIngredient = vanillaIngredient;
            this.vanillaRecipe = vanillaRecipe;
        }

        public RecipeIngredientSlot(ThingDef fixedCandidate, int fixedCount)
        {
            candidates = new List<ThingDef> { fixedCandidate };
            this.fixedCount = fixedCount;
        }

        public bool IsStuffCandidate(ThingDef chosen) =>
            vanillaIngredient != null && chosen != null
            && vanillaRecipe.ProducedThingDef.MadeFromStuff && vanillaIngredient.filter.Allows(chosen);

        public int RequiredCountFor(ThingDef chosen) =>
            vanillaIngredient != null
                ? Mathf.Max(1, vanillaIngredient.CountRequiredOfFor(chosen, vanillaRecipe))
                : fixedCount;
    }

    public class CraftingCommissionRecipe
    {
        public readonly CraftingRecipeIdentity identity;
        public readonly string label;
        public readonly string description;
        public readonly ThingDef primaryOutput;
        public readonly List<ThingDefCountClass> products;
        public readonly List<RecipeIngredientSlot> ingredientSlots;
        public readonly TechLevel techLevel;
        public readonly List<string> factionPrerequisiteTags;
        public readonly bool commissionable;
        public readonly bool madeFromStuff;

        private readonly RecipeDef vanillaRecipe;
        private readonly float customWorkAmount;
        private readonly ThingDef fixedOutputStuff;

        private CraftingCommissionRecipe(CraftingRecipeIdentity identity, string label, string description, ThingDef primaryOutput,
            List<ThingDefCountClass> products, List<RecipeIngredientSlot> ingredientSlots, TechLevel techLevel,
            List<string> factionPrerequisiteTags, bool commissionable, bool madeFromStuff,
            RecipeDef vanillaRecipe, float customWorkAmount, ThingDef fixedOutputStuff)
        {
            this.identity = identity;
            this.label = label;
            this.description = description;
            this.primaryOutput = primaryOutput;
            this.products = products;
            this.ingredientSlots = ingredientSlots;
            this.techLevel = techLevel;
            this.factionPrerequisiteTags = factionPrerequisiteTags;
            this.commissionable = commissionable;
            this.madeFromStuff = madeFromStuff;
            this.vanillaRecipe = vanillaRecipe;
            this.customWorkAmount = customWorkAmount;
            this.fixedOutputStuff = fixedOutputStuff;
        }

        public static CraftingCommissionRecipe ForVanilla(RecipeDef recipe, List<RecipeIngredientSlot> ingredientSlots) =>
            new CraftingCommissionRecipe(
                new CraftingRecipeIdentity(CraftingRecipeKind.Vanilla, recipe.defName),
                recipe.LabelCap, recipe.description,
                recipe.ProducedThingDef,
                recipe.products,
                ingredientSlots,
                recipe.ProducedThingDef.techLevel,
                recipe.factionPrerequisiteTags,
                true,
                recipe.ProducedThingDef.MadeFromStuff,
                recipe, 0f, null);

        public static CraftingCommissionRecipe ForCustom(CraftingCommissionRecipeDef def, List<RecipeIngredientSlot> ingredientSlots)
        {
            bool madeFromStuff = def.output.thingDef.MadeFromStuff;
            return new CraftingCommissionRecipe(
                new CraftingRecipeIdentity(CraftingRecipeKind.Custom, def.defName),
                def.LabelCap, def.description,
                def.output.thingDef,
                new List<ThingDefCountClass> { def.output },
                ingredientSlots,
                def.techLevel,
                def.factionPrerequisiteTags,
                def.commissionable,
                madeFromStuff,
                null, def.workAmount, madeFromStuff ? def.output.stuff : null);
        }

        public CraftingRecipeKind Kind => identity.kind;
        public RecipeDef VanillaRecipeDef => vanillaRecipe;
        public ThingDef FixedOutputStuff => fixedOutputStuff;

        public float WorkAmountFor(ThingDef stuff) => vanillaRecipe != null ? vanillaRecipe.WorkAmountForStuff(stuff) : customWorkAmount;

        public int ProductCountPerExecution(ThingDef product) => products.Where(p => p.thingDef == product).Sum(p => p.count);

        public List<Thing> MakeProducts(int executions, ThingDef selectedStuff, int effectiveSkillLevel)
        {
            ThingDef stuffForPrimary = vanillaRecipe != null ? selectedStuff : fixedOutputStuff;
            var results = new List<Thing>();
            foreach (ThingDefCountClass productSpec in products)
            {
                ThingDef productStuff = productSpec.thingDef == primaryOutput ? stuffForPrimary : null;
                results.AddRange(MakeProductStacks(productSpec.thingDef, productStuff, productSpec.count * executions, effectiveSkillLevel));
            }
            return results;
        }

        private static IEnumerable<Thing> MakeProductStacks(ThingDef thingDef, ThingDef stuff, int count, int effectiveSkillLevel)
        {
            int remaining = Mathf.Max(1, count);
            while (remaining > 0)
            {
                int stackCount = Mathf.Min(remaining, Mathf.Max(1, thingDef.stackLimit));
                Thing thing = ThingMaker.MakeThing(thingDef, stuff);
                thing.stackCount = stackCount;

                CompQuality compQuality = thing.TryGetComp<CompQuality>();
                if (compQuality != null)
                {
                    QualityCategory quality = QualityUtility.GenerateQualityCreatedByPawn(effectiveSkillLevel, inspired: false);
                    compQuality.SetQuality(quality, ArtGenerationContext.Outsider);
                }

                yield return thing;
                remaining -= stackCount;
            }
        }
    }
}

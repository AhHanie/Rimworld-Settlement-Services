using UnityEngine;
using Verse;
using Settlement_Services.Services.Crafting;

namespace Settlement_Services.Framework.Dto
{
    public class CraftingCommissionLine : IExposable
    {
        public CraftingRecipeKind recipeKind;
        public string recipeDefName;
        public int count = 1;
        public string stuffDefName;

        public CraftingCommissionLine()
        {
        }

        public CraftingCommissionLine(CraftingRecipeKind recipeKind, string recipeDefName, int count, string stuffDefName)
        {
            this.recipeKind = recipeKind;
            this.recipeDefName = recipeDefName;
            this.count = count;
            this.stuffDefName = stuffDefName;
        }

        public CraftingRecipeIdentity Identity => new CraftingRecipeIdentity(recipeKind, recipeDefName);

        public CraftingCommissionLine Clone() => new CraftingCommissionLine(recipeKind, recipeDefName, count, stuffDefName);

        public void ExposeData()
        {
            Scribe_Values.Look(ref recipeKind, "recipeKind");
            Scribe_Values.Look(ref recipeDefName, "recipeDefName");
            Scribe_Values.Look(ref count, "count", 1);
            Scribe_Values.Look(ref stuffDefName, "stuffDefName");

            if (Scribe.mode == LoadSaveMode.PostLoadInit) count = Mathf.Max(1, count);
        }
    }
}

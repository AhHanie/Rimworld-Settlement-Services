using Verse;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.Services.Crafting
{
    internal static class CraftingProductionPlanValidator
    {
        private const string UnavailableKey = "SettlementServices.Error.CommissionedItemNoLongerAvailable";

        public static bool Validate(CraftingProductionPlan plan, out string errorKey)
        {
            if (plan == null)
            {
                errorKey = UnavailableKey;
                return false;
            }

            foreach (CraftingProductionOperation op in plan.operations)
            {
                if (IsOperationValid(op)) continue;
                errorKey = UnavailableKey;
                return false;
            }

            errorKey = null;
            return true;
        }

        private static bool IsOperationValid(CraftingProductionOperation op)
        {
            RecipeNode node = CraftingRecipeDependencyIndex.NodeFor(op.Identity);
            CraftingCommissionRecipe recipe = node?.recipe;
            if (recipe?.primaryOutput == null) return false;

            if (recipe.Kind == CraftingRecipeKind.Vanilla)
                return op.stuffDefName == null || DefDatabase<ThingDef>.GetNamedSilentFail(op.stuffDefName) != null;

            if (op.stuffDefName != null) return false;
            return !recipe.madeFromStuff || recipe.FixedOutputStuff != null;
        }
    }
}

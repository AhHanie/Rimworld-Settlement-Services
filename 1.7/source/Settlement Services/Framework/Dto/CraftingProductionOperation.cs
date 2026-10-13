using Verse;
using Settlement_Services.Services.Crafting;

namespace Settlement_Services.Framework.Dto
{
    public class CraftingProductionOperation : IExposable
    {
        public CraftingRecipeKind recipeKind;
        public string recipeDefName;
        public string stuffDefName;
        public int executions = 1;
        public bool producesFinalResult;

        public CraftingProductionOperation()
        {
        }

        public CraftingProductionOperation(CraftingRecipeKind recipeKind, string recipeDefName, string stuffDefName, int executions, bool producesFinalResult)
        {
            this.recipeKind = recipeKind;
            this.recipeDefName = recipeDefName;
            this.stuffDefName = stuffDefName;
            this.executions = executions;
            this.producesFinalResult = producesFinalResult;
        }

        public CraftingRecipeIdentity Identity => new CraftingRecipeIdentity(recipeKind, recipeDefName);

        public CraftingProductionOperation Clone() => new CraftingProductionOperation(recipeKind, recipeDefName, stuffDefName, executions, producesFinalResult);

        public void ExposeData()
        {
            Scribe_Values.Look(ref recipeKind, "recipeKind");
            Scribe_Values.Look(ref recipeDefName, "recipeDefName");
            Scribe_Values.Look(ref stuffDefName, "stuffDefName");
            Scribe_Values.Look(ref executions, "executions", 1);
            Scribe_Values.Look(ref producesFinalResult, "producesFinalResult");
        }
    }
}

using Verse;

namespace Settlement_Services.Framework.Dto
{
    public class CraftingMaterialRequirement : IExposable
    {
        public string thingDefName;
        public int amount;
        public bool playerCanSupply = true;

        public CraftingMaterialRequirement()
        {
        }

        public CraftingMaterialRequirement(string thingDefName, int amount, bool playerCanSupply)
        {
            this.thingDefName = thingDefName;
            this.amount = amount;
            this.playerCanSupply = playerCanSupply;
        }

        public CraftingMaterialRequirement Clone() => new CraftingMaterialRequirement(thingDefName, amount, playerCanSupply);

        public void ExposeData()
        {
            Scribe_Values.Look(ref thingDefName, "thingDefName");
            Scribe_Values.Look(ref amount, "amount");
            Scribe_Values.Look(ref playerCanSupply, "playerCanSupply", true);
        }
    }
}

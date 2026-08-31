namespace Settlement_Services.Framework.Defs
{
    public class ServiceStockRequirement
    {
        public string stockCategoryDefName;
        public string thingDefName;
        public string preferredThingDefName;
        public int amount = 1;
        public float nutritionRequired = 0f;
        public bool playerCanSupply = true;

        public bool IsExact => !string.IsNullOrEmpty(thingDefName);

        public ServiceStockRequirement Clone() => new ServiceStockRequirement
        {
            stockCategoryDefName = stockCategoryDefName,
            thingDefName = thingDefName,
            preferredThingDefName = preferredThingDefName,
            amount = amount,
            nutritionRequired = nutritionRequired,
            playerCanSupply = playerCanSupply,
        };
    }
}

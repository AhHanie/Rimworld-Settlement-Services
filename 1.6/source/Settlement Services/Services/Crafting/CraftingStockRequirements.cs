using System.Collections.Generic;
using System.Linq;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.Services.Crafting
{
    internal static class CraftingStockRequirements
    {
        public static List<ServiceStockRequirement> ToStockRequirements(CraftingProductionPlan plan) =>
            plan.rawMaterials
                .Select(m => new ServiceStockRequirement { thingDefName = m.thingDefName, amount = m.amount, playerCanSupply = m.playerCanSupply })
                .ToList();

        public static ServiceStockRequirement ToBlockingRequirement(CraftingMaterialRequirement blocking, List<ThingDefCountClass> playerSuppliedInputs)
        {
            int alreadySupplied = playerSuppliedInputs.Where(i => i.thingDef?.defName == blocking.thingDefName).Sum(i => i.count);
            int grossAmount = blocking.amount + alreadySupplied;
            return new ServiceStockRequirement { thingDefName = blocking.thingDefName, amount = grossAmount, playerCanSupply = blocking.playerCanSupply };
        }
    }
}

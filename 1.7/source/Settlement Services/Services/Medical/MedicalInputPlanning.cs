using System.Collections.Generic;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Medical
{
    internal static class MedicalInputPlanning
    {
        public static ServiceInputPlan PlanFromCategory(SettlementServiceRequest request, string stockCategoryDefName, int amount, string preferredThingDefName = "MedicineIndustrial")
        {
            var plan = new ServiceInputPlan();
            if (amount <= 0) return plan;

            var requirement = new ServiceStockRequirement { stockCategoryDefName = stockCategoryDefName, preferredThingDefName = preferredThingDefName, amount = amount, playerCanSupply = true };
            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, new List<ServiceStockRequirement> { requirement }, request.playerSuppliedInputs);
            if (!allocation.Success) return plan;

            plan.stockConsumed.AddRange(allocation.SettlementSupplied);
            plan.playerSuppliedConsumed.AddRange(allocation.PlayerSupplied);
            return plan;
        }
    }
}

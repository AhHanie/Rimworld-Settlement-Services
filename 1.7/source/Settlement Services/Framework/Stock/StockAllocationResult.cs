using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Framework.Stock
{
    public class StockAllocationResult
    {
        public bool Success { get; private set; }
        public string ErrorKey { get; private set; }
        public List<ThingDefCountClass> SettlementSupplied { get; private set; } = new List<ThingDefCountClass>();
        public List<ThingDefCountClass> PlayerSupplied { get; private set; } = new List<ThingDefCountClass>();

        public static StockAllocationResult Fail(string errorKey) => new StockAllocationResult { Success = false, ErrorKey = errorKey };

        public static StockAllocationResult Ok(List<ThingDefCountClass> settlementSupplied, List<ThingDefCountClass> playerSupplied) =>
            new StockAllocationResult { Success = true, SettlementSupplied = settlementSupplied, PlayerSupplied = playerSupplied };
    }
}

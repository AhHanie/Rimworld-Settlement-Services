using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Stock;

namespace Settlement_Services.Services.Mechanitor
{
    public static class MechanoidHiringStock
    {
        public const int BaseCapacity = 2;
        public const int BaseRefreshAmount = 1;
        public const int RefreshIntervalTicks = GenDate.TicksPerDay * 10;

        public static int Capacity => SettlementStockService.ToQuantity((float)BaseCapacity * SettlementStockService.GlobalQuantityMultiplier);

        public static int RefreshAmount => SettlementStockService.ToQuantity((float)BaseRefreshAmount * SettlementStockService.GlobalQuantityMultiplier);

        private const string StockKeyPrefix = "SettlementServices.MechHireStock:";

        public static string StockKeyFor(MechanoidHiringEntry entry) => StockKeyPrefix + entry.raceDef.defName;

        public static int Available(Settlement settlement, MechanoidHiringEntry entry)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            string key = StockKeyFor(entry);
            int current = domain.CatchUpStock(settlement.ID, key, Capacity, RefreshAmount, RefreshIntervalTicks);
            int reserved = domain.TotalReserved(settlement.ID, key);
            return Mathf.Max(0, current - reserved);
        }

        public static bool AnyAvailable(Settlement settlement)
        {
            foreach (MechanoidHiringEntry entry in MechanoidHiringCatalog.AllEntries)
                if (Available(settlement, entry) > 0) return true;
            return false;
        }
    }
}

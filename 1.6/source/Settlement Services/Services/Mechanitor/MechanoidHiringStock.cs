using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Settlement_Services.Domain;

namespace Settlement_Services.Services.Mechanitor
{
    public static class MechanoidHiringStock
    {
        public const int Capacity = 2;
        public const int RefreshAmount = 1;
        public const int RefreshIntervalTicks = GenDate.TicksPerDay * 10;

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

using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Stock;

namespace Settlement_Services.Services.Hospitality
{
    internal static class SubstanceEligibilityService
    {
        public static IEnumerable<ThingDef> EligibleDrugs(Settlement settlement)
        {
            SettlementStockCategoryDef category = DefDatabase<SettlementStockCategoryDef>.GetNamedSilentFail("SettlementStock_Drugs");
            if (category == null) return Enumerable.Empty<ThingDef>();

            return SettlementStockService.ItemsFor(settlement, category)
                .Where(item => SettlementStockService.GetAvailableStock(settlement, item.thing) > 0)
                .Select(item => item.thing);
        }
    }
}

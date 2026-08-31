using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Stock;

namespace Settlement_Services.Services.Crafting
{
    internal static class CraftingMaterialPlanner
    {
        public static IEnumerable<ThingDef> EligibleStuffs(ThingDef producedThingDef, TechLevel effectiveTechCeiling, Settlement settlement)
        {
            if (producedThingDef == null || !producedThingDef.MadeFromStuff) yield break;

            foreach (ThingDef stuff in GenStuff.AllowedStuffsFor(producedThingDef, effectiveTechCeiling, checkAllowedInStuffGeneration: true))
            {
                SettlementStockItemReference reference = SettlementStockCatalog.ItemFor(stuff);
                if (SettlementStockService.IsEligibleForSettlement(settlement, reference)) yield return stuff;
            }
        }
    }
}

using System;
using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Stock;

namespace Settlement_Services.Debug
{
    public static class StockRefreshDebugAction
    {
        [DebugAction("Settlement Services", "Force settlement stock refresh", actionType = DebugActionType.ToolWorld, allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        private static void ForceSettlementStockRefresh()
        {
            Settlement settlement = Find.WorldObjects.SettlementAt(GenWorld.MouseTile());
            if (settlement == null)
            {
                Logger.Message("No settlement under mouse.");
                return;
            }
            if (settlement.Faction?.def == null)
            {
                Logger.Message($"{settlement.Name} has no faction, so it has no stock.");
                return;
            }

            SettlementServicesWorldComponent.Current.MarkStockRefreshDue(settlement);

            foreach (var thing in SettlementStockService.AllStockedThingDefsFor(settlement).ToList())
                SettlementStockService.GetAvailableStock(settlement, thing);

            Logger.Message($"{settlement.Name}: forced one stock refresh cycle (static stock and dynamic pools).");
            foreach (DynamicStockEntryView view in SettlementStockService.OfferedDynamicStock(settlement)
                         .OrderBy(v => v.poolKey, StringComparer.Ordinal)
                         .ThenBy(v => v.thingDefName, StringComparer.Ordinal))
                Logger.Message($"  {view.poolKey}: {view.thingDefName} {view.currentAmount}/{view.baseCapacity} (base capacity)");
        }
    }
}

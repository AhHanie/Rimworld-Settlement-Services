using HarmonyLib;
using RimWorld.Planet;
using Settlement_Services.Domain;

namespace Settlement_Services.Framework.SettlementLifecycle
{
    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.Destroy))]
    internal static class WorldObject_Destroy_ServiceRecoveryPatch
    {
        private static void Postfix(WorldObject __instance)
        {
            if (!(__instance is Settlement settlement)) return;

            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null) return;

            SettlementServiceOrchestrator.HandleSettlementDestroyed(domain, settlement.ID, settlement.Tile);
        }
    }
}

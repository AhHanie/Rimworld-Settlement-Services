using HarmonyLib;
using RimWorld;
using Settlement_Services.Domain;

namespace Settlement_Services.Framework.Custody
{
    [HarmonyPatch(typeof(GameEnder), nameof(GameEnder.CheckOrUpdateGameOver))]
    internal static class GameEnder_CustodyHeldColonistPatch
    {
        private static void Postfix(GameEnder __instance)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null) return;

            if (domain.HasCustodyHeldFreeColonist()) __instance.gameEnding = false;
        }
    }
}

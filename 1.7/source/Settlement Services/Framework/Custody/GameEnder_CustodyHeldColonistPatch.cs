using HarmonyLib;
using RimWorld;
using Settlement_Services.Domain;

namespace Settlement_Services.Framework.Custody
{
    [HarmonyPatch(typeof(GameEnder), nameof(GameEnder.CheckOrUpdateGameOver))]
    internal static class GameEnder_CustodyHeldColonistPatch
    {
        [HarmonyInfix(typeof(QuestUtility), nameof(QuestUtility.TotalBorrowedColonistCount))]
        private static int Postfix(int __result, [HarmonyOuter] GameEnder __instance)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null) return __result;

            if (!domain.HasCustodyHeldFreeColonist() && !domain.HasBoardHeldFreeColonist()) return __result;

            __instance.gameEnding = false;
            return __result > 0 ? __result : 1;
        }
    }
}

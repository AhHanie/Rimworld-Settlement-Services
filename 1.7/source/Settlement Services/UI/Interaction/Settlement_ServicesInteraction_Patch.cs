using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Services.Land;
using Verse;

namespace Settlement_Services.UI.Interaction
{
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.GetGizmos))]
    internal static class Settlement_GetGizmos_SettlementLandPatch
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Settlement __instance)
        {
            foreach (Gizmo gizmo in __result) yield return gizmo;
            foreach (Gizmo gizmo in LandPlotCommands.AtSettlement(__instance)) yield return gizmo;
        }
    }

    [HarmonyPatch(typeof(Settlement), nameof(Settlement.GetCaravanGizmos))]
    internal static class Settlement_GetCaravanGizmos_ServicesPatch
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Settlement __instance, Caravan caravan)
        {
            foreach (Gizmo gizmo in __result) yield return gizmo;
            foreach (Gizmo gizmo in SettlementServicesInteractionCommands.GetVisitedCaravanGizmos(__instance, caravan)) yield return gizmo;
        }
    }

    [HarmonyPatch(typeof(Settlement), nameof(Settlement.GetFloatMenuOptions))]
    internal static class Settlement_GetFloatMenuOptions_ServicesPatch
    {
        private static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> __result, Settlement __instance, Caravan caravan)
        {
            foreach (FloatMenuOption option in __result) yield return option;
            foreach (FloatMenuOption option in SettlementServicesInteractionCommands.GetVisitFloatMenuOptions(__instance, caravan)) yield return option;
        }
    }
}

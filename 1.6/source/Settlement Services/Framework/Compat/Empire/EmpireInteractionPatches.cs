using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.UI.Interaction;

namespace Settlement_Services.Framework.Compat.Empire
{
    internal static class EmpireInteractionPatches
    {
        internal static IEnumerable<Gizmo> GizmosPostfix(IEnumerable<Gizmo> __result, Settlement __instance, Caravan caravan)
        {
            foreach (Gizmo gizmo in __result) yield return gizmo;
            foreach (Gizmo gizmo in SettlementServicesInteractionCommands.GetVisitedCaravanGizmos(__instance, caravan)) yield return gizmo;
        }

        internal static IEnumerable<FloatMenuOption> FloatMenuPostfix(IEnumerable<FloatMenuOption> __result, Settlement __instance, Caravan caravan)
        {
            foreach (FloatMenuOption option in __result) yield return option;
            foreach (FloatMenuOption option in SettlementServicesInteractionCommands.GetVisitFloatMenuOptions(__instance, caravan)) yield return option;
        }
    }
}

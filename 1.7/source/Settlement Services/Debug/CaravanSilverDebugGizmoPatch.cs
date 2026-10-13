using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Debug
{
    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetGizmos))]
    internal static class CaravanSilverDebugGizmoPatch
    {
        private const int SilverPerClick = 2000;

        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Caravan __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            if (!DebugSettings.ShowDevGizmos || !__instance.IsPlayerControlled)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "DEV: Add 2,000 silver",
                action = () => AddSilver(__instance)
            };
        }

        private static void AddSilver(Caravan caravan)
        {
            int remaining = SilverPerClick;
            while (remaining > 0)
            {
                int stackCount = Math.Min(remaining, ThingDefOf.Silver.stackLimit);
                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = stackCount;
                CaravanInventoryUtility.GiveThing(caravan, silver);
                remaining -= stackCount;
            }
        }
    }
}

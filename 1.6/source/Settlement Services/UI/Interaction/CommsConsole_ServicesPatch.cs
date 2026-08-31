using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Validation;

namespace Settlement_Services.UI.Interaction
{
    [HarmonyPatch(typeof(Building), nameof(Building.GetGizmos))]
    internal static class Building_GetGizmos_CommsConsolePatch
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Building __instance)
        {
            foreach (Gizmo gizmo in __result) yield return gizmo;
            if (!(__instance is Building_CommsConsole) || __instance.Faction != Faction.OfPlayer) yield break;
            // TODO: Re-enable remote settlement contact after remote requests have been tested.
            // yield return BuildContactCommand();
        }

        private static Command_Action BuildContactCommand()
        {
            var command = new Command_Action
            {
                defaultLabel = "SettlementServices.Command.ContactSettlement".Translate(),
                defaultDesc = "SettlementServices.Command.ContactSettlementDesc".Translate(),
                icon = ServiceUITextures.ServicesCommand,
                action = OpenSettlementPicker,
            };

            if (!CommsConsoleUtility.PlayerHasPoweredCommsConsole())
                command.Disable("SettlementServices.Error.RemoteRequiresCommsConsole".Translate());
            else if (!EligibleSettlements().Any())
                command.Disable("SettlementServices.Error.NoContactableSettlements".Translate());

            return command;
        }

        private static void OpenSettlementPicker()
        {
            List<FloatMenuOption> options = EligibleSettlements()
                .OrderBy(s => s.Faction.Name).ThenBy(s => s.LabelCap)
                .Select(s => new FloatMenuOption(
                    "SettlementServices.Label.ContactSettlementOption".Translate(s.LabelCap, s.Faction.Name),
                    () => Find.WindowStack.Add(new Dialog_SettlementServices(ServiceRequestSession.ForRemote(s, null)))))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static IEnumerable<Settlement> EligibleSettlements() =>
            Find.WorldObjects.Settlements.Where(s =>
                s.Faction != null && !s.Faction.IsPlayer && !s.Faction.temporary &&
                !s.Faction.HostileTo(Faction.OfPlayer) &&
                SettlementServiceValidator.RemoteRequiresIndustrialTech(s));
    }
}

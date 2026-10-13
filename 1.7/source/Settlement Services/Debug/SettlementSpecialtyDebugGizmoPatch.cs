using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Specialty;
using Settlement_Services.UI;

namespace Settlement_Services.Debug
{
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.GetGizmos))]
    internal static class SettlementSpecialtyDebugGizmoPatch
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Settlement __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }

            if (!DebugSettings.ShowDevGizmos || __instance.Faction == null || __instance.Faction.IsPlayer)
            {
                yield break;
            }

            yield return BuildCommand(__instance);
        }

        private static Command_Action BuildCommand(Settlement settlement)
        {
            var command = new Command_Action
            {
                defaultLabel = "DEV: Add specialty",
                action = () => OpenPicker(settlement),
            };

            if (!AddableSpecialties(settlement).Any())
                command.Disable("All eligible specialties are already assigned.");

            return command;
        }

        private static List<SettlementSpecialtyDef> AddableSpecialties(Settlement settlement)
        {
            IReadOnlyList<SettlementSpecialtyDef> current = SettlementSpecialtyService.GetSpecialties(settlement);
            return DefDatabase<SettlementSpecialtyDef>.AllDefsListForReading
                .Where(d => !d.disabled && !current.Any(c => c.defName == d.defName))
                .OrderBy(d => d.LabelCap.ToString())
                .ThenBy(d => d.defName)
                .ToList();
        }

        private static void OpenPicker(Settlement settlement)
        {
            List<FloatMenuOption> options = AddableSpecialties(settlement)
                .Select(specialty =>
                {
                    SettlementSpecialtyDef capturedSpecialty = specialty;
                    string tooltip = SettlementSpecialtyTooltip.Build(settlement, capturedSpecialty);
                    return new FloatMenuOption(
                        $"{capturedSpecialty.LabelCap} ({capturedSpecialty.defName})",
                        () => AddSpecialty(settlement, capturedSpecialty),
                        ServiceUITextures.Resolve(capturedSpecialty.iconTexPath),
                        Color.white,
                        mouseoverGuiAction: rect => TooltipHandler.TipRegion(rect, tooltip));
                })
                .ToList();

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void AddSpecialty(Settlement settlement, SettlementSpecialtyDef specialty)
        {
            if (SettlementSpecialtyService.TryAddSpecialty(settlement, specialty))
            {
                Messages.Message($"DEV: Added specialty {specialty.LabelCap} to {settlement.LabelCap}.", MessageTypeDefOf.PositiveEvent, historical: false);
            }
            else
            {
                Messages.Message($"DEV: Could not add specialty {specialty.LabelCap} to {settlement.LabelCap}.", MessageTypeDefOf.RejectInput, historical: false);
            }
        }
    }
}

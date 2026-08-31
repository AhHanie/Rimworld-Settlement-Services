using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework;

namespace Settlement_Services.UI.Interaction
{
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.GetCaravanGizmos))]
    internal static class Settlement_GetCaravanGizmos_ServicesPatch
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Settlement __instance, Caravan caravan)
        {
            foreach (Gizmo gizmo in __result) yield return gizmo;
            if (CaravanVisitUtility.SettlementVisitedNow(caravan) != __instance) yield break;
            yield return BuildServicesCommand(__instance, caravan);
            // TODO: Re-enable the investment gizmo after the investment flow has been tested.
            // yield return BuildInvestCommand(__instance, caravan);

            Command_Action collectCommand = BuildCollectCommand(__instance, caravan);
            if (collectCommand != null) yield return collectCommand;
        }

        private static Command_Action BuildCollectCommand(Settlement settlement, Caravan caravan)
        {
            List<ServiceJobRecord> awaitingCollection = SettlementServicesWorldComponent.Current
                .JobsForSettlement(settlement.ID)
                .Where(j => j.status == ServiceJobStatus.AwaitingCollection)
                .ToList();
            if (awaitingCollection.Count == 0) return null;

            return new Command_Action
            {
                defaultLabel = "SettlementServices.Command.Collect".Translate(),
                defaultDesc = "SettlementServices.Command.CollectDesc".Translate(),
                icon = ServiceUITextures.CollectCommand,
                action = () =>
                {
                    int collected = awaitingCollection.Count(j => SettlementServiceOrchestrator.CollectJob(j.jobId, caravan));
                    Messages.Message("SettlementServices.Message.JobsCollected".Translate(collected), MessageTypeDefOf.PositiveEvent, historical: false);
                },
            };
        }

        private static Command_Action BuildServicesCommand(Settlement settlement, Caravan caravan)
        {
            var command = new Command_Action
            {
                defaultLabel = "SettlementServices.Command.Services".Translate(),
                defaultDesc = "SettlementServices.Command.ServicesDesc".Translate(),
                icon = ServiceUITextures.ServicesCommand,
                action = () => Find.WindowStack.Add(new Dialog_SettlementServices(ServiceRequestSession.ForInPersonVisit(settlement, caravan))),
            };

            if (settlement.Faction != null && settlement.Faction.HostileTo(Faction.OfPlayer))
                command.Disable("SettlementServices.Error.FactionHostile".Translate());
            else if (BestCaravanPawnUtility.FindBestNegotiator(caravan) == null)
                command.Disable("SettlementServices.Command.NoNegotiator".Translate());

            return command;
        }

        private static Command_Action BuildInvestCommand(Settlement settlement, Caravan caravan)
        {
            var command = new Command_Action
            {
                defaultLabel = "SettlementServices.Command.Invest".Translate(),
                defaultDesc = "SettlementServices.Command.InvestDesc".Translate(),
                icon = ServiceUITextures.InvestCommand,
                action = () => Find.WindowStack.Add(new Dialog_SettlementInvestment(settlement, caravan)),
            };

            if (settlement.Faction != null && settlement.Faction.HostileTo(Faction.OfPlayer))
                command.Disable("SettlementServices.Error.FactionHostile".Translate());
            else if (BestCaravanPawnUtility.FindBestNegotiator(caravan) == null)
                command.Disable("SettlementServices.Command.NoNegotiator".Translate());

            return command;
        }
    }

    [HarmonyPatch(typeof(Settlement), nameof(Settlement.GetFloatMenuOptions))]
    internal static class Settlement_GetFloatMenuOptions_ServicesPatch
    {
        private static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> __result, Settlement __instance, Caravan caravan)
        {
            foreach (FloatMenuOption option in __result) yield return option;
            foreach (FloatMenuOption option in CaravanArrivalAction_VisitServices.GetFloatMenuOptions(caravan, __instance)) yield return option;
            // TODO: Re-enable the investment float-menu option after the investment flow has been fully tested.
            // foreach (FloatMenuOption option in CaravanArrivalAction_InvestInSettlement.GetFloatMenuOptions(caravan, __instance)) yield return option;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework;
using Settlement_Services.Framework.Board;
using Settlement_Services.Framework.Compatibility;
using Settlement_Services.Framework.Custody;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Registry;
using Settlement_Services.Services.Stashing;
using Settlement_Services.Services.Land;

namespace Settlement_Services.UI.Interaction
{
    internal static class SettlementServicesInteractionCommands
    {
        internal static IEnumerable<Gizmo> GetVisitedCaravanGizmos(Settlement settlement, Caravan caravan)
        {
            if (CaravanVisitUtility.SettlementVisitedNow(caravan) != settlement) yield break;

            yield return BuildServicesCommand(settlement, caravan);
            // TODO: Re-enable the investment gizmo after the investment flow has been tested.
            // yield return BuildInvestCommand(settlement, caravan);

            Command_Action collectCommand = BuildCollectCommand(settlement, caravan);
            if (collectCommand != null) yield return collectCommand;
            Command_Action stashRecovery = BuildStashRecoveryCommand(settlement, caravan);
            if (stashRecovery != null) yield return stashRecovery;
            Command_Action landRecovery = LandPlotCommands.AtVisitedSettlement(settlement, caravan);
            if (landRecovery != null) yield return landRecovery;
        }

        internal static IEnumerable<FloatMenuOption> GetVisitFloatMenuOptions(Settlement settlement, Caravan caravan)
        {
            foreach (FloatMenuOption option in CaravanArrivalAction_VisitServices.GetFloatMenuOptions(caravan, settlement)) yield return option;
            // TODO: Re-enable the investment float-menu option after the investment flow has been fully tested.
            // foreach (FloatMenuOption option in CaravanArrivalAction_InvestInSettlement.GetFloatMenuOptions(caravan, settlement)) yield return option;
        }

        private static Command_Action BuildCollectCommand(Settlement settlement, Caravan caravan)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            List<ServiceJobRecord> awaitingCollection = domain
                .JobsForSettlement(settlement.ID)
                .Where(j => j.status == ServiceJobStatus.AwaitingCollection)
                .ToList();
            List<BoardJobRecord> boardPickups = domain
                .BoardJobsForSettlement(settlement.ID)
                .Where(j => j.status == BoardJobStatus.ReturnPending && !j.pawnsReturned && j.assignedPawns.Any(p => p != null && !p.Destroyed && !p.Dead))
                .ToList();
            if (awaitingCollection.Count == 0 && boardPickups.Count == 0) return null;

            var heldPawns = new List<Pawn>();
            foreach (ServiceJobRecord job in awaitingCollection) heldPawns.AddRange(TargetCustodyService.HeldPawns(job));
            foreach (BoardJobRecord job in boardPickups) heldPawns.AddRange(job.assignedPawns.Where(p => p != null && !p.Destroyed && !p.Dead));
            heldPawns = heldPawns.Distinct().ToList();

            TaggedString description = "SettlementServices.Command.CollectDesc".Translate();
            if (heldPawns.Count > 0)
                description += "\n\n" + "SettlementServices.Command.CollectHeldPawns".Translate(heldPawns.Count, string.Join(", ", heldPawns.Select(p => p.LabelShortCap)));

            string firstBlockKey = null;
            bool anyCollectible = false;
            foreach (ServiceJobRecord job in awaitingCollection)
            {
                string key = SettlementServiceOrchestrator.GetCollectBlockKey(job, caravan);
                if (key == null) anyCollectible = true;
                else if (firstBlockKey == null) firstBlockKey = key;
            }
            foreach (BoardJobRecord job in boardPickups)
            {
                string key = BoardPawnCustodyService.GetCollectBlockKey(caravan, settlement.Tile, job.assignedPawns.Where(p => p != null && !p.Destroyed && !p.Dead).ToList());
                if (key == null) anyCollectible = true;
                else if (firstBlockKey == null) firstBlockKey = key;
            }

            var command = new Command_Action
            {
                defaultLabel = "SettlementServices.Command.Collect".Translate(),
                defaultDesc = description,
                icon = ServiceUITextures.CollectCommand,
                action = () =>
                {
                    int collected = awaitingCollection.Count(j => SettlementServiceOrchestrator.CollectJob(j.jobId, caravan));
                    foreach (BoardJobRecord boardJob in boardPickups)
                    {
                        if (BoardJobCoordinator.TryCollectWorkers(domain, boardJob, caravan, out _)) collected++;
                    }
                    Messages.Message("SettlementServices.Message.JobsCollected".Translate(collected), MessageTypeDefOf.PositiveEvent, historical: false);
                },
            };

            if (!anyCollectible && firstBlockKey != null) command.Disable(firstBlockKey.Translate());
            return command;
        }

        private static Command_Action BuildStashRecoveryCommand(Settlement settlement, Caravan caravan)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null) return null;
            domain.ExpireDueStashes();
            if (domain.StashesForSettlement(settlement.ID).Count == 0) return null;
            SettlementServiceDef stashDef = DefDatabase<SettlementServiceDef>.GetNamedSilentFail("SettlementService_Stash");
            if (SettlementServiceRegistry.IsEnabled(stashDef)) return null;
            var command = new Command_Action
            {
                defaultLabel = "SettlementServices.Command.StashRecovery".Translate(),
                defaultDesc = "SettlementServices.Command.StashRecoveryDesc".Translate(),
                icon = ServiceUITextures.CollectCommand,
                action = () => Find.WindowStack.Add(new Dialog_SettlementStash(settlement, caravan, true)),
            };
            string block = StashService.VisitBlock(settlement, caravan);
            if (block != null) command.Disable(block.Translate());
            return command;
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
            else if (!HasVisitingColonist(caravan))
                command.Disable("SettlementServices.Command.NoVisitingColonist".Translate());
            else if (SettlementServicesCompatibilityRegistry.GetRequestBlockReason(settlement) is string compatibilityBlockReason)
                command.Disable(compatibilityBlockReason.Translate());

            return command;
        }

        internal static bool HasVisitingColonist(Caravan caravan) =>
            caravan != null && caravan.PawnsListForReading.Any(p => p.IsFreeColonist);

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
}

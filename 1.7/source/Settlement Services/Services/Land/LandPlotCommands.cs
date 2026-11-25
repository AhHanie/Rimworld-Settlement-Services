using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Custody;
using Settlement_Services.Framework.Registry;
using Settlement_Services.UI;
using Verse;

namespace Settlement_Services.Services.Land
{
    internal static class LandPlotCommands
    {
        internal static IEnumerable<Gizmo> AtSettlement(Settlement host)
        {
            Map plotMap = ViewableMap(host);
            if (plotMap == null) yield break;
            yield return new Command_Action
            {
                defaultLabel = (LandPlotService.Key + "ViewPlot").Translate(),
                defaultDesc = (LandPlotService.Key + "ViewPlotDesc").Translate(),
                icon = ServiceUITextures.ViewMap,
                action = () =>
                {
                    Map map = ViewableMap(host);
                    if (map != null) CameraJumper.TryJump(map.Center, map);
                }
            };

            var depart = new Command_Action
            {
                defaultLabel = (LandPlotService.Key + "FormPlotCaravan").Translate(),
                defaultDesc = (LandPlotService.Key + "LeaveDesc").Translate(),
                icon = ServiceUITextures.LandExit,
                action = () =>
                {
                    Map map = ViewableMap(host);
                    if (map != null) OpenDeparture(map);
                }
            };
            if (SettlementLandMapParent.Frozen(plotMap))
                depart.Disable((LandPlotService.Key + "NoOccupants").Translate());
            yield return depart;
        }

        internal static Map ViewableMap(Settlement host)
        {
            if (!host.Spawned || host.Destroyed) return null;
            LandPlotRecord plot = SettlementServicesWorldComponent.Current?.LandPlotAtHost(host.ID);
            if (plot == null || plot.hostLost || plot.recoveryError || plot.Due(LandPlotPolicy.Now)) return null;
            return plot.Map is { Disposed: false } map ? map : null;
        }

        internal static Command_Action AtVisitedSettlement(Settlement host, Caravan caravan)
        {
            LandPlotRecord plot = SettlementServicesWorldComponent.Current?.LandPlotAtHost(host.ID);
            if (plot == null || (!plot.recoveryError && plot.Due(LandPlotPolicy.Now))) return null;
            if (SettlementServiceRegistry.IsEnabled(LandPlotService.Def) && LandPlotService.EligibleHost(host)
                && ServiceCandidacyService.IsCandidate(LandPlotService.Def, host)
                && LandPlotService.CanEnterNormally(plot, host, caravan)) return null;
            return RecoveryCommand(plot, caravan);
        }

        internal static IEnumerable<Gizmo> RecoveryGizmos(Caravan caravan)
        {
            var domain = SettlementServicesWorldComponent.Current;
            if (domain == null || !caravan.Spawned || !caravan.IsPlayerControlled || caravan.pather.Moving) yield break;
            Settlement visited = CaravanVisitUtility.SettlementVisitedNow(caravan);
            foreach (LandPlotRecord plot in domain.LandPlotsForVisit(caravan.Tile))
                if (visited?.ID != plot.hostId && domain.LandPlotAtHost(plot.hostId) == plot)
                    yield return RecoveryCommand(plot, caravan);
            foreach (var orphan in Find.World.pocketMaps.OfType<SettlementLandMapParent>())
            {
                if (orphan.Contract != null || LandPlotTravel.RecoveryTile(orphan.departureTile) != caravan.Tile
                    || orphan.Map == null) continue;
                var command = new Command_Action
                {
                    defaultLabel = (LandPlotService.Key + "RecoverOrphan").Translate(orphan.plotId),
                    defaultDesc = (LandPlotService.Key + "RecoverOrphanDesc").Translate(),
                    icon = ServiceUITextures.CollectCommand,
                    action = () =>
                    {
                        string error = LandPlotTravel.EnterOrphan(orphan, caravan);
                        if (error != null) LandPlotService.Reject(error);
                    }
                };
                string blocked = LandPlotService.CaravanBlock(caravan);
                if (blocked != null) command.Disable(blocked.Translate());
                yield return command;
            }
        }

        private static Command_Action RecoveryCommand(LandPlotRecord plot, Caravan caravan)
        {
            var command = new Command_Action
            {
                defaultLabel = (LandPlotService.Key + "ManageHost").Translate(plot.hostLabel),
                defaultDesc = (LandPlotService.Key + "ManageDesc").Translate(),
                icon = ServiceUITextures.CollectCommand,
                action = () =>
                {
                    if (!Find.WindowStack.IsOpen<Dialog_SettlementLand>())
                        Find.WindowStack.Add(new Dialog_SettlementLand(plot, caravan));
                }
            };
            string block = LandPlotService.CaravanBlock(caravan);
            if (block != null) command.Disable(block.Translate());
            return command;
        }

        internal static void OpenDeparture(Map map)
        {
            var parent = SettlementLandMapParent.For(map);
            if (parent == null || LandPlotPatches.IsPacking(map)) return;
            List<Pawn> occupants = SettlementLandMapParent.LivingPawns(map).ToList();
            if (occupants.Count == 0)
            {
                LandPlotService.Reject(LandPlotService.Key + "NoOccupants");
                return;
            }

            if (LandPlotPatches.CanPack && LandPlotTravel.RecoveryTile(parent.departureTile).Valid
                && CaravanOwnershipRules.HasCapableOwner(Dialog_FormCaravan.AllSendablePawns(map, true), Faction.OfPlayer))
            {
                Find.WindowStack.Add(new Dialog_FormCaravan(map, reform: true, mapAboutToBeRemoved: false));
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                (LandPlotService.Key + "EmergencyConfirm").Translate(), () =>
                {
                    if (parent.Map == map && !LandPlotTravel.Evacuate(parent))
                        LandPlotService.Reject(LandPlotService.Key + "EvacuationFailed");
                }));
        }
    }
}

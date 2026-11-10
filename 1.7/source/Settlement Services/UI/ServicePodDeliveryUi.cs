using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Delivery;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.UI
{
    internal static class ServicePodDeliveryUi
    {
        internal static bool IsOffered(ServiceRequestSession session, bool producesItems) =>
            ServicePodDeliveryPlanner.IsTechEligible(session.settlement)
            && (session.def.targetRule != Framework.Defs.ServiceTargetRule.None || producesItems);

        internal static void Draw(Listing_Standard listing, ServiceRequestSession session, bool producesItems, Window dialog)
        {
            if (!IsOffered(session, producesItems)) return;

            ServicePodDeliverySelection selection = session.podDelivery;
            List<Map> homeMaps = ServicePodDeliveryPlanner.PlayerHomeMaps();

            bool enabled = selection.enabled;
            bool previous = enabled;
            Rect toggleRect = listing.GetRect(Text.LineHeight);
            if (homeMaps.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(toggleRect, "SettlementServices.Label.PodDeliveryToggle".Translate() + ": " + ServicePodDeliveryPlanner.NoHomeMapKey.Translate());
                GUI.color = Color.white;
                return;
            }

            Widgets.CheckboxLabeled(toggleRect, "SettlementServices.Label.PodDeliveryToggle".Translate(), ref enabled);
            TooltipHandler.TipRegion(toggleRect, "SettlementServices.Label.PodDeliveryToggleDesc".Translate());
            selection.enabled = enabled;

            if (enabled && !previous)
            {
                if (homeMaps.Count == 1) selection.mapParentId = homeMaps[0].Parent.ID;
                else if (ServicePodDeliveryPlanner.ResolveMapParent(selection.mapParentId) == null) selection.mapParentId = -1;
            }

            if (!enabled) return;

            string destinationLabel = DestinationLabel(selection);
            Rect row = listing.GetRect(30f);
            Widgets.Label(new Rect(row.x, row.y + 4f, row.width - 190f, row.height), destinationLabel);
            if (Widgets.ButtonText(new Rect(row.xMax - 180f, row.y, 180f, 28f), "SettlementServices.Button.ChoosePodDestination".Translate()))
                BeginChoosingDestination(session, dialog, homeMaps);
        }

        internal static void DrawStats(Listing_Standard listing, ServiceRequestSession session, SettlementServiceQuote quote)
        {
            if (!session.podDelivery.enabled || quote == null || !quote.IsValid || quote.podCount <= 0) return;

            listing.Label("SettlementServices.Label.PodDeliveryStats".Translate(quote.podCount, quote.podMassBound.ToString("F0"), quote.podDistance, quote.podMaxRange));
        }

        private static string DestinationLabel(ServicePodDeliverySelection selection)
        {
            MapParent parent = ServicePodDeliveryPlanner.ResolveMapParent(selection.mapParentId);
            if (parent == null || !selection.cell.IsValid) return "SettlementServices.Label.PodDestinationNone".Translate();
            return "SettlementServices.Label.PodDestination".Translate(parent.LabelCap, selection.cell.x, selection.cell.z);
        }

        private static void BeginChoosingDestination(ServiceRequestSession session, Window dialog, List<Map> homeMaps)
        {
            if (homeMaps.Count == 1)
            {
                BeginCellTargeting(session, dialog, homeMaps[0]);
                return;
            }

            var options = homeMaps.Select(map => new FloatMenuOption(map.Parent.LabelCap, () => BeginCellTargeting(session, dialog, map))).ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void BeginCellTargeting(ServiceRequestSession session, Window dialog, Map map)
        {
            ServicePodDeliverySelection selection = session.podDelivery;
            selection.mapParentId = map.Parent.ID;

            Find.WindowStack.WindowOfType<Dialog_SettlementServices>()?.Close(doCloseSound: false);
            dialog.Close(doCloseSound: false);

            Current.Game.CurrentMap = map;
            CameraJumper.TryJump(new GlobalTargetInfo(map.Center, map));
            Find.TickManager.Pause();

            Find.Targeter.BeginTargeting(
                TargetingParameters.ForDropPodsDestination(),
                target =>
                {
                    selection.mapParentId = map.Parent.ID;
                    selection.cell = target.Cell;
                },
                actionWhenFinished: () =>
                {
                    Find.WindowStack.Add(new Dialog_SettlementServices(session.CreateTemplate()));
                    Find.WindowStack.Add(new Dialog_ServiceRequestDetail(session));
                });
        }
    }
}

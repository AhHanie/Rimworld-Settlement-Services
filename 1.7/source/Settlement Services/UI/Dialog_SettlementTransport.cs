using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Delivery;
using Settlement_Services.Services.Transport;
using UnityEngine;
using Verse;

namespace Settlement_Services.UI
{
    internal sealed class Dialog_SettlementTransport : Window
    {
        private const string Key = TransportBooking.Key;
        private const float RowHeight = 30f;
        private const string IconPath = "SettlementServices/UI/Icons/shuttle";
        private readonly TransportRequest request;
        private readonly Dialog_SettlementServices services;
        private readonly Dictionary<Thing, string> countBuffers = new Dictionary<Thing, string>();
        private TransportQuote displayedQuote;
        private TransportFare? displayedFare;
        private Vector2 passengerScroll;
        private Vector2 cargoScroll;
        private bool committing;

        public override Vector2 InitialSize => new Vector2(Mathf.Min(920f, Verse.UI.screenWidth), Mathf.Min(710f, Verse.UI.screenHeight));

        internal Dialog_SettlementTransport(Settlement provider, Caravan caravan, Dialog_SettlementServices services)
        {
            request = new TransportRequest(provider, caravan);
            this.services = services;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            closeOnAccept = false;
        }

        public override void DoWindowContents(Rect rect)
        {
            var domain = SettlementServicesWorldComponent.Current;
            if (domain == null) return;
            Texture2D icon = ContentFinder<Texture2D>.Get(IconPath, false);
            if (icon != null) GUI.DrawTexture(new Rect(0f, 0f, 34f, 34f), icon, ScaleMode.ScaleToFit);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(44f, 0f, rect.width - 65f, 34f), (Key + "Title").Translate(request.Provider.LabelCap));
            Text.Font = GameFont.Small;

            TechLevel tech = request.Provider.Faction?.def.techLevel ?? TechLevel.Undefined;
            DrawMode(new Rect(0f, 42f, 155f, 32f), TransportMode.Pod, tech);
            DrawMode(new Rect(163f, 42f, 155f, 32f), TransportMode.Shuttle, tech);
            string shuttleBlock = TransportRoute.ResolveSpecs(TransportMode.Shuttle, tech, out _);
            if (shuttleBlock != null) Widgets.Label(new Rect(328f, 43f, rect.width - 328f, 45f), shuttleBlock.Translate());

            string modeBlock = TransportRoute.ResolveSpecs(request.Mode, tech, out TransportSpecs specs);
            string providerBlock = TransportBooking.ProviderBlock(request.Provider, request.Caravan);
            Widgets.Label(new Rect(0f, 88f, rect.width - 193f, 30f), DestinationLabel());
            bool priorEnabled = GUI.enabled;
            GUI.enabled = priorEnabled && providerBlock == null && modeBlock == null;
            if (Widgets.ButtonText(new Rect(rect.width - 184f, 84f, 184f, 32f), (Key + "ChooseDestination").Translate()))
                new DestinationTargeting(this, specs, icon).Begin();
            GUI.enabled = priorEnabled;

            var direct = TransportBooking.DirectPassengers(request.Caravan).ToList();
            var cargo = TransportBooking.LooseInventory(direct, domain).ToList();
            float column = (rect.width - 12f) / 2f;
            float listHeight = rect.height - 302f;
            DrawPassengers(new Rect(0f, 124f, column, listHeight), direct, domain);
            DrawCargo(new Rect(column + 12f, 124f, column, listHeight), cargo);
            DrawQuote(new Rect(0f, rect.height - 170f, rect.width, 170f), specs, cargo);
        }

        private void DrawMode(Rect rect, TransportMode mode, TechLevel tech)
        {
            string error = TransportRoute.ResolveSpecs(mode, tech, out TransportSpecs specs);
            if (error != null) TooltipHandler.TipRegion(rect, error.Translate());
            bool selected = Widgets.RadioButtonLabeled(rect, (Key + (mode == TransportMode.Pod ? "Pod" : "Shuttle")).Translate(),
                request.Mode == mode, disabled: error != null || !GUI.enabled);
            if (!selected || request.Mode == mode) return;

            request.Mode = mode;
            displayedQuote = null;
            if (!request.Destination.Tile.Valid) return;
            Map map = TransportRoute.FindLocalDestination(request.Destination)?.Map;
            if (request.Destination.IsLocal && !TransportRoute.IsSafeCell(mode, specs.Hull, request.Destination.Cell, map))
                request.Destination = request.Destination with { Cell = IntVec3.Invalid };
        }

        private TaggedString DestinationLabel()
        {
            TransportDestination destination = request.Destination;
            if (!destination.Tile.Valid) return (Key + "NoDestination").Translate();
            if (destination.IsLocal)
            {
                MapParent parent = TransportRoute.FindLocalDestination(destination);
                if (parent != null && destination.Cell.IsValid)
                    return (Key + "MapDestination").Translate(parent.LabelCap, destination.Cell.x, destination.Cell.z);
                return (Key + "ChooseCellAgain").Translate();
            }
            Settlement settlement = Find.WorldObjects.WorldObjectAt<Settlement>(destination.Tile);
            return settlement == null ? (Key + "TileDestination").Translate(destination.Tile.tileId)
                : (Key + "SettlementDestination").Translate(settlement.LabelCap);
        }

        private void DrawPassengers(Rect rect, List<Pawn> direct, SettlementServicesWorldComponent domain)
        {
            Widgets.DrawMenuSection(rect);
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 5f, rect.width - 16f, RowHeight), (Key + "Passengers").Translate());
            List<Pawn> rows = direct.Where(p => TransportBooking.CanBookPassenger(p, domain) || request.Passengers.Contains(p))
                .Concat(request.Passengers.Where(p => !direct.Contains(p))).ToList();
            Rect scroll = new Rect(rect.x + 5f, rect.y + RowHeight + 4f, rect.width - 10f, rect.height - RowHeight - 8f);
            const float height = 49f;
            Rect view = new Rect(0f, 0f, scroll.width - 16f, Mathf.Max(scroll.height, height * rows.Count));
            Widgets.BeginScrollView(scroll, ref passengerScroll, view);
            for (int i = 0; i < rows.Count; i++)
            {
                Pawn pawn = rows[i];
                float y = i * height;
                Rect row = new Rect(3f, y, view.width - 6f, RowHeight);
                bool selected = request.Passengers.Contains(pawn);
                bool previous = selected;
                bool available = direct.Contains(pawn) && TransportBooking.CanBookPassenger(pawn, domain);
                Widgets.CheckboxLabeled(row, pawn.LabelShortCap, ref selected, disabled: !available && !selected);
                if (selected != previous)
                {
                    if (selected) request.Passengers.Add(pawn);
                    else request.Passengers.Remove(pawn);
                    displayedQuote = null;
                }
                Text.Font = GameFont.Tiny;
                Rect detail = new Rect(3f, y + 27f, view.width - 6f, 20f);
                List<Thing> gear = available ? TransportBooking.PersonalGear(pawn).ToList() : new List<Thing>();
                string subtitle = available ? (Key + "PassengerMass").Translate(TransportBooking.PersonalMass(pawn).ToString("0.##"), gear.Count).ToString()
                    : (Key + "SelectionUnavailable").Translate().ToString();
                Widgets.Label(detail, subtitle);
                if (gear.Count > 0) TooltipHandler.TipRegion(detail, gear.Select(t => t.LabelCap.ToString()).ToList().ToLineList());
                Text.Font = GameFont.Small;
            }
            if (rows.Count == 0) Widgets.Label(new Rect(3f, 0f, view.width - 6f, 45f), (Key + "NoPassengers").Translate());
            Widgets.EndScrollView();
        }

        private void DrawCargo(Rect rect, List<TransportStack> cargo)
        {
            Widgets.DrawMenuSection(rect);
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 5f, rect.width - 16f, RowHeight), (Key + "Cargo").Translate());
            List<TransportStack> rows = cargo.Concat(request.Cargo.Keys.Where(t => !cargo.Any(s => s.Item == t))
                .Select(t => new TransportStack(t, t.holdingOwner, 0))).ToList();
            Rect scroll = new Rect(rect.x + 5f, rect.y + RowHeight + 4f, rect.width - 10f, rect.height - RowHeight - 8f);
            const float height = 49f;
            Rect view = new Rect(0f, 0f, scroll.width - 16f, Mathf.Max(scroll.height, height * rows.Count));
            Widgets.BeginScrollView(scroll, ref cargoScroll, view);
            for (int i = 0; i < rows.Count; i++)
            {
                TransportStack stack = rows[i];
                Thing item = stack.Item;
                float y = i * height;
                Widgets.ThingIcon(new Rect(1f, y + 3f, 30f, 33f), item);
                Widgets.Label(new Rect(36f, y + 1f, view.width - 138f, 27f), item.LabelCapNoCount.Truncate(view.width - 139f));
                Text.Font = GameFont.Tiny;
                string carrier = (stack.Owner?.Owner as Pawn_InventoryTracker)?.pawn?.LabelShortCap.ToString() ?? string.Empty;
                Widgets.Label(new Rect(36f, y + 23f, view.width - 138f, 22f), stack.Count == 0 ? (Key + "SelectionUnavailable").Translate()
                    : (Key + "AvailableStack").Translate(stack.Count, carrier));
                Text.Font = GameFont.Small;

                request.Cargo.TryGetValue(item, out int oldCount);
                int count = oldCount;
                if (stack.Count > 0)
                {
                    if (!countBuffers.TryGetValue(item, out string buffer)) buffer = count.ToString();
                    Widgets.TextFieldNumeric(new Rect(view.width - 98f, y + 6f, 53f, 27f), ref count, ref buffer, 0f, Math.Max(count, stack.Count));
                    if (Widgets.ButtonText(new Rect(view.width - 43f, y + 6f, 41f, 27f), (Key + "All").Translate()))
                    { count = stack.Count; buffer = count.ToString(); }
                    countBuffers[item] = buffer;
                }
                else if (Widgets.ButtonText(new Rect(view.width - 98f, y + 6f, 96f, 27f), (Key + "Clear").Translate()))
                { count = 0; countBuffers.Remove(item); }
                if (count != oldCount)
                {
                    if (count == 0) request.Cargo.Remove(item);
                    else request.Cargo[item] = count;
                    displayedQuote = null;
                }
            }
            if (rows.Count == 0) Widgets.Label(new Rect(3f, 0f, view.width - 6f, 45f), (Key + "NoCargo").Translate());
            Widgets.EndScrollView();
        }

        private void DrawQuote(Rect rect, TransportSpecs specs, List<TransportStack> cargo)
        {
            if (Event.current.type == EventType.Repaint)
                bookingError = TransportBooking.BuildQuote(request, out displayedQuote, out displayedFare);

            float mass = request.Passengers.Where(p => !p.Destroyed).Sum(TransportBooking.PersonalMass)
                + cargo.Where(s => request.Cargo.ContainsKey(s.Item)).Sum(s => ServicePodDeliveryPlanner.MassOf(s.Item, request.Cargo[s.Item]));
            long silver = cargo.Where(s => s.Item.def == ThingDefOf.Silver).Sum(s =>
                (long)Math.Max(0, s.Count - request.Cargo.GetValueOrDefault(s.Item)));
            if (specs != null)
            {
                Widgets.Label(new Rect(0f, rect.y, rect.width, 23f), (Key + "Load").Translate(mass.ToString("0.###"), specs.Capacity.ToString("0.###"), silver));
                if (displayedFare is TransportFare fare)
                {
                    float half = rect.width / 2f;
                    Widgets.Label(new Rect(0f, rect.y + 25f, rect.width, 22f), (Key + "Route").Translate(fare.Distance, fare.Range));
                    Widgets.Label(new Rect(0f, rect.y + 49f, half, 22f), (Key + "BaseFare").Translate(fare.BaseFare));
                    Widgets.Label(new Rect(half, rect.y + 49f, half, 22f), (Key + "FuelFare").Translate(fare.Fuel.ToString("0.##"), fare.FuelFare));
                    if (request.Mode == TransportMode.Pod)
                        Widgets.Label(new Rect(0f, rect.y + 73f, half, 22f), (Key + "MaterialFare").Translate(fare.MaterialFare));
                    Widgets.Label(new Rect(half, rect.y + 73f, half, 22f), (Key + "WealthFare").Translate(fare.WealthFare));
                    Widgets.Label(new Rect(0f, rect.y + 97f, rect.width, 22f), (Key + "Total").Translate(fare.Total));
                }
            }

            bool abandonsCargo = displayedQuote?.Remainders.Any(r => r.IsAbandoned) == true;
            if (bookingError != null)
                Widgets.Label(new Rect(0f, rect.y + 118f, rect.width - 206f, 52f), bookingError.Translate());
            else if (abandonsCargo)
                Widgets.Label(new Rect(0f, rect.y + 118f, rect.width - 206f, 52f), (Key + "AbandonedCargoWarning").Translate());
            bool priorEnabled = GUI.enabled;
            GUI.enabled = priorEnabled && displayedQuote != null && !committing;
            bool confirm = Widgets.ButtonText(new Rect(rect.width - 196f, rect.yMax - 40f, 196f, 36f), (Key + "Confirm").Translate());
            GUI.enabled = priorEnabled;
            if (!confirm) return;

            TransportQuote quote = displayedQuote;
            if (abandonsCargo)
            {
                string cargoList = quote.Remainders.Where(r => r.IsAbandoned)
                    .GroupBy(r => r.Stack.Item.LabelCapNoCount.ToString())
                    .Select(g => (Key + "AbandonedCargoCount").Translate(g.Sum(r => (long)r.Stack.Count), g.Key).ToString())
                    .ToList().ToLineList();
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    (Key + "ConfirmAbandonedCargo").Translate(quote.Fare.Total, cargoList),
                    () => PayAndDispatch(quote), destructive: true));
                return;
            }
            PayAndDispatch(quote);
        }

        private void PayAndDispatch(TransportQuote quote)
        {
            if (!IsOpen || committing) return;
            committing = true;
            try
            {
                if (TransportDispatch.TryBook(request, quote, out string error))
                {
                    Close();
                    services.Close(doCloseSound: false);
                }
                else
                {
                    displayedQuote = null;
                    Messages.Message(error.Translate(), MessageTypeDefOf.RejectInput, historical: false);
                }
            }
            finally { committing = false; }
        }

        private string bookingError;

        private sealed class DestinationTargeting : Window
        {
            private enum Stage { World, Choice, Cell, Done }
            private readonly Dialog_SettlementTransport dialog;
            private readonly TransportSpecs specs;
            private readonly Texture2D icon;
            private Stage stage;
            private PlanetTile pickedTile = PlanetTile.Invalid;
            private TransportDestination? chosen;
            private Map map;
            private FloatMenu menu;

            public override Vector2 InitialSize => new Vector2(1f, 1f);
            protected override float Margin => 0f;

            internal DestinationTargeting(Dialog_SettlementTransport dialog, TransportSpecs specs, Texture2D icon)
            {
                this.dialog = dialog;
                this.specs = specs;
                this.icon = icon;
                layer = WindowLayer.GameUI;
                forcePause = true;
                doWindowBackground = false;
                drawShadow = false;
                focusWhenOpened = false;
                closeOnCancel = false;
                closeOnAccept = false;
                preventCameraMotion = false;
                soundAppear = null;
            }

            internal void Begin()
            {
                dialog.displayedQuote = null;
                dialog.Close(doCloseSound: false);
                dialog.services.Close(doCloseSound: false);
                CameraJumper.TryJump(dialog.request.Provider.Tile);
                Find.WindowStack.Add(this);
                Find.WorldTargeter.BeginTargeting(PickTile, canTargetTiles: true, mouseAttachment: icon, extraLabelGetter: HoverLabel,
                    originForClosest: dialog.request.Provider.Tile, showCancelButton: true);
            }

            public override void DoWindowContents(Rect rect) { }

            public override void WindowUpdate()
            {
                if (stage == Stage.World && !WorldRendererUtility.WorldSelected)
                {
                    if (Find.WorldTargeter.IsTargeting) Find.WorldTargeter.StopTargeting();
                    Finish();
                }
                else if (stage == Stage.World && !Find.WorldTargeter.IsTargeting)
                {
                    if (pickedTile.Valid) ChooseArrival(pickedTile);
                    else Finish();
                }
                else if (stage == Stage.Choice && !WorldRendererUtility.WorldSelected)
                {
                    menu?.Close(doCloseSound: false);
                    Finish();
                }
                else if (stage == Stage.Cell && (Find.CurrentMap != map || !Find.Maps.Contains(map) || WorldRendererUtility.WorldSelected))
                {
                    if (Find.Targeter.IsTargeting) Find.Targeter.StopTargeting();
                    Finish();
                }
            }

            public override void ExtraOnGUI()
            {
                if (stage != Stage.World && stage != Stage.Cell) return;
                Rect banner = new Rect((Verse.UI.screenWidth - 450f) / 2f, 12f, 450f, 30f);
                Widgets.DrawWindowBackground(banner);
                GameFont font = Text.Font;
                TextAnchor anchor = Text.Anchor;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(banner, (Key + (stage == Stage.World ? "PickTile" : "PickCell")).Translate());
                Text.Font = font;
                Text.Anchor = anchor;
            }

            private static PlanetTile TileFor(GlobalTargetInfo target)
            {
                if (target.WorldObject is TravellingTransporters || target.WorldObject is SettlementTransportMarker)
                    return GenWorld.MouseTile();
                return target.Tile;
            }

            private bool PickTile(GlobalTargetInfo target)
            {
                PlanetTile tile = TileFor(target);
                string error = DestinationBlock(tile, out _);
                if (error != null)
                {
                    Messages.Message(error.Translate(), MessageTypeDefOf.RejectInput, historical: false);
                    return false;
                }
                pickedTile = tile;
                return true;
            }

            private TaggedString HoverLabel(GlobalTargetInfo target)
            {
                if (!target.IsValid) return (Key + "PickTile").Translate();
                string error = DestinationBlock(TileFor(target), out TransportLeg leg);
                return error == null ? (Key + "Route").Translate(leg.Distance, leg.Range) : error.Translate();
            }

            private string DestinationBlock(PlanetTile tile, out TransportLeg leg)
            {
                string error = TransportRoute.CheckRoute(dialog.request.Provider.Tile, tile, specs, out leg);
                if (error != null) return error;
                return TransportRoute.CanBookWorldDestination(tile) || TransportRoute.LocalDestinations(tile).Any()
                    ? null : TransportRoute.InvalidDestination;
            }

            private void ChooseArrival(PlanetTile tile)
            {
                List<MapParent> parents = TransportRoute.LocalDestinations(tile).ToList();
                bool world = TransportRoute.CanBookWorldDestination(tile);
                if (parents.Count == 0)
                {
                    if (world) chosen = TransportDestination.World(tile);
                    Finish();
                }
                else if (parents.Count == 1 && !world) BeginCell(parents[0]);
                else
                {
                    stage = Stage.Choice;
                    var options = parents.Select(p => new FloatMenuOption((Key + "LandOnMap").Translate(p.LabelCap), () => BeginCell(p))).ToList();
                    if (world) options.Add(new FloatMenuOption((Key + "LandOnWorld").Translate(), () => { chosen = TransportDestination.World(tile); Finish(); }));
                    menu = new FloatMenu(options) { onCloseCallback = () => { if (stage == Stage.Choice) Finish(); } };
                    Find.WindowStack.Add(menu);
                }
            }

            private void BeginCell(MapParent parent)
            {
                var destination = new TransportDestination(pickedTile, parent.ID, IntVec3.Invalid);
                if (TransportRoute.FindLocalDestination(destination) != parent) { Finish(); return; }
                stage = Stage.Cell;
                map = parent.Map;
                CameraJumper.TryJump(map.Center, map);
                var parameters = TargetingParameters.ForDropPodsDestination();
                parameters.validator = null;
                bool IsValid(LocalTargetInfo target) => target.IsValid && Find.CurrentMap == map
                    && TransportRoute.FindLocalDestination(destination) == parent
                    && TransportRoute.IsSafeCell(dialog.request.Mode, specs.Hull, target.Cell, map);
                Find.Targeter.BeginTargeting(parameters, target => chosen = destination with { Cell = target.Cell }, target =>
                {
                    if (!target.IsValid || Find.CurrentMap != map) return;
                    List<IntVec3> cells = TransportRoute.Footprint(dialog.request.Mode, specs.Hull, target.Cell, map)
                        .Where(c => c.IsValid && c.InBounds(map)).Distinct().ToList();
                    GenDraw.DrawFieldEdges(cells, IsValid(target) ? Color.green : Color.red);
                }, IsValid, actionWhenFinished: Finish, mouseAttachment: icon);
            }

            private void Finish()
            {
                if (stage == Stage.Done) return;
                stage = Stage.Done;
                if (chosen.HasValue) dialog.request.Destination = chosen.Value;
                Close(doCloseSound: false);
                CameraJumper.TryJump(dialog.request.Provider.Tile);
                if (!dialog.services.IsOpen) Find.WindowStack.Add(dialog.services);
                if (!dialog.IsOpen) Find.WindowStack.Add(dialog);
            }
        }
    }
}

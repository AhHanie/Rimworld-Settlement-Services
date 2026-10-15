using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Payment;

namespace Settlement_Services.UI
{
    public class Dialog_ServiceBarter : Window
    {
        private const float TitleHeight = 34f;
        private const float LineHeight = 24f;
        private const float ButtonHeight = 40f;
        private const float SectionGap = 6f;
        private const float PaneTitleHeight = 30f;
        private const float PaneGap = 12f;
        private const float SideBySideMinWidth = 1150f;

        private sealed class BarterState
        {
            public SettlementServiceQuote quote;
            public List<ServicePaymentEntry> payerEntries;
            public List<ServicePaymentEntry> goodsEntries;
            public bool goodsStale;
            public bool valuesOk;
            public ServiceBarterBalance balance;
            public bool covers;
        }

        private readonly ServiceRequestSession session;
        private readonly Window detailDialog;
        private readonly Func<bool> requestReady;
        private readonly IReadOnlyList<ServiceDisplayOption> displayedOptions;
        private readonly bool inPerson;
        private readonly List<BarterTransferable> transferables;
        private readonly TransferableOneWayWidget widget;
        private readonly List<BarterGoodsTransferable> goodsTransferables = new List<BarterGoodsTransferable>();
        private readonly TransferableOneWayWidget goodsWidget;
        private readonly string goodsNote;

        private string errorText;

        public override Vector2 InitialSize => inPerson
            ? new Vector2(Mathf.Min(1320f, Verse.UI.screenWidth), Mathf.Min(800f, Verse.UI.screenHeight))
            : new Vector2(Mathf.Min(1000f, Verse.UI.screenWidth), Mathf.Min(720f, Verse.UI.screenHeight));

        public Dialog_ServiceBarter(ServiceRequestSession session, Window detailDialog, Func<bool> requestReady, IReadOnlyList<ServiceDisplayOption> displayedOptions)
        {
            this.session = session;
            this.detailDialog = detailDialog;
            this.requestReady = requestReady;
            this.displayedOptions = displayedOptions;
            inPerson = session.channel == RequestChannel.InPerson;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;

            SettlementServiceRequest request = session.BuildRequest();
            transferables = ServiceBarterSource.BuildCandidates(request)
                .Select(c => new BarterTransferable(c))
                .ToList();

            widget = new TransferableOneWayWidget(
                transferables,
                inPerson ? null : "SettlementServices.Label.BarterSourceRemote".Translate().ToString(),
                inPerson ? null : "SettlementServices.Label.BarterPledgedHeader".Translate().ToString(),
                "SettlementServices.Label.BarterCountTooltip".Translate(),
                drawMass: true);

            if (!inPerson) return;

            if (ServiceBarterGoodsSource.TryResolveAccess(request, out Settlement settlement, out _, out _, out string accessErrorKey))
            {
                foreach (Thing thing in ServiceBarterGoodsSource.StockItems(settlement))
                {
                    BarterGoodsTransferable row = TransferableUtility.TransferableMatching(thing, goodsTransferables, TransferAsOneMode.Normal);
                    if (row == null)
                    {
                        row = new BarterGoodsTransferable(settlement);
                        goodsTransferables.Add(row);
                    }

                    row.AddGood(thing);
                }

                goodsTransferables.RemoveAll(t => !t.HasAnyThing);
            }
            else
            {
                goodsNote = "SettlementServices.Label.BarterGoodsUnavailableNote".Translate(accessErrorKey.Translate());
            }

            if (goodsTransferables.Count > 0)
            {
                goodsWidget = new TransferableOneWayWidget(
                    goodsTransferables,
                    null,
                    null,
                    "SettlementServices.Label.BarterGoodsCountTooltip".Translate(),
                    drawMass: true);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            BarterState state = Evaluate();
            if (inPerson) DrawInPerson(inRect, state);
            else DrawRemote(inRect, state);
        }

        private BarterState Evaluate()
        {
            var state = new BarterState();
            SettlementServiceRequest request = session.BuildRequest();
            state.quote = SettlementServiceOrchestrator.RequestQuote(request, session.def);
            state.payerEntries = BuildPayerEntries();
            state.goodsEntries = BuildGoodsEntries(out state.goodsStale);

            bool payerOk = ServiceBarterValuation.TryTotal(state.payerEntries, out decimal offered);
            bool goodsOk = ServiceBarterValuation.TryTotal(state.goodsEntries, out decimal goodsValue);
            state.valuesOk = payerOk && goodsOk;
            state.balance = new ServiceBarterBalance(offered, state.quote.IsValid ? state.quote.totalCost : 0, goodsValue);
            state.covers = state.quote.IsValid && state.valuesOk && !state.goodsStale && state.balance.Covers;
            return state;
        }

        private void DrawRemote(Rect inRect, BarterState state)
        {
            bool showCapacity = CaravanCapacityPreview.CanShowFor(session);
            float capacityHeight = showCapacity ? CaravanCapacityDisplay.Height + SectionGap : 0f;
            float summaryHeight = LineHeight * 3f + SectionGap;
            float bottomHeight = ButtonHeight + SectionGap + capacityHeight + summaryHeight;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, TitleHeight), "SettlementServices.Label.BarterTitle".Translate(session.def.LabelCap));
            Text.Font = GameFont.Small;

            string policy = "SettlementServices.Label.BarterPolicy".Translate();
            float policyHeight = Text.CalcHeight(policy, inRect.width);
            Widgets.Label(new Rect(inRect.x, inRect.y + TitleHeight, inRect.width, policyHeight), policy);

            float widgetTop = inRect.y + TitleHeight + policyHeight + SectionGap;
            Rect widgetRect = new Rect(inRect.x, widgetTop, inRect.width, inRect.yMax - bottomHeight - widgetTop);
            DrawPane(widgetRect, null, transferables.Count == 0 ? null : widget, "SettlementServices.Label.BarterNoItemsRemote".Translate());

            float y = inRect.yMax - bottomHeight + SectionGap;
            DrawSummary(new Rect(inRect.x, y, inRect.width, summaryHeight - SectionGap), state);
            y += summaryHeight - SectionGap;

            if (showCapacity)
            {
                y += SectionGap;
                CaravanCapacityDisplay.Draw(new Rect(inRect.x, y, inRect.width, CaravanCapacityDisplay.Height), BuildCapacity(state));
            }

            DrawButtons(inRect, state.quote.IsValid && state.covers && requestReady(), showReset: false);
        }

        private void DrawInPerson(Rect inRect, BarterState state)
        {
            bool showCapacity = CaravanCapacityPreview.CanShowFor(session);
            float capacityHeight = showCapacity ? CaravanCapacityDisplay.Height + SectionGap : 0f;
            float summaryHeight = LineHeight * 3f + SectionGap;
            float bottomHeight = ButtonHeight + SectionGap + capacityHeight + summaryHeight;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, TitleHeight), "SettlementServices.Label.BarterTitle".Translate(session.def.LabelCap));
            Text.Font = GameFont.Small;

            string policy = "SettlementServices.Label.BarterPolicyInPerson".Translate();
            float policyHeight = Text.CalcHeight(policy, inRect.width);
            Widgets.Label(new Rect(inRect.x, inRect.y + TitleHeight, inRect.width, policyHeight), policy);

            Rect serviceRect = new Rect(inRect.x, inRect.y + TitleHeight + policyHeight + SectionGap, inRect.width, LineHeight + 4f);
            DrawServiceRow(serviceRect, state);

            float panesTop = serviceRect.yMax + SectionGap;
            Rect panes = new Rect(inRect.x, panesTop, inRect.width, Mathf.Max(80f, inRect.yMax - bottomHeight - panesTop));

            string offerTitle = "SettlementServices.Label.BarterOfferPane".Translate();
            string goodsTitle = "SettlementServices.Label.BarterGoodsPane".Translate();
            string noItems = "SettlementServices.Label.BarterNoItemsInPerson".Translate();
            string noGoods = goodsNote ?? "SettlementServices.Label.BarterNoGoods".Translate().ToString();

            if (panes.width >= SideBySideMinWidth)
            {
                float paneWidth = (panes.width - PaneGap) / 2f;
                DrawPane(new Rect(panes.x, panes.y, paneWidth, panes.height), offerTitle, transferables.Count == 0 ? null : widget, noItems);
                DrawPane(new Rect(panes.x + paneWidth + PaneGap, panes.y, paneWidth, panes.height), goodsTitle, goodsWidget, noGoods);
            }
            else
            {
                float paneHeight = (panes.height - PaneGap) / 2f;
                DrawPane(new Rect(panes.x, panes.y, panes.width, paneHeight), offerTitle, transferables.Count == 0 ? null : widget, noItems);
                DrawPane(new Rect(panes.x, panes.y + paneHeight + PaneGap, panes.width, paneHeight), goodsTitle, goodsWidget, noGoods);
            }

            float y = inRect.yMax - bottomHeight + SectionGap;
            DrawSummary(new Rect(inRect.x, y, inRect.width, summaryHeight - SectionGap), state);
            y += summaryHeight - SectionGap;

            if (showCapacity)
            {
                y += SectionGap;
                CaravanCapacityDisplay.Draw(new Rect(inRect.x, y, inRect.width, CaravanCapacityDisplay.Height), BuildCapacity(state));
            }

            DrawButtons(inRect, state.quote.IsValid && state.covers && requestReady(), showReset: true);
        }

        private void DrawServiceRow(Rect rect, BarterState state)
        {
            Widgets.DrawLightHighlight(rect);
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect labelRect = new Rect(rect.x + 6f, rect.y, rect.width - 12f, rect.height);
            string price = state.quote.IsValid ? ServiceBarterValuation.Format(state.quote.totalCost) : "—";
            Widgets.Label(labelRect, "SettlementServices.Label.BarterServiceLine".Translate(session.def.LabelCap, price));
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawPane(Rect rect, string title, TransferableOneWayWidget paneWidget, string emptyText)
        {
            Rect body = rect;
            if (title != null)
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(rect.x, rect.y, rect.width, PaneTitleHeight), title);
                Text.Font = GameFont.Small;
                body = new Rect(rect.x, rect.y + PaneTitleHeight, rect.width, Mathf.Max(0f, rect.height - PaneTitleHeight));
            }

            if (paneWidget == null)
            {
                GUI.color = Color.gray;
                Widgets.Label(body, emptyText);
                GUI.color = Color.white;
                return;
            }

            Widgets.BeginGroup(body);
            paneWidget.OnGUI(new Rect(0f, 0f, body.width, body.height));
            Widgets.EndGroup();
        }

        private CaravanCapacityPreview BuildCapacity(BarterState state)
        {
            float removedMass = 0f;
            float addedMass = 0f;
            float addedCapacity = 0f;
            bool estimate = false;

            if (state.valuesOk)
                removedMass = state.payerEntries.Sum(e => e.thing.GetStatValue(StatDefOf.Mass) * e.count);

            if (state.valuesOk && !state.goodsStale)
            {
                foreach (ServicePaymentEntry good in state.goodsEntries)
                {
                    if (good.thing is Pawn pawn)
                    {
                        addedMass += MassUtility.GearAndInventoryMass(pawn);
                        addedCapacity += MassUtility.Capacity(pawn);
                        estimate = true;
                    }
                    else
                    {
                        addedMass += good.thing.GetStatValue(StatDefOf.Mass) * good.count;
                    }
                }
            }

            return CaravanCapacityPreview.ForPurchase(session, state.quote, displayedOptions, removedMass, addedMass, addedCapacity, estimate);
        }

        private void DrawSummary(Rect rect, BarterState state)
        {
            Color prevColor = GUI.color;
            Text.Anchor = TextAnchor.MiddleLeft;

            if (!state.quote.IsValid)
            {
                GUI.color = ColorLibrary.RedReadable;
                Widgets.Label(new Rect(rect.x, rect.y, rect.width, LineHeight), state.quote.validationErrors.First().Translate());
                GUI.color = prevColor;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            ServiceBarterBalance balance = state.balance;
            string offeredText = state.valuesOk ? ServiceBarterValuation.Format(balance.offered) : "—";
            if (inPerson)
            {
                string goodsText = state.valuesOk ? ServiceBarterValuation.Format(balance.goodsCost) : "—";
                string balanceText = state.valuesOk ? ServiceBarterValuation.Format(balance.Balance) : "—";
                Widgets.Label(new Rect(rect.x, rect.y, rect.width, LineHeight),
                    "SettlementServices.Label.BarterBalanceLine".Translate(offeredText, ServiceBarterValuation.Format(balance.serviceCost), goodsText, balanceText));
            }
            else
            {
                Widgets.Label(new Rect(rect.x, rect.y, rect.width, LineHeight),
                    "SettlementServices.Label.BarterSelectedVsPrice".Translate(offeredText, state.quote.totalCost));
            }

            Rect secondLine = new Rect(rect.x, rect.y + LineHeight, rect.width, LineHeight);
            if (!state.valuesOk)
            {
                GUI.color = ColorLibrary.RedReadable;
                Widgets.Label(secondLine, "SettlementServices.Error.BarterInvalidValue".Translate());
            }
            else if (state.goodsStale)
            {
                GUI.color = ColorLibrary.RedReadable;
                Widgets.Label(secondLine, "SettlementServices.Error.BarterGoodsStale".Translate());
            }
            else if (!balance.Covers)
            {
                GUI.color = ColorLibrary.RedReadable;
                Widgets.Label(secondLine, "SettlementServices.Label.BarterStillNeeded".Translate(ServiceBarterValuation.Format(balance.Shortfall)));
            }
            else
            {
                GUI.color = balance.Excess > 0m ? ColorLibrary.Yellow : ColorLibrary.Green;
                Widgets.Label(secondLine, "SettlementServices.Label.BarterExcess".Translate(ServiceBarterValuation.Format(balance.Excess)));
            }

            GUI.color = ColorLibrary.RedReadable;
            if (errorText != null) Widgets.Label(new Rect(rect.x, rect.y + LineHeight * 2f, rect.width, LineHeight), errorText);

            GUI.color = prevColor;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawButtons(Rect inRect, bool canConfirm, bool showReset)
        {
            string cancelLabel = "CancelButton".Translate();
            string confirmLabel = "SettlementServices.Button.ConfirmBarter".Translate();
            string resetLabel = "SettlementServices.Button.ResetBarter".Translate();
            float confirmWidth = Mathf.Max(160f, Text.CalcSize(confirmLabel).x + 30f);
            float cancelWidth = Mathf.Max(140f, Text.CalcSize(cancelLabel).x + 30f);
            float resetWidth = Mathf.Max(120f, Text.CalcSize(resetLabel).x + 30f);

            Rect confirmRect = new Rect(inRect.xMax - confirmWidth, inRect.yMax - ButtonHeight, confirmWidth, ButtonHeight);
            Rect cancelRect = new Rect(confirmRect.x - 10f - cancelWidth, confirmRect.y, cancelWidth, ButtonHeight);

            if (showReset && Widgets.ButtonText(new Rect(inRect.x, confirmRect.y, resetWidth, ButtonHeight), resetLabel)) ResetSelection();
            if (Widgets.ButtonText(cancelRect, cancelLabel)) Close();

            bool prevEnabled = GUI.enabled;
            GUI.enabled = canConfirm;
            if (Widgets.ButtonText(confirmRect, confirmLabel)) OnConfirmClicked();
            GUI.enabled = prevEnabled;
        }

        private void ResetSelection()
        {
            errorText = null;
            foreach (BarterTransferable transferable in transferables) transferable.ForceTo(0);
            foreach (BarterGoodsTransferable transferable in goodsTransferables) transferable.ForceTo(0);
        }

        private List<ServicePaymentEntry> BuildPayerEntries() =>
            transferables.Where(t => t.CountToTransfer > 0 && t.HasAnyThing).Select(t => new ServicePaymentEntry(t.AnyThing, t.CountToTransfer)).ToList();

        private List<ServicePaymentEntry> BuildGoodsEntries(out bool stale)
        {
            stale = false;
            var entries = new List<ServicePaymentEntry>();
            foreach (BarterGoodsTransferable transferable in goodsTransferables)
            {
                if (transferable.CountToTransfer <= 0) continue;

                if (transferable.TryResolve(out List<ServicePaymentEntry> resolved)) entries.AddRange(resolved);
                else stale = true;
            }

            return entries;
        }

        private bool TryPrepare(out SettlementServiceQuote quote, out ServiceBarterBalance balance)
        {
            quote = null;
            balance = default;
            errorText = null;

            SettlementServiceRequest request = session.BuildRequest();
            quote = SettlementServiceOrchestrator.RequestQuote(request, session.def);
            if (!quote.IsValid)
            {
                errorText = quote.validationErrors.First().Translate();
                return false;
            }

            List<ServicePaymentEntry> goods = BuildGoodsEntries(out bool stale);
            if (stale)
            {
                errorText = "SettlementServices.Error.BarterGoodsStale".Translate();
                return false;
            }

            ServicePaymentSelection payment = ServicePaymentSelection.Barter(BuildPayerEntries(), goods);
            if (!ServiceBarterSource.TryValidate(payment, request, request.playerSuppliedInputs, out decimal offered, out string errorKey)
                || !ServiceBarterGoodsSource.TryValidate(payment, request, out decimal goodsValue, out errorKey))
            {
                errorText = errorKey.Translate();
                return false;
            }

            balance = new ServiceBarterBalance(offered, quote.totalCost, goodsValue);
            if (!balance.Covers)
            {
                errorText = "SettlementServices.Error.BarterInsufficientValue".Translate();
                return false;
            }

            return true;
        }

        private void OnConfirmClicked()
        {
            if (!TryPrepare(out SettlementServiceQuote quote, out ServiceBarterBalance balance)) return;

            if (!balance.NeedsExcessWarning)
            {
                Finalize(null, default);
                return;
            }

            string message = balance.goodsCost > 0m
                ? "SettlementServices.Message.BarterExcessWarningWithGoods".Translate(
                    ServiceBarterValuation.Format(balance.Excess),
                    ServiceBarterValuation.Format(balance.offered),
                    quote.totalCost,
                    ServiceBarterValuation.Format(balance.goodsCost))
                : "SettlementServices.Message.BarterExcessWarning".Translate(
                    ServiceBarterValuation.Format(balance.Excess),
                    ServiceBarterValuation.Format(balance.offered),
                    quote.totalCost);

            ServiceBarterBalance balanceAtWarning = balance;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(message, () => Finalize(true, balanceAtWarning)));
        }

        private void Finalize(bool? hasExpectation, ServiceBarterBalance expected)
        {
            if (!TryPrepare(out SettlementServiceQuote quote, out ServiceBarterBalance balance)) return;

            if (hasExpectation.HasValue && (balance.serviceCost != expected.serviceCost || balance.offered != expected.offered || balance.goodsCost != expected.goodsCost))
            {
                errorText = "SettlementServices.Message.BarterFiguresChanged".Translate();
                return;
            }

            if (!requestReady())
            {
                errorText = "SettlementServices.Message.RequestFailed".Translate();
                return;
            }

            ServicePaymentSelection payment = ServicePaymentSelection.Barter(BuildPayerEntries(), BuildGoodsEntries(out _));
            if (ServiceConfirmationFlow.TryConfirm(session, quote, detailDialog, payment, out string failureMessage))
            {
                Close();
                return;
            }

            errorText = failureMessage;
        }
    }

    internal sealed class BarterTransferable : TransferableOneWay
    {
        private readonly int reservedUnits;

        public BarterTransferable(BarterCandidate candidate)
        {
            things.Add(candidate.thing);
            reservedUnits = candidate.reservedUnits;
        }

        private int AvailableUnits => HasAnyThing && !AnyThing.Destroyed ? Mathf.Max(0, AnyThing.stackCount - reservedUnits) : 0;

        public override int GetMaximumToTransfer() => AvailableUnits;

        public override string Label => base.Label + " (" + CreditText() + ")";

        public override string TipDescription
        {
            get
            {
                string tip = "SettlementServices.Label.BarterCreditTooltip".Translate(UnitValueText());
                if (reservedUnits > 0) tip += "\n\n" + "SettlementServices.Label.BarterReservedTooltip".Translate(reservedUnits);
                return tip + "\n\n" + base.TipDescription;
            }
        }

        private string UnitValueText() =>
            ServiceBarterValuation.TryUnitValue(AnyThing, out decimal unitValue) ? ServiceBarterValuation.Format(unitValue) : "—";

        private string CreditText()
        {
            string text = "SettlementServices.Label.BarterRowCredit".Translate(UnitValueText());
            if (reservedUnits > 0) text += ", " + "SettlementServices.Label.BarterRowReserved".Translate(reservedUnits);
            return text;
        }
    }

    internal sealed class BarterGoodsTransferable : TransferableOneWay
    {
        private readonly Settlement settlement;
        private readonly Dictionary<Thing, decimal> unitValues = new Dictionary<Thing, decimal>();

        public BarterGoodsTransferable(Settlement settlement)
        {
            this.settlement = settlement;
        }

        public void AddGood(Thing thing)
        {
            if (!ServiceBarterValuation.TryUnitValue(thing, out decimal unitValue)) return;

            things.Add(thing);
            unitValues[thing] = unitValue;
        }

        public override int GetMaximumToTransfer()
        {
            int total = 0;
            for (int i = 0; i < things.Count; i++)
                if (ServiceBarterGoodsSource.IsInStock(settlement, things[i])) total += things[i].stackCount;
            return total;
        }

        public bool TryResolve(out List<ServicePaymentEntry> entries)
        {
            entries = new List<ServicePaymentEntry>();
            int remaining = CountToTransfer;
            for (int i = 0; i < things.Count && remaining > 0; i++)
            {
                Thing thing = things[i];
                if (!ServiceBarterGoodsSource.IsInStock(settlement, thing)) continue;

                int take = Math.Min(remaining, thing.stackCount);
                entries.Add(new ServicePaymentEntry(thing, take, unitValues[thing]));
                remaining -= take;
            }

            return remaining <= 0;
        }

        public override string Label
        {
            get
            {
                string label = base.Label;
                if (AnyThing is Pawn pawn)
                    label += " (" + pawn.GetGenderLabel() + ", " + Mathf.FloorToInt(pawn.ageTracker.AgeBiologicalYearsFloat) + ")";
                return label + " (" + "SettlementServices.Label.BarterRowCredit".Translate(UnitValueText()) + ")";
            }
        }

        public override string TipDescription
        {
            get
            {
                string tip = "SettlementServices.Label.BarterGoodsCreditTooltip".Translate(UnitValueText());
                string detail = AnyThing is Pawn pawn ? pawn.MainDesc(writeFaction: true) + "\n\n" + pawn.DescriptionDetailed : base.TipDescription;
                return tip + "\n\n" + detail;
            }
        }

        private string UnitValueText() =>
            AnyThing != null && unitValues.TryGetValue(AnyThing, out decimal unitValue) ? ServiceBarterValuation.Format(unitValue) : "—";
    }
}

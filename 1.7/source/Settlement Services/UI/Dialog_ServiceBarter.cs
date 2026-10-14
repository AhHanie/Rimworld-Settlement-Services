using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
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

        private readonly ServiceRequestSession session;
        private readonly Window detailDialog;
        private readonly Func<bool> requestReady;
        private readonly IReadOnlyList<ServiceDisplayOption> displayedOptions;
        private readonly List<BarterTransferable> transferables;
        private readonly TransferableOneWayWidget widget;

        private string errorText;

        public override Vector2 InitialSize => new Vector2(Mathf.Min(1000f, Verse.UI.screenWidth), Mathf.Min(720f, Verse.UI.screenHeight));

        public Dialog_ServiceBarter(ServiceRequestSession session, Window detailDialog, Func<bool> requestReady, IReadOnlyList<ServiceDisplayOption> displayedOptions)
        {
            this.session = session;
            this.detailDialog = detailDialog;
            this.requestReady = requestReady;
            this.displayedOptions = displayedOptions;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;

            transferables = ServiceBarterSource.BuildCandidates(session.BuildRequest())
                .Select(c => new BarterTransferable(c))
                .ToList();

            widget = new TransferableOneWayWidget(
                transferables,
                (session.channel == RequestChannel.Remote ? "SettlementServices.Label.BarterSourceRemote" : "SettlementServices.Label.BarterSourceInPerson").Translate(),
                "SettlementServices.Label.BarterPledgedHeader".Translate(),
                "SettlementServices.Label.BarterCountTooltip".Translate(),
                drawMass: true);
        }

        public override void DoWindowContents(Rect inRect)
        {
            SettlementServiceRequest request = session.BuildRequest();
            SettlementServiceQuote quote = SettlementServiceOrchestrator.RequestQuote(request, session.def);
            List<ServicePaymentEntry> entries = BuildEntries();
            bool valueOk = ServiceBarterValuation.TryTotal(entries, out decimal selectedValue);
            int price = quote.IsValid ? quote.totalCost : 0;
            bool covers = quote.IsValid && valueOk && ServiceBarterValuation.Covers(selectedValue, price);

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
            if (transferables.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(widgetRect, (session.channel == RequestChannel.Remote ? "SettlementServices.Label.BarterNoItemsRemote" : "SettlementServices.Label.BarterNoItemsInPerson").Translate());
                GUI.color = Color.white;
            }
            else
            {
                widget.OnGUI(widgetRect);
            }

            float y = inRect.yMax - bottomHeight + SectionGap;
            DrawSummary(new Rect(inRect.x, y, inRect.width, summaryHeight - SectionGap), quote, valueOk, selectedValue, covers);
            y += summaryHeight - SectionGap;

            if (showCapacity)
            {
                y += SectionGap;
                float removedMass = valueOk ? entries.Sum(e => e.thing.GetStatValue(StatDefOf.Mass) * e.count) : 0f;
                CaravanCapacityDisplay.Draw(new Rect(inRect.x, y, inRect.width, CaravanCapacityDisplay.Height),
                    CaravanCapacityPreview.ForPurchase(session, quote, displayedOptions, removedMass));
            }

            DrawButtons(inRect, quote.IsValid && covers && requestReady());
        }

        private void DrawSummary(Rect rect, SettlementServiceQuote quote, bool valueOk, decimal selectedValue, bool covers)
        {
            Color prevColor = GUI.color;
            Text.Anchor = TextAnchor.MiddleLeft;

            if (!quote.IsValid)
            {
                GUI.color = ColorLibrary.RedReadable;
                Widgets.Label(new Rect(rect.x, rect.y, rect.width, LineHeight), quote.validationErrors.First().Translate());
                GUI.color = prevColor;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            Widgets.Label(new Rect(rect.x, rect.y, rect.width, LineHeight),
                "SettlementServices.Label.BarterSelectedVsPrice".Translate(valueOk ? ServiceBarterValuation.Format(selectedValue) : "—", quote.totalCost));

            Rect secondLine = new Rect(rect.x, rect.y + LineHeight, rect.width, LineHeight);
            if (!valueOk)
            {
                GUI.color = ColorLibrary.RedReadable;
                Widgets.Label(secondLine, "SettlementServices.Error.BarterInvalidValue".Translate());
            }
            else if (!covers)
            {
                GUI.color = ColorLibrary.RedReadable;
                Widgets.Label(secondLine, "SettlementServices.Label.BarterStillNeeded".Translate(ServiceBarterValuation.Format(ServiceBarterValuation.Shortfall(selectedValue, quote.totalCost))));
            }
            else
            {
                decimal excess = ServiceBarterValuation.Excess(selectedValue, quote.totalCost);
                GUI.color = excess > 0m ? ColorLibrary.Yellow : ColorLibrary.Green;
                Widgets.Label(secondLine, "SettlementServices.Label.BarterExcess".Translate(ServiceBarterValuation.Format(excess)));
            }

            GUI.color = ColorLibrary.RedReadable;
            if (errorText != null) Widgets.Label(new Rect(rect.x, rect.y + LineHeight * 2f, rect.width, LineHeight), errorText);

            GUI.color = prevColor;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawButtons(Rect inRect, bool canConfirm)
        {
            string cancelLabel = "CancelButton".Translate();
            string confirmLabel = "SettlementServices.Button.ConfirmBarter".Translate();
            float confirmWidth = Mathf.Max(160f, Text.CalcSize(confirmLabel).x + 30f);
            float cancelWidth = Mathf.Max(140f, Text.CalcSize(cancelLabel).x + 30f);

            Rect confirmRect = new Rect(inRect.xMax - confirmWidth, inRect.yMax - ButtonHeight, confirmWidth, ButtonHeight);
            Rect cancelRect = new Rect(confirmRect.x - 10f - cancelWidth, confirmRect.y, cancelWidth, ButtonHeight);

            if (Widgets.ButtonText(cancelRect, cancelLabel)) Close();

            bool prevEnabled = GUI.enabled;
            GUI.enabled = canConfirm;
            if (Widgets.ButtonText(confirmRect, confirmLabel)) OnConfirmClicked();
            GUI.enabled = prevEnabled;
        }

        private List<ServicePaymentEntry> BuildEntries() =>
            transferables.Where(t => t.CountToTransfer > 0 && t.HasAnyThing).Select(t => new ServicePaymentEntry(t.AnyThing, t.CountToTransfer)).ToList();

        private bool TryPrepare(out SettlementServiceQuote quote, out ServicePaymentSelection payment, out decimal selectedValue)
        {
            quote = null;
            payment = null;
            selectedValue = 0m;
            errorText = null;

            SettlementServiceRequest request = session.BuildRequest();
            quote = SettlementServiceOrchestrator.RequestQuote(request, session.def);
            if (!quote.IsValid)
            {
                errorText = quote.validationErrors.First().Translate();
                return false;
            }

            payment = ServicePaymentSelection.Barter(BuildEntries());
            if (!ServiceBarterSource.TryValidate(payment, request, request.playerSuppliedInputs, out selectedValue, out string errorKey))
            {
                errorText = errorKey.Translate();
                return false;
            }

            if (!ServiceBarterValuation.Covers(selectedValue, quote.totalCost))
            {
                errorText = "SettlementServices.Error.BarterInsufficientValue".Translate();
                return false;
            }

            return true;
        }

        private void OnConfirmClicked()
        {
            if (!TryPrepare(out SettlementServiceQuote quote, out _, out decimal selectedValue)) return;

            if (!ServiceBarterValuation.NeedsExcessWarning(selectedValue, quote.totalCost))
            {
                Finalize(null, 0m);
                return;
            }

            int priceAtWarning = quote.totalCost;
            decimal valueAtWarning = selectedValue;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "SettlementServices.Message.BarterExcessWarning".Translate(
                    ServiceBarterValuation.Format(ServiceBarterValuation.Excess(selectedValue, quote.totalCost)),
                    ServiceBarterValuation.Format(selectedValue),
                    quote.totalCost),
                () => Finalize(priceAtWarning, valueAtWarning)));
        }

        private void Finalize(int? expectedPrice, decimal expectedValue)
        {
            if (!TryPrepare(out SettlementServiceQuote quote, out ServicePaymentSelection payment, out decimal selectedValue)) return;

            if (expectedPrice.HasValue && (quote.totalCost != expectedPrice.Value || selectedValue != expectedValue))
            {
                errorText = "SettlementServices.Message.BarterFiguresChanged".Translate();
                return;
            }

            if (!requestReady())
            {
                errorText = "SettlementServices.Message.RequestFailed".Translate();
                return;
            }

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
}

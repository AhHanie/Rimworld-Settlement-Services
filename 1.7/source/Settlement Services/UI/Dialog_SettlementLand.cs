using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Services.Land;
using UnityEngine;
using Verse;

namespace Settlement_Services.UI
{
    internal sealed class Dialog_SettlementLand : Window
    {
        private readonly Settlement settlement;
        private readonly int hostId;
        private readonly Caravan caravan;
        private readonly Window servicesWindow;
        private readonly List<TabRecord> offerTabs = new();
        private int purchaseWidth = LandPlotPolicy.MinSide;
        private int purchaseLength = LandPlotPolicy.MinSide;
        private int rentalWidth = LandPlotPolicy.MinSide;
        private int rentalLength = LandPlotPolicy.MinSide;
        private int days = 1;
        private bool bunkerInterior;
        private bool buying;
        private Vector2 scrollPosition;
        private float scrollHeight;

        public Dialog_SettlementLand(Settlement settlement, Caravan caravan, Window servicesWindow = null)
        {
            this.settlement = settlement;
            hostId = settlement.ID;
            this.caravan = caravan;
            this.servicesWindow = servicesWindow;
            SetWindowOptions();
        }

        public Dialog_SettlementLand(LandPlotRecord plot, Caravan caravan)
        {
            settlement = WorldObjectLookup.ResolveSettlement(plot.hostId);
            hostId = plot.hostId;
            this.caravan = caravan;
            SetWindowOptions();
        }

        public override Vector2 InitialSize => new Vector2(575f, 565f);

        private void SetWindowOptions()
        {
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = true;
            preventCameraMotion = false;
            offerTabs.Add(new TabRecord((LandPlotService.Key + "RentTab").Translate(), () => SelectOffer(false), () => !buying));
            offerTabs.Add(new TabRecord((LandPlotService.Key + "BuyTab").Translate(), () => SelectOffer(true), () => buying));
        }

        private void SelectOffer(bool buy)
        {
            buying = buy;
            scrollPosition = Vector2.zero;
            scrollHeight = 0f;
        }

        internal static bool IsManaging(LandPlotRecord plot) =>
            Find.WindowStack?.Windows.OfType<Dialog_SettlementLand>().Any(window => window.hostId == plot.hostId) == true;

        public override void DoWindowContents(Rect inRect)
        {
            var domain = SettlementServicesWorldComponent.Current;
            LandPlotRecord plot = domain?.LandPlotAtHost(hostId);
            var listing = new Listing_Standard { maxOneColumn = true };
            listing.Begin(inRect);
            Text.Font = GameFont.Medium;
            listing.Label((LandPlotService.Key + "ManageHost").Translate(plot?.hostLabel ?? settlement?.LabelCap.ToString()));
            listing.GapLine();
            float headerHeight = listing.CurHeight;
            listing.End();

            bool ready = domain?.LandPlotsReady == true;
            Rect body = inRect;
            body.yMin += headerHeight;
            body.yMax -= 45f;
            if (ready && plot == null && settlement is { Destroyed: false })
            {
                body.yMin += TabDrawer.TabHeight;
                Widgets.DrawMenuSection(body);
                TabDrawer.DrawTabs(body, offerTabs);
                body = body.ContractedBy(8f);
            }

            var view = new Rect(0f, 0f, body.width - 16f, Mathf.Max(body.height, scrollHeight));
            Widgets.BeginScrollView(body, ref scrollPosition, view);
            var contents = view;
            contents.height = 99999f;
            listing.Begin(contents);
            if (!ready)
                listing.Label(LandPlotService.Invalid.Translate());
            else if (plot != null) DrawContract(listing, domain, plot);
            else if (settlement is { Destroyed: false }) DrawOffer(listing, domain);
            else listing.Label(LandPlotService.MissingHost.Translate());
            scrollHeight = listing.CurHeight;
            listing.End();
            Widgets.EndScrollView();
        }

        private void DrawOffer(Listing_Standard listing, SettlementServicesWorldComponent domain)
        {
            listing.Label((LandPlotService.Key + "Offer").Translate());
            if (buying)
            {
                purchaseWidth = StepSlider(listing, "Width", purchaseWidth, LandPlotPolicy.MinSide, LandPlotPolicy.MaxSide);
                purchaseLength = StepSlider(listing, "Length", purchaseLength, LandPlotPolicy.MinSide, LandPlotPolicy.MaxSide);
            }
            else
            {
                rentalWidth = StepSlider(listing, "Width", rentalWidth, LandPlotPolicy.MinSide, LandPlotPolicy.MaxSide);
                rentalLength = StepSlider(listing, "Length", rentalLength, LandPlotPolicy.MinSide, LandPlotPolicy.MaxSide);
                days = StepSlider(listing, "Days", days, 1, LandPlotPolicy.MaxRentalDays);
            }

            listing.CheckboxLabeled((LandPlotService.Key + "BunkerOption").Translate(), ref bunkerInterior,
                (LandPlotService.Key + "BunkerOptionTip").Translate());
            long silver = ShowSilver(listing, domain);
            int total;
            bool hasQuote;
            if (buying)
                hasQuote = LandPlotPolicy.TryPurchaseQuote(purchaseWidth, purchaseLength, out total);
            else
            {
                hasQuote = LandPlotPolicy.TryRentalQuote(rentalWidth, rentalLength, days, out var quote);
                total = quote.Total;
                if (hasQuote)
                {
                    listing.Label((LandPlotService.Key + "DailyRate").Translate(quote.DailyRent.ToString("#,0.##")));
                    listing.Label((LandPlotService.Key + "PaidThrough").Translate(LandPlotPolicy.LocalTime(
                        LandPlotService.QuoteDeadline(null, days), settlement.Tile)));
                }
            }
            if (!hasQuote)
            {
                listing.Label((LandPlotService.Key + "PriceUnavailable").Translate());
                return;
            }

            string blocked = LandPlotService.TransactionBlock(settlement, caravan, null, bunkerInterior);
            listing.Gap();
            if (Button(listing, buying ? "Buy" : "Rent", blocked ?? (silver < total ? LandPlotService.SilverShort : null), total.ToString("N0")))
                ConfirmPayment(null, !buying, total);
            if (blocked != null) listing.Label(blocked.Translate());
        }

        private void DrawContract(Listing_Standard listing, SettlementServicesWorldComponent domain, LandPlotRecord plot)
        {
            long now = LandPlotPolicy.Now;
            listing.Label((LandPlotService.Key + "Dimensions").Translate(plot.width, plot.length));
            listing.Label(LayoutLine(plot.bunkerInterior));
            string status = plot.recoveryError ? "RecordError" : plot.Due(now) ? "Closed"
                : plot.RecoveryOnly(now) ? "Recovery" : plot.rented ? "Rented" : "Owned";
            listing.Label((LandPlotService.Key + status).Translate());
            if (plot.rented)
            {
                listing.Label((LandPlotService.Key + "PaidThrough").Translate(LandPlotPolicy.LocalTime(plot.paidThrough, plot.hostTile)));
                listing.Label((LandPlotService.Key + "GraceThrough").Translate(LandPlotPolicy.LocalTime(plot.graceThrough, plot.hostTile)));
            }
            listing.Gap();
            bool normal = LandPlotService.CaravanBlock(caravan) == null && LandPlotService.CanEnterNormally(plot, settlement, caravan);
            string entryBlock = LandPlotTravel.EntryBlock(plot, caravan);
            if (Button(listing, normal ? "Enter" : "Recover", entryBlock))
            {
                string result = LandPlotTravel.Enter(plot, caravan);
                if (result == null)
                {
                    Close();
                    servicesWindow?.Close();
                }
                else LandPlotService.Reject(result);
            }
            if (entryBlock != null) listing.Label(entryBlock.Translate());

            if (!plot.rented && !plot.hostLost && settlement is { Destroyed: false })
            {
                listing.GapLine();
                string saleBlock = LandPlotService.SaleBlock(settlement, caravan, plot);
                if (LandPlotPolicy.TrySaleQuote(plot, out int amount))
                {
                    if (Button(listing, "Sell", saleBlock, amount.ToString("N0"))) ConfirmSale(plot, amount);
                    if (saleBlock == LandPlotService.SaleBlockedOccupants) listing.Label(saleBlock.Translate());
                }
                else listing.Label((saleBlock ?? LandPlotService.Key + "PriceUnavailable").Translate());
            }

            if (plot.rented && !plot.Due(now) && !plot.hostLost && !plot.recoveryError)
            {
                listing.GapLine();
                days = StepSlider(listing, "Days", days, 1, LandPlotPolicy.MaxRentalDays);
                long silver = ShowSilver(listing, domain);
                if (!LandPlotPolicy.TryRentalQuote(plot.width, plot.length, days, out var quote))
                {
                    listing.Label((LandPlotService.Key + "PriceUnavailable").Translate());
                    return;
                }

                listing.Label((LandPlotService.Key + "DailyRate").Translate(quote.DailyRent.ToString("#,0.##")));
                listing.Label((LandPlotService.Key + "RenewThrough").Translate(
                    LandPlotPolicy.LocalTime(LandPlotService.QuoteDeadline(plot, days), plot.hostTile)));
                string renewal = LandPlotService.TransactionBlock(settlement, caravan, plot, plot.bunkerInterior);
                if (Button(listing, "Renew", renewal ?? (silver < quote.Total ? LandPlotService.SilverShort : null), quote.Total.ToString("N0")))
                    ConfirmPayment(plot, true, quote.Total);
                if (renewal != null) listing.Label(renewal.Translate());
            }

            if (plot.hostLost && !plot.rented && !plot.Due(now) && !plot.recoveryError
                && Button(listing, "Relinquish", LandPlotService.CaravanBlock(caravan)))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation((LandPlotService.Key + "RelinquishConfirm").Translate(),
                    () =>
                    {
                        if (LandPlotService.CaravanBlock(caravan) != null || caravan.Tile != LandPlotTravel.RecoveryTile(plot.hostTile)) return;
                        domain.RelinquishLandPlot(plot);
                        Close();
                    }, destructive: true));
        }

        private long ShowSilver(Listing_Standard listing, SettlementServicesWorldComponent domain)
        {
            long silver = caravan == null || caravan.Destroyed ? 0 : LandPlotSilverHold.Available(domain, caravan);
            listing.Label((LandPlotService.Key + "Silver").Translate(silver.ToString("N0")));
            return silver;
        }

        private void ConfirmPayment(LandPlotRecord plot, bool rent, int total)
        {
            int selectedWidth = plot?.width ?? (rent ? rentalWidth : purchaseWidth);
            int selectedLength = plot?.length ?? (rent ? rentalLength : purchaseLength);
            bool selectedBunker = plot?.bunkerInterior ?? bunkerInterior;
            int selectedDays = days;
            long expectedDeadline = plot?.paidThrough ?? 0;
            PlanetTile tile = plot?.hostTile ?? settlement.Tile;
            string confirmation;
            if (rent)
            {
                long paid = LandPlotService.QuoteDeadline(plot, selectedDays);
                confirmation = (LandPlotService.Key + "RentConfirmDimensions").Translate(
                    selectedWidth, selectedLength, selectedDays, total.ToString("N0"),
                    LandPlotPolicy.LocalTime(paid, tile), LandPlotPolicy.GraceDays,
                    LandPlotPolicy.LocalTime(LandPlotPolicy.GraceThrough(paid), tile));
            }
            else confirmation = (LandPlotService.Key + "BuyConfirmDimensions").Translate(
                selectedWidth, selectedLength, total.ToString("N0"));
            if (plot == null) confirmation += "\n\n" + LayoutLine(selectedBunker);

            bool submitted = false;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirmation, () =>
            {
                if (submitted || !Find.WindowStack.IsOpen(this)) return;
                submitted = true;
                string error = LandPlotService.Confirm(settlement, caravan, plot, expectedDeadline, selectedWidth, selectedLength,
                    selectedBunker, rent, selectedDays, total);
                if (error != null) LandPlotService.Reject(error);
                else
                {
                    scrollPosition = Vector2.zero;
                    Messages.Message((LandPlotService.Key + "PaymentAccepted").Translate(), MessageTypeDefOf.TaskCompletion, historical: false);
                }
            }));
        }

        private void ConfirmSale(LandPlotRecord plot, int amount)
        {
            int plotWidth = plot.width;
            int plotLength = plot.length;
            bool plotBunker = plot.bunkerInterior;
            SettlementLandMapParent parent = plot.parent;
            Map map = plot.Map;
            string formattedAmount = amount.ToString("N0");
            string key = map == null ? "SaleConfirmDimensions" : "SaleConfirmGeneratedDimensions";
            string confirmation = (LandPlotService.Key + key).Translate(settlement.LabelCap, plotWidth, plotLength, formattedAmount);

            bool submitted = false;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirmation, () =>
            {
                if (submitted || !Find.WindowStack.IsOpen(this)) return;
                submitted = true;
                string error = LandPlotService.ConfirmSale(settlement, caravan, plot, plotWidth, plotLength, plotBunker, parent, map, amount);
                if (error != null) LandPlotService.Reject(error);
                else Messages.Message((LandPlotService.Key + "SaleCompleted").Translate(formattedAmount),
                    MessageTypeDefOf.TaskCompletion, historical: false);
            }, destructive: true));
        }

        private static string LayoutLine(bool bunkerInterior) => (LandPlotService.Key + "Layout").Translate(
            (LandPlotService.Key + (bunkerInterior ? "LayoutBunker" : "LayoutOutdoor")).Translate());

        private static int StepSlider(Listing_Standard listing, string key, int value, int min, int max)
        {
            listing.Label((LandPlotService.Key + key).Translate(value));
            Rect row = listing.GetRect(26f);
            const float stepWidth = 36f;
            if (Widgets.ButtonText(new Rect(row.x, row.y, stepWidth, row.height), "-", active: value > min)) value--;
            if (Widgets.ButtonText(new Rect(row.xMax - stepWidth, row.y, stepWidth, row.height), "+", active: value < max)) value++;
            row.xMin += stepWidth + 12f;
            row.xMax -= stepWidth + 12f;
            return Mathf.RoundToInt(Widgets.HorizontalSlider(row, value, min, max, roundTo: 1f));
        }

        private static bool Button(Listing_Standard listing, string key, string blocked, params NamedArgument[] args)
        {
            Rect rect = listing.GetRect(32f);
            if (blocked != null) TooltipHandler.TipRegion(rect, blocked.Translate());
            bool clicked = Widgets.ButtonText(rect, (LandPlotService.Key + key).Translate(args), active: blocked == null);
            listing.Gap(4f);
            return clicked && blocked == null;
        }
    }
}

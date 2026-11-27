using System;
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
        private int side = LandPlotPolicy.MinSide;
        private int days = 1;
        private bool bunkerInterior;

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
        }

        internal static bool IsManaging(LandPlotRecord plot) =>
            Find.WindowStack?.Windows.OfType<Dialog_SettlementLand>().Any(window => window.hostId == plot.hostId) == true;

        public override void DoWindowContents(Rect inRect)
        {
            var domain = SettlementServicesWorldComponent.Current;
            LandPlotRecord plot = domain?.LandPlotAtHost(hostId);
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            Text.Font = GameFont.Medium;
            listing.Label((LandPlotService.Key + "ManageHost").Translate(plot?.hostLabel ?? settlement?.LabelCap.ToString()));
            Text.Font = GameFont.Small;
            listing.GapLine();
            if (domain == null || !domain.LandPlotsReady)
                listing.Label(LandPlotService.Invalid.Translate());
            else if (plot == null) DrawOffer(listing, domain);
            else DrawContract(listing, domain, plot);
            listing.End();
        }

        private void DrawOffer(Listing_Standard listing, SettlementServicesWorldComponent domain)
        {
            listing.Label((LandPlotService.Key + "Offer").Translate());
            side = StepSlider(listing, "Size", side, LandPlotPolicy.MinSide, LandPlotPolicy.MaxSide);
            days = StepSlider(listing, "Days", days, 1, LandPlotPolicy.MaxRentalDays);

            long silver = ShowSilver(listing, domain);
            bool hasQuote = LandPlotPolicy.TryQuote(side, days, out var quote);
            if (hasQuote)
            {
                listing.Label((LandPlotService.Key + "DailyRate").Translate(quote.DailyRent.ToString("N0")));
                listing.Label((LandPlotService.Key + "PaidThrough").Translate(LandPlotPolicy.LocalTime(
                    LandPlotService.QuoteDeadline(null, days), settlement.Tile)));
            }
            listing.CheckboxLabeled((LandPlotService.Key + "BunkerOption").Translate(), ref bunkerInterior,
                (LandPlotService.Key + "BunkerOptionTip").Translate());
            if (!hasQuote)
            {
                listing.Label((LandPlotService.Key + "PriceUnavailable").Translate());
                return;
            }

            string blocked = LandPlotService.TransactionBlock(settlement, caravan, null, bunkerInterior);
            listing.Gap();
            if (Button(listing, "Buy", blocked ?? (silver < quote.Purchase ? LandPlotService.SilverShort : null), quote.Purchase.ToString("N0")))
                ConfirmPayment(null, false, quote.Purchase);
            if (Button(listing, "Rent", blocked ?? (silver < quote.Rent ? LandPlotService.SilverShort : null), quote.Rent.ToString("N0")))
                ConfirmPayment(null, true, quote.Rent);
            if (blocked != null) listing.Label(blocked.Translate());
        }

        private void DrawContract(Listing_Standard listing, SettlementServicesWorldComponent domain, LandPlotRecord plot)
        {
            long now = LandPlotPolicy.Now;
            listing.Label((LandPlotService.Key + "Size").Translate(plot.side));
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

            if (plot.rented && !plot.Due(now) && !plot.hostLost && !plot.recoveryError)
            {
                listing.GapLine();
                days = StepSlider(listing, "Days", days, 1, LandPlotPolicy.MaxRentalDays);
                long silver = ShowSilver(listing, domain);
                if (!LandPlotPolicy.TryQuote(plot.side, days, out var quote))
                {
                    listing.Label((LandPlotService.Key + "PriceUnavailable").Translate());
                    return;
                }

                listing.Label((LandPlotService.Key + "DailyRate").Translate(quote.DailyRent.ToString("N0")));
                listing.Label((LandPlotService.Key + "RenewThrough").Translate(
                    LandPlotPolicy.LocalTime(LandPlotService.QuoteDeadline(plot, days), plot.hostTile)));
                string renewal = LandPlotService.TransactionBlock(settlement, caravan, plot, plot.bunkerInterior);
                if (Button(listing, "Renew", renewal ?? (silver < quote.Rent ? LandPlotService.SilverShort : null), quote.Rent.ToString("N0")))
                    ConfirmPayment(plot, true, quote.Rent);
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
            int selectedSide = plot?.side ?? side;
            bool selectedBunker = plot?.bunkerInterior ?? bunkerInterior;
            int selectedDays = days;
            long expectedDeadline = plot?.paidThrough ?? 0;
            PlanetTile tile = plot?.hostTile ?? settlement.Tile;
            string confirmation;
            if (rent)
            {
                long paid = LandPlotService.QuoteDeadline(plot, selectedDays);
                confirmation = (LandPlotService.Key + "RentConfirm").Translate(selectedSide, selectedDays, total.ToString("N0"),
                    LandPlotPolicy.LocalTime(paid, tile), LandPlotPolicy.GraceDays,
                    LandPlotPolicy.LocalTime(LandPlotPolicy.GraceThrough(paid), tile));
            }
            else confirmation = (LandPlotService.Key + "BuyConfirm").Translate(selectedSide, total.ToString("N0"));
            if (plot == null) confirmation += "\n\n" + LayoutLine(selectedBunker);

            bool submitted = false;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirmation, () =>
            {
                if (submitted || !Find.WindowStack.IsOpen(this)) return;
                submitted = true;
                string error = LandPlotService.Confirm(settlement, caravan, plot, expectedDeadline, selectedSide,
                    selectedBunker, rent, selectedDays, total);
                if (error != null) LandPlotService.Reject(error);
                else Messages.Message((LandPlotService.Key + "PaymentAccepted").Translate(), MessageTypeDefOf.TaskCompletion, historical: false);
            }));
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

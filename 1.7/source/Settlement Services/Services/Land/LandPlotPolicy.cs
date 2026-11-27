using System;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Pricing;
using Verse;

namespace Settlement_Services.Services.Land
{
    internal static class LandPlotPolicy
    {
        internal const int MinSide = 10;
        internal const int MaxSide = 100;
        internal const int MaxRentalDays = 30;
        internal const int GraceDays = 3;
        internal const int LoadedMapLimit = 120;
        private const int PurchasePriceMultiplier = 10;
        private const double RentalPriceDivisor = 100d;

        private static bool ValidSide(int side) => side >= MinSide && side <= MaxSide;
        internal static bool ValidDimensions(int width, int length) => ValidSide(width) && ValidSide(length);

        internal static bool TryPurchaseQuote(int width, int length, out int price)
        {
            price = 0;
            if (!ValidDimensions(width, length)) return false;
            double purchasePrice = CalculatePurchasePrice(width, length);
            if (!double.IsFinite(purchasePrice) || purchasePrice < 1 || purchasePrice > int.MaxValue) return false;
            price = (int)purchasePrice;
            return true;
        }

        internal static bool TryRentalQuote(int width, int length, int days, out (double DailyRent, int Total) quote)
        {
            quote = default;
            if (!ValidDimensions(width, length) || days < 1 || days > MaxRentalDays) return false;
            double purchasePrice = CalculatePurchasePrice(width, length);
            if (!double.IsFinite(purchasePrice) || purchasePrice < 1) return false;
            // Multiply by the term before dividing so an exact half-silver total stays at the midpoint.
            double total = Math.Max(1, Math.Round(purchasePrice * days / RentalPriceDivisor, MidpointRounding.AwayFromZero));
            if (total > int.MaxValue) return false;
            quote = (purchasePrice / RentalPriceDivisor, (int)total);
            return true;
        }

        private static double CalculatePurchasePrice(int width, int length)
        {
            var def = LandPlotService.Def;
            if (def == null) return double.NaN;

            IServicePricingContext pricing = ServicePricingContext.Current;
            double minimumSizePrice = ServicePricingEngine.ScaledCost(def, pricing.TotalPlayerWealth,
                difficultyMultiplier: 1f, wealthPriceScalePct: pricing.WealthPriceScalePct);
            double effectiveSide = Math.Sqrt(width * length);
            return Math.Round(minimumSizePrice * effectiveSide / (MinSide * PurchasePriceMultiplier)) * PurchasePriceMultiplier;
        }

        internal static bool TrySaleQuote(LandPlotRecord plot, out int amount)
        {
            amount = 0;
            if (!TryPurchaseQuote(plot.width, plot.length, out int purchase)) return false;
            amount = (int)((3L * purchase + 2L) / 4L);
            return true;
        }

        internal static long Now => Find.TickManager.TicksGame;
        internal static long PaidThrough(long start, int days) => checked(start + (long)days * GenDate.TicksPerDay);
        internal static long GraceThrough(long paidThrough) => PaidThrough(paidThrough, GraceDays);

        internal static string LocalTime(long tick, PlanetTile tile)
        {
            if (!LandPlotTravel.TileExists(tile)) return "SettlementServices.Land.WorldTick".Translate(tick);
            long absoluteTick = Find.TickManager.gameStartAbsTick + tick;
            var location = Find.WorldGrid.LongLatOf(tile);
            long localTick = absoluteTick + GenDate.LocalTicksOffsetFromLongitude(location.x);
            int minutes = (int)((localTick % GenDate.TicksPerHour + GenDate.TicksPerHour) % GenDate.TicksPerHour
                * 60 / GenDate.TicksPerHour);
            return "SettlementServices.Land.LocalTime".Translate(GenDate.DateFullStringAt(absoluteTick, location),
                GenDate.HourOfDay(absoluteTick, location.x).ToString("D2"), minutes.ToString("D2"));
        }
    }
}

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
        private const int RentalDaysPerPurchase = 10;

        internal static bool ValidSide(int side) => side >= MinSide && side <= MaxSide;
        internal static bool ValidTerm(int days) => days >= 1 && days <= MaxRentalDays;
        internal static bool TryQuote(int side, int days, out (int Purchase, int DailyRent, int Rent) quote)
        {
            quote = default;
            var def = LandPlotService.Def;
            if (def == null || !ValidSide(side) || !ValidTerm(days)) return false;

            IServicePricingContext pricing = ServicePricingContext.Current;
            double minimumSizePrice = ServicePricingEngine.ScaledCost(def, pricing.TotalPlayerWealth,
                difficultyMultiplier: 1f, wealthPriceScalePct: pricing.WealthPriceScalePct);
            double dailyPrice = Math.Round(minimumSizePrice * side / (MinSide * RentalDaysPerPurchase));
            if (!double.IsFinite(dailyPrice) || dailyPrice < 1 || dailyPrice > int.MaxValue / Math.Max(days, RentalDaysPerPurchase))
                return false;

            int daily = (int)dailyPrice;
            quote = (checked(daily * RentalDaysPerPurchase), daily, checked(daily * days));
            return true;
        }

        internal static bool TrySaleQuote(LandPlotRecord plot, out int amount)
        {
            amount = 0;
            if (!TryQuote(plot.side, 1, out var quote)) return false;
            amount = (int)((3L * quote.Purchase + 2L) / 4L);
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

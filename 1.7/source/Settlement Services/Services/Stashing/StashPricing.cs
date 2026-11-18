using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Settlement_Services.Services.Stashing
{
    public readonly struct StashSelection
    {
        public readonly Thing thing;
        public readonly int count;
        public StashSelection(Thing thing, int count) { this.thing = thing; this.count = count; }
    }

    public readonly struct StashQuote
    {
        public readonly decimal value;
        public readonly decimal mass;
        public readonly int stacks;
        public readonly int dailyFee;
        public readonly int handlingFee;
        public readonly int days;
        public readonly int total;
        public StashQuote(decimal value, decimal mass, int stacks, int dailyFee, int handlingFee, int days, int total)
        {
            this.value = value; this.mass = mass; this.stacks = stacks;
            this.dailyFee = dailyFee; this.handlingFee = handlingFee; this.days = days; this.total = total;
        }
    }

    public static class StashPricing
    {
        public const int HandlingFee = 12;
        public const int MinimumDailyFee = 2;
        public const decimal ValueRate = 0.001m;
        public const decimal MassRate = 0.25m;
        public const decimal StackRate = 0.5m;
        public const int MaximumPurchaseDays = 60;

        public static bool TryQuote(IEnumerable<StashSelection> selections, int days, bool newContract, out StashQuote quote)
        {
            quote = default;
            if (days < 1 || days > MaximumPurchaseDays || selections == null) return false;
            try
            {
                decimal value = 0m, mass = 0m;
                int stacks = 0;
                foreach (StashSelection selection in selections)
                {
                    Thing thing = selection.thing;
                    if (thing == null || thing.Destroyed || selection.count < 1 || selection.count > thing.stackCount) return false;
                    float unitValue = thing.MarketValue;
                    float unitMass = thing.GetStatValue(StatDefOf.Mass);
                    if (float.IsNaN(unitValue) || float.IsInfinity(unitValue) || unitValue < 0f
                        || float.IsNaN(unitMass) || float.IsInfinity(unitMass) || unitMass < 0f) return false;
                    value = checked(value + (decimal)unitValue * selection.count);
                    mass = checked(mass + (decimal)unitMass * selection.count);
                    stacks = checked(stacks + 1);
                }
                if (stacks == 0) return false;
                int daily = checked((int)Math.Max(MinimumDailyFee, Math.Ceiling(value * ValueRate + mass * MassRate + stacks * StackRate)));
                int handling = newContract ? HandlingFee : 0;
                int total = checked(handling + daily * days);
                quote = new StashQuote(value, mass, stacks, daily, handling, days, total);
                return true;
            }
            catch (OverflowException) { return false; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using Verse;

namespace Settlement_Services.Framework.Payment
{
    public readonly struct ServiceBarterBalance
    {
        public readonly decimal offered;
        public readonly decimal serviceCost;
        public readonly decimal goodsCost;

        public ServiceBarterBalance(decimal offered, decimal serviceCost, decimal goodsCost)
        {
            this.offered = offered;
            this.serviceCost = serviceCost;
            this.goodsCost = goodsCost;
        }

        public decimal Balance => offered - serviceCost - goodsCost;

        public bool Covers => Balance >= 0m;

        public decimal Shortfall => Math.Max(0m, -Balance);

        public decimal Excess => Math.Max(0m, Balance);

        public bool NeedsExcessWarning => Balance > ServiceBarterValuation.ExcessWarningThreshold;
    }

    public static class ServiceBarterValuation
    {
        public const decimal ExcessWarningThreshold = 10m;

        private const float MaxUnitValue = 1e12f;

        public static bool TryUnitValue(Thing thing, out decimal value)
        {
            value = 0m;
            if (thing == null || thing.Destroyed) return false;

            float market = thing.MarketValue;
            if (float.IsNaN(market) || float.IsInfinity(market) || market > MaxUnitValue) return false;

            value = (decimal)Math.Max(0f, market);
            return true;
        }

        public static bool TryTotal(IEnumerable<ServicePaymentEntry> entries, out decimal total)
        {
            total = 0m;
            if (entries == null) return true;

            foreach (ServicePaymentEntry entry in entries)
            {
                if (entry == null || entry.count <= 0 || !TryUnitValue(entry.thing, out decimal unitValue))
                {
                    total = 0m;
                    return false;
                }

                total += unitValue * entry.count;
            }

            return true;
        }

        public static string Format(decimal value) => value.ToString("0.00######", CultureInfo.CurrentCulture);
    }
}

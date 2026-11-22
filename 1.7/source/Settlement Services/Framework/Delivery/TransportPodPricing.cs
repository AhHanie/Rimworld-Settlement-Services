using System;
using RimWorld;

namespace Settlement_Services.Framework.Delivery
{
    internal static class TransportPodPricing
    {
        internal static bool TryGetMaterialCost(out int cost)
        {
            cost = 0;
            float steel = ThingDefOf.Steel?.BaseMarketValue ?? float.NaN;
            float component = ThingDefOf.ComponentIndustrial?.BaseMarketValue ?? float.NaN;
            if (!float.IsFinite(steel) || !float.IsFinite(component) || steel < 0f || component < 0f) return false;

            double total = Math.Round(60d * steel + component);
            if (total > int.MaxValue) return false;
            cost = (int)total;
            return true;
        }
    }
}

using System;
using System.Collections.Generic;

namespace Settlement_Services.Framework.Compat.VehicleFramework
{
    internal sealed class VehicleShopCandidate
    {
        public string defName;
        public int tier;
    }

    internal static class VehicleShopDraw
    {
        public const int MinTier = 1;
        public const int MaxTier = 3;
        public const int MinOffers = 2;
        public const int MaxOffers = 3;
        public const int MaxTierThreePerRoster = 1;

        private static readonly float[] TierWeights = { 75f, 20f, 5f };

        public static List<VehicleShopCandidate> Select(IReadOnlyList<VehicleShopCandidate> pool, Func<float> roll, int[] drawnTierCounts = null)
        {
            var chosen = new List<VehicleShopCandidate>();
            var remaining = new List<VehicleShopCandidate>(pool);
            int offerCount = MinOffers + Math.Min(MaxOffers - MinOffers, (int)(Unit(roll) * (MaxOffers - MinOffers + 1)));
            int tierThreeChosen = 0;

            for (int i = 0; i < offerCount && remaining.Count > 0; i++)
            {
                int drawnTier = DrawTier(roll);
                if (drawnTierCounts != null) drawnTierCounts[drawnTier - MinTier]++;

                VehicleShopCandidate pick = PickCandidate(remaining, drawnTier, tierThreeChosen < MaxTierThreePerRoster, roll);
                if (pick == null) break;

                remaining.Remove(pick);
                chosen.Add(pick);
                if (pick.tier == MaxTier) tierThreeChosen++;
            }
            return chosen;
        }

        public static int DrawTier(Func<float> roll)
        {
            float total = 0f;
            foreach (float weight in TierWeights) total += weight;

            float target = Unit(roll) * total;
            float cumulative = 0f;
            for (int i = 0; i < TierWeights.Length; i++)
            {
                cumulative += TierWeights[i];
                if (target < cumulative) return MinTier + i;
            }
            return MinTier;
        }

        private static VehicleShopCandidate PickCandidate(List<VehicleShopCandidate> remaining, int drawnTier, bool tierThreeAllowed, Func<float> roll)
        {
            for (int tier = drawnTier; tier >= MinTier; tier--)
            {
                VehicleShopCandidate pick = PickInTier(remaining, tier, tierThreeAllowed, roll);
                if (pick != null) return pick;
            }
            for (int tier = drawnTier + 1; tier <= MaxTier; tier++)
            {
                VehicleShopCandidate pick = PickInTier(remaining, tier, tierThreeAllowed, roll);
                if (pick != null) return pick;
            }
            return null;
        }

        private static VehicleShopCandidate PickInTier(List<VehicleShopCandidate> remaining, int tier, bool tierThreeAllowed, Func<float> roll)
        {
            if (tier == MaxTier && !tierThreeAllowed) return null;

            int count = 0;
            foreach (VehicleShopCandidate candidate in remaining)
                if (candidate.tier == tier) count++;
            if (count == 0) return null;

            int index = Math.Min(count - 1, (int)(Unit(roll) * count));
            foreach (VehicleShopCandidate candidate in remaining)
            {
                if (candidate.tier != tier) continue;
                if (index-- == 0) return candidate;
            }
            return null;
        }

        private static float Unit(Func<float> roll)
        {
            float value = roll();
            if (value < 0f) return 0f;
            return value >= 1f ? 0.9999999f : value;
        }
    }
}

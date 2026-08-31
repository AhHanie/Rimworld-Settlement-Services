using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Framework.Specialty
{
    public static class SettlementSpecialtyGenerator
    {
        private static readonly (int count, float weight)[] CountWeights =
        {
            (1, 0.65f), (2, 0.30f), (3, 0.05f),
        };

        private const int CountSalt = 1;
        private const int PickSaltBase = 100;

        public static List<SettlementSpecialtyDef> EligibleSpecialties(Settlement settlement)
        {
            return DefDatabase<SettlementSpecialtyDef>.AllDefsListForReading
                .Where(d => IsEligible(d, settlement))
                .ToList();
        }

        public static List<SettlementSpecialtyDef> Generate(Settlement settlement)
        {
            int seed = Gen.HashCombineInt(Find.World.info.Seed, settlement.ID);

            List<SettlementSpecialtyDef> eligible = EligibleSpecialties(settlement);
            if (eligible.Count == 0) return new List<SettlementSpecialtyDef>();

            int count = Mathf.Min(WeightedCount(seed), eligible.Count);
            var result = new List<SettlementSpecialtyDef>();
            var pool = new List<SettlementSpecialtyDef>(eligible);

            for (int i = 0; i < count; i++)
            {
                int pickSeed = Gen.HashCombineInt(seed, PickSaltBase + i);
                SettlementSpecialtyDef picked = WeightedPick(pool, pickSeed);
                result.Add(picked);
                pool.Remove(picked);
            }

            return result;
        }

        private static bool IsEligible(SettlementSpecialtyDef def, Settlement settlement)
        {
            if (def.disabled) return false;

            Faction faction = settlement.Faction;
            if (faction == null) return false;

            if (def.minTechLevel != TechLevel.Undefined && faction.def.techLevel < def.minTechLevel) return false;
            if (def.maxTechLevel != TechLevel.Undefined && faction.def.techLevel > def.maxTechLevel) return false;

            if (!def.requiredFactionCategoryTags.NullOrEmpty()
                && !def.requiredFactionCategoryTags.Contains(faction.def.categoryTag)) return false;

            if (def.requiresFactionHasIdeo && faction.ideos?.PrimaryIdeo == null) return false;

            return true;
        }

        private static int WeightedCount(int seed)
        {
            float roll = Rand.ValueSeeded(Gen.HashCombineInt(seed, CountSalt));
            float cumulative = 0f;
            foreach (var (count, weight) in CountWeights)
            {
                cumulative += weight;
                if (roll <= cumulative) return count;
            }
            return CountWeights[CountWeights.Length - 1].count;
        }

        private static SettlementSpecialtyDef WeightedPick(List<SettlementSpecialtyDef> pool, int seed)
        {
            float totalWeight = pool.Sum(d => d.selectionWeight);
            if (totalWeight <= 0f) return pool[Rand.RangeSeeded(0, pool.Count, seed)];

            float roll = Rand.RangeSeeded(0f, totalWeight, seed);
            float cumulative = 0f;
            foreach (SettlementSpecialtyDef def in pool)
            {
                cumulative += def.selectionWeight;
                if (roll <= cumulative) return def;
            }
            return pool[pool.Count - 1];
        }
    }
}

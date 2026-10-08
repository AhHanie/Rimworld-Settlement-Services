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

            ModSettings settings = ModSettings.Current;
            int baseCount = WeightedCount(seed, settings.specialtyChanceOnePct, settings.specialtyChanceTwoPct);
            int count = ResolveCount(baseCount, settings.specialtyCountOffset, eligible.Count);
            var result = new List<SettlementSpecialtyDef>();
            if (count == 0) return result;

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

        public static int ResolveCount(int baseCount, int offset, int eligibleCount)
        {
            return Mathf.Clamp(baseCount + offset, 0, eligibleCount);
        }

        private static int WeightedCount(int seed, int chanceOnePct, int chanceTwoPct)
        {
            float roll = Rand.ValueSeeded(Gen.HashCombineInt(seed, CountSalt));
            int chanceThreePct = 100 - chanceOnePct - chanceTwoPct;
            int[] chancesPct = { chanceOnePct, chanceTwoPct, chanceThreePct };

            float cumulative = 0f;
            int lastPositive = 1;
            for (int i = 0; i < chancesPct.Length; i++)
            {
                if (chancesPct[i] <= 0) continue;

                int count = i + 1;
                lastPositive = count;
                cumulative += chancesPct[i] / 100f;
                if (roll <= cumulative) return count;
            }
            return lastPositive;
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

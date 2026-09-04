using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Settlement_Services.Domain
{
    public static class SettlementIdeologyRoster
    {
        private const int MinCount = 1;
        private const int MaxCount = 3;
        private const int CountSalt = 1;
        private const int PickSaltBase = 100;

        public static List<string> EligibleCandidateLoadIds()
        {
            if (!ModsConfig.IdeologyActive || Find.IdeoManager.classicMode) return new List<string>();

            return Find.IdeoManager.IdeosListForReading
                .Where(i => i != null && !i.hidden)
                .Select(i => i.GetUniqueLoadID())
                .Distinct()
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
        }

        public static List<string> GenerateRoster(int settlementId, out int count, List<string> reservedLoadIds = null)
        {
            List<string> eligible = EligibleCandidateLoadIds();
            count = 0;
            if (eligible.Count == 0) return new List<string>();

            int seed = Gen.HashCombineInt(Find.World.info.Seed, settlementId);
            count = Mathf.Min(RollCount(seed), eligible.Count);

            var pool = new List<string>(eligible);
            var result = new List<string>();

            if (reservedLoadIds != null)
            {
                foreach (string reserved in reservedLoadIds)
                {
                    if (result.Count >= count) break;
                    if (reserved == null || !pool.Remove(reserved)) continue;
                    result.Add(reserved);
                }
            }

            FillFrom(pool, result, count, seed);
            return result;
        }

        public static List<string> FillVacancies(int settlementId, IEnumerable<string> existingValidIds, int targetCount)
        {
            var existing = existingValidIds?.Where(id => id != null).Distinct().ToList() ?? new List<string>();
            List<string> eligible = EligibleCandidateLoadIds();
            if (eligible.Count == 0 || existing.Count >= targetCount) return existing;

            int seed = Gen.HashCombineInt(Find.World.info.Seed, settlementId);
            var pool = eligible.Where(id => !existing.Contains(id)).ToList();
            var result = new List<string>(existing);

            FillFrom(pool, result, targetCount, seed);
            return result;
        }

        public static List<Ideo> ResolveRoster(IEnumerable<string> loadIds)
        {
            var result = new List<Ideo>();
            if (loadIds == null) return result;

            foreach (string id in loadIds)
            {
                Ideo ideo = IdeoLookup.ResolveIdeo(id);
                if (ideo != null && !ideo.hidden) result.Add(ideo);
            }
            return result;
        }

        private static void FillFrom(List<string> pool, List<string> result, int targetCount, int seed)
        {
            int pickIndex = result.Count;
            while (result.Count < targetCount && pool.Count > 0)
            {
                int pickSeed = Gen.HashCombineInt(seed, PickSaltBase + pickIndex++);
                int index = Rand.RangeSeeded(0, pool.Count, pickSeed);
                result.Add(pool[index]);
                pool.RemoveAt(index);
            }
        }

        private static int RollCount(int seed)
        {
            float roll = Rand.ValueSeeded(Gen.HashCombineInt(seed, CountSalt));
            if (roll < 1f / 3f) return MinCount;
            if (roll < 2f / 3f) return MinCount + 1;
            return MaxCount;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Framework.Board
{
    internal static class BoardRewardService
    {
        internal const float MinBudget = 200f;
        internal const float MaxBudget = 8000f;

        private const float BaseBudget = 150f;
        private const float BudgetPerPawnDay = 95f;
        private const float AcceptableLowFraction = 0.5f;
        private const float AcceptableHighFraction = 1.6f;
        private const int GenerationAttempts = 2;

        internal static float ComputeBudget(SettlementBoardJobDef def, int partySize, float workDays, float difficulty, IReadOnlyList<BoardSkillRequirement> requirements)
        {
            float pawnDays = partySize * workDays;
            float skillFactor = Mathf.Clamp(1f + 0.03f * (WeightedMeanThreshold(requirements) - 6f), 0.85f, 1.35f);
            float storytellerFactor = Find.Storyteller?.difficulty != null ? Find.Storyteller.difficulty.EffectiveQuestRewardValueFactor : 1f;

            float budget = (BaseBudget + BudgetPerPawnDay * pawnDays)
                * (0.8f + 0.5f * difficulty)
                * skillFactor
                * def.rewardMultiplier
                * storytellerFactor
                * Rand.Range(0.9f, 1.1f);

            return RoundToQuarterHundred(Mathf.Clamp(budget, MinBudget, MaxBudget));
        }

        internal static float WeightedMeanThreshold(IReadOnlyList<BoardSkillRequirement> requirements)
        {
            float weighted = 0f;
            float total = 0f;
            for (int i = 0; i < requirements.Count; i++)
            {
                weighted += requirements[i].minLevel * requirements[i].requiredCount;
                total += requirements[i].requiredCount;
            }
            return total <= 0f ? 0f : weighted / total;
        }

        internal static bool TryBankReward(SettlementServicesWorldComponent domain, BoardJobRecord job, Faction provider)
        {
            List<Thing> items = null;
            for (int attempt = 0; attempt < GenerationAttempts && items == null; attempt++)
                items = TryGenerateItems(job.rewardBudget, provider);

            if (items == null) items = MakeSilver(job.rewardBudget);

            var banked = new List<Thing>();
            foreach (Thing item in items)
            {
                if (item == null || item.Destroyed) continue;
                if (domain.TryTakeItemCustody(item)) banked.Add(item);
            }

            if (banked.Count != items.Count)
            {
                DestroyBanked(domain, banked);
                foreach (Thing item in items) if (!item.Destroyed && !banked.Contains(item)) item.Destroy();
                return false;
            }

            job.reward = new BoardRewardRecord
            {
                items = banked,
                marketValue = TotalMarketValue(banked),
                label = string.Join(", ", banked.Select(t => t.Label)),
            };
            return true;
        }

        private static List<Thing> TryGenerateItems(float budget, Faction provider)
        {
            ThingSetMakerDef maker = ThingSetMakerDefOf.Reward_ItemsStandard;
            if (maker?.root == null) return null;

            var parms = new ThingSetMakerParams
            {
                totalMarketValueRange = new FloatRange(0.7f, 1.3f) * budget,
                makingFaction = provider,
            };

            List<Thing> generated;
            try
            {
                if (!maker.root.CanGenerate(parms)) return null;
                generated = maker.root.Generate(parms);
            }
            catch (Exception ex)
            {
                SupportLog.Error($"Job board reward generation threw: {ex}");
                return null;
            }

            if (generated.NullOrEmpty()) return null;

            float value = TotalMarketValue(generated);
            bool acceptable = generated.All(t => t != null && !(t is Pawn))
                && value >= budget * AcceptableLowFraction
                && value <= budget * AcceptableHighFraction;
            if (acceptable) return generated;

            foreach (Thing thing in generated) if (thing != null && !thing.Destroyed) thing.Destroy();
            return null;
        }

        private static List<Thing> MakeSilver(float budget)
        {
            var stacks = new List<Thing>();
            int remaining = Mathf.Max(1, Mathf.RoundToInt(budget / ThingDefOf.Silver.BaseMarketValue));
            while (remaining > 0)
            {
                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = Mathf.Min(remaining, ThingDefOf.Silver.stackLimit);
                remaining -= silver.stackCount;
                stacks.Add(silver);
            }
            return stacks;
        }

        internal static float TotalMarketValue(IEnumerable<Thing> things)
        {
            float total = 0f;
            foreach (Thing thing in things)
            {
                if (thing == null || thing.Destroyed) continue;
                total += thing.MarketValue * thing.stackCount;
            }
            return total;
        }

        internal static bool TryDeliver(SettlementServicesWorldComponent domain, BoardJobRecord job, Caravan caravan)
        {
            BoardRewardRecord reward = job.reward;
            if (reward == null || reward.settled) return true;
            if (caravan == null || caravan.Destroyed) return false;

            for (int i = reward.items.Count - 1; i >= 0; i--)
            {
                Thing item = reward.items[i];
                if (item == null || item.Destroyed)
                {
                    reward.items.RemoveAt(i);
                    continue;
                }

                if (CaravanInventoryUtility.FindPawnToMoveInventoryTo(item, caravan.PawnsListForReading, null) == null) return false;

                domain.ReleaseItemCustody(item);
                CaravanInventoryUtility.GiveThing(caravan, item);
                reward.items.RemoveAt(i);
            }

            reward.settled = true;
            return true;
        }

        internal static void QueueHomeDelivery(SettlementServicesWorldComponent domain, BoardJobRecord job)
        {
            BoardRewardRecord reward = job.reward;
            if (reward == null || reward.settled) return;

            foreach (Thing item in reward.items)
            {
                if (item == null || item.Destroyed) continue;
                domain.QueueHomeDelivery(new TargetSnapshot
                {
                    kind = TargetKind.Item,
                    liveThing = item,
                    snapshotLabel = item.LabelCap,
                    snapshotDefName = item.def?.defName,
                });
            }
            reward.items.Clear();
            reward.settled = true;
        }

        internal static void Discard(SettlementServicesWorldComponent domain, BoardJobRecord job)
        {
            BoardRewardRecord reward = job.reward;
            if (reward == null || reward.settled) return;

            DestroyBanked(domain, reward.items);
            reward.items.Clear();
            reward.settled = true;
        }

        private static void DestroyBanked(SettlementServicesWorldComponent domain, List<Thing> items)
        {
            foreach (Thing item in items.ToList())
            {
                if (item == null) continue;
                domain.ReleaseItemCustody(item);
                if (!item.Destroyed) item.Destroy();
            }
        }

        private static float RoundToQuarterHundred(float value) => Mathf.Round(value / 25f) * 25f;
    }
}

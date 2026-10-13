using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Grammar;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Framework.Board
{
    internal static class BoardJobGenerator
    {
        internal const int TicksPerDay = 60000;
        internal const int MaxPartySize = 4;
        internal const float MinDifficulty = 0.15f;
        internal const float MaxDifficulty = 0.85f;
        internal const float MinWorkDays = 1f;
        internal const float MaxWorkDays = 10f;
        internal const int MinExpiryDays = 6;
        internal const int MaxExpiryDays = 10;

        private const int MaxAttempts = 4;
        private const float RepeatDefWeightFactor = 0.35f;
        private const string FlavorRoot = "r_flavor";
        private const string TitleRoot = "r_title";

        internal static BoardJobRecord TryGenerate(Settlement settlement, ICollection<string> defNamesAlreadyOffered)
        {
            Faction provider = settlement?.Faction;
            if (provider == null) return null;

            List<SettlementBoardJobDef> defs = DefDatabase<SettlementBoardJobDef>.AllDefsListForReading
                .Where(d => d.IsEligibleFor(provider) && d.patterns.Any(IsUsablePattern))
                .ToList();
            if (defs.Count == 0) return null;

            float tierShift = Mathf.Clamp(0.04f * ((int)provider.def.techLevel - (int)TechLevel.Medieval), -0.08f, 0.12f);

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                SettlementBoardJobDef def = defs.RandomElementByWeight(d => d.selectionWeight * (defNamesAlreadyOffered != null && defNamesAlreadyOffered.Contains(d.defName) ? RepeatDefWeightFactor : 1f));
                float difficulty = Mathf.Clamp01(Rand.Range(MinDifficulty, MaxDifficulty) + def.difficultyBias + tierShift);

                BoardRolePattern pattern = def.patterns.Where(IsUsablePattern).RandomElementByWeight(p => p.weight);
                int partySize = PickPartySize(Mathf.Max(1, pattern.minPartySize), Mathf.Min(MaxPartySize, pattern.maxPartySize), difficulty);

                List<BoardSkillRequirement> requirements = BoardRequirementGenerator.Generate(pattern, partySize, difficulty);
                if (requirements == null) continue;

                BoardJobRecord offer = Build(def, settlement, provider, partySize, difficulty, requirements);
                if (offer != null) return offer;
            }
            return null;
        }

        private static bool IsUsablePattern(BoardRolePattern pattern) =>
            pattern != null && pattern.weight > 0f && !pattern.skills.NullOrEmpty() && pattern.minPartySize <= MaxPartySize;

        internal static int PickPartySize(int min, int max, float difficulty)
        {
            if (max <= min) return min;

            float ratio = Mathf.Clamp(0.25f + 0.5f * difficulty, 0.25f, 0.75f);
            var sizes = new List<int>();
            for (int n = min; n <= max; n++) sizes.Add(n);
            return sizes.RandomElementByWeight(n => Mathf.Pow(ratio, n - 1));
        }

        internal static float RollWorkDays(SettlementBoardJobDef def, int partySize, float difficulty)
        {
            float days = def.durationDays.RandomInRange
                * (0.8f + 0.6f * difficulty)
                * (1f + 0.08f * (partySize - 1))
                * Rand.Range(0.9f, 1.1f);
            return Mathf.Clamp(Mathf.Round(days * 2f) / 2f, MinWorkDays, MaxWorkDays);
        }

        private static BoardJobRecord Build(SettlementBoardJobDef def, Settlement settlement, Faction provider, int partySize, float difficulty, List<BoardSkillRequirement> requirements)
        {
            float workDays = RollWorkDays(def, partySize, difficulty);
            int now = Find.TickManager.TicksGame;

            var offer = new BoardJobRecord
            {
                defName = def.defName,
                settlementWorldObjectId = settlement.ID,
                settlementTile = settlement.Tile,
                providerFactionLoadId = provider.GetUniqueLoadID(),
                difficulty = difficulty,
                partySize = partySize,
                requirements = requirements,
                workTicks = Mathf.RoundToInt(workDays * TicksPerDay),
                createdTick = now,
                expiryTick = now + Rand.RangeInclusive(MinExpiryDays, MaxExpiryDays) * TicksPerDay,
                status = BoardJobStatus.Available,
                statusChangedTick = now,
                rewardBudget = BoardRewardService.ComputeBudget(def, partySize, workDays, difficulty, requirements),
            };

            offer.title = ResolveTitle(def, settlement, provider);
            offer.flavor = ResolveFlavor(def, settlement, provider);

            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null || !BoardRewardService.TryBankReward(domain, offer, provider)) return null;

            return offer;
        }

        private static string ResolveTitle(SettlementBoardJobDef def, Settlement settlement, Faction provider)
        {
            string resolved = ResolveGrammar(def, settlement, provider, TitleRoot);
            return resolved.NullOrEmpty() ? def.LabelCap.Resolve() : resolved;
        }

        private static string ResolveFlavor(SettlementBoardJobDef def, Settlement settlement, Faction provider)
        {
            string resolved = ResolveGrammar(def, settlement, provider, FlavorRoot);
            return resolved.NullOrEmpty()
                ? "SettlementServices.JobBoard.FlavorFallback".Translate(settlement.LabelCap, def.label).Resolve()
                : resolved;
        }

        private static string ResolveGrammar(SettlementBoardJobDef def, Settlement settlement, Faction provider, string root)
        {
            if (def.flavorRulePack == null) return null;
            if (!def.flavorRulePack.RulesPlusIncludes.Any(r => r.keyword == root)) return null;

            var request = default(GrammarRequest);
            request.Includes.Add(def.flavorRulePack);
            request.Rules.Add(new Rule_String("SETTLEMENT", settlement.LabelCap));
            request.Rules.Add(new Rule_String("FACTION", provider?.Name ?? string.Empty));

            string text = GrammarResolver.Resolve(root, request, "settlement board " + def.defName);
            return text.NullOrEmpty() || text == "err" ? null : text;
        }
    }
}

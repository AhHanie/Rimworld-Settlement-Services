using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Specialty;

namespace Settlement_Services.Services.Hiring
{
    internal static class HiringCandidateGenerator
    {
        private static readonly string[] SpecialtyWorkTypes = { "Doctoring", "Construction", "Research", "Handling", "Hunting" };

        private const int StandardSkillLevel = 6;
        private const int SkilledSkillLevel = 10;
        private const int ExpertSkillLevel = 14;
        private const int BaseWage = 200;
        private const int EliteMinimumGoodwill = 80;

        public static HiringCandidateRecord Generate(Settlement settlement, int candidateId, int expiryTicks)
        {
            bool eliteEligible = SettlementSpecialtyService.HasCapabilityTag(settlement, "ContractorServices")
                && settlement.Faction.PlayerGoodwill >= EliteMinimumGoodwill;

            float roll = Rand.Value;
            string tierKey = roll < 0.15f && eliteEligible ? "Expert" : roll < 0.5f ? "Skilled" : "Standard";
            int skillLevel = tierKey == "Expert" ? ExpertSkillLevel : tierKey == "Skilled" ? SkilledSkillLevel : StandardSkillLevel;
            int wageMultiplierPct = tierKey == "Expert" ? 220 : tierKey == "Skilled" ? 140 : 100;

            return new HiringCandidateRecord
            {
                candidateId = candidateId,
                specialtyWorkTypeDefName = SpecialtyWorkTypes.RandomElement(),
                skillLevel = skillLevel,
                qualityTierKey = tierKey,
                wage = BaseWage * wageMultiplierPct / 100,
                refusesHazardousWork = Rand.Chance(0.3f),
                expiryTick = Find.TickManager.TicksGame + expiryTicks,
            };
        }
    }
}

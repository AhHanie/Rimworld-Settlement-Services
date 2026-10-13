using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Specialty;

namespace Settlement_Services.Services.Hiring
{
    internal static class HiringCandidateSkillBiasService
    {
        public static void Apply(Settlement settlement, Pawn pawn)
        {
            if (settlement == null || pawn?.skills == null) return;

            var biases = SettlementSpecialtyService.GetSpecialties(settlement)
                .SelectMany(s => s.hiringSkillBiases)
                .Where(b => b != null && !b.skillDefName.NullOrEmpty());

            foreach (SpecialtyHiringSkillBias bias in biases)
            {
                SkillDef skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(bias.skillDefName);
                if (skillDef == null) continue;

                if (!Rand.Chance(bias.chance)) continue;

                SkillRecord record = pawn.skills.GetSkill(skillDef);
                if (record == null || record.TotallyDisabled) continue;

                record.Level = Mathf.Max(record.Level, bias.levelRange.RandomInRange);
            }
        }
    }
}

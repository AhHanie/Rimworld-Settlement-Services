using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Services.Education
{
    internal readonly struct EducationExperienceResult
    {
        public readonly SkillDef Skill;
        public readonly SkillRecord SkillRecord;
        public readonly SkillLessonOption Option;
        public readonly float QualityXp;
        public readonly float EstimatedXp;

        public EducationExperienceResult(SkillDef skill, SkillRecord skillRecord, SkillLessonOption option, float qualityXp, float estimatedXp)
        {
            Skill = skill;
            SkillRecord = skillRecord;
            Option = option;
            QualityXp = qualityXp;
            EstimatedXp = estimatedXp;
        }
    }

    internal static class EducationExperienceCalculator
    {
        public static bool TryCalculate(Pawn pawn, Settlement settlement, ServiceCategoryDef category, SkillLessonServiceDef def, IEnumerable<string> selectedOptionKeys, out EducationExperienceResult result)
        {
            result = default;
            if (pawn?.skills == null || def == null) return false;

            SkillDef skill = SkillEligibilityService.FindByKey(selectedOptionKeys);
            if (skill == null || !SkillEligibilityService.FindEligible(pawn).Contains(skill)) return false;

            SkillLessonOption option = def.OptionFor(selectedOptionKeys);
            if (option == null) return false;

            SkillRecord skillRecord = pawn.skills.GetSkill(skill);
            float quality = EducationQualityService.TrainingQuality(settlement, category);
            float qualityXp = option.baseXp * quality;
            float estimatedXp = qualityXp * skillRecord.LearnRateFactor();

            result = new EducationExperienceResult(skill, skillRecord, option, qualityXp, estimatedXp);
            return true;
        }

        public static IEnumerable<string> BuildSummaryLines(EducationExperienceResult result)
        {
            yield return "SettlementServices.Label.EducationBaseServiceXp".Translate(Mathf.RoundToInt(result.QualityXp));
            yield return PassionLine(result);
            yield return "SettlementServices.Label.EducationEstimatedXp".Translate(Mathf.RoundToInt(result.EstimatedXp));
        }

        private static string PassionLine(EducationExperienceResult result)
        {
            switch (result.SkillRecord.passion)
            {
                case Passion.Minor: return "SettlementServices.Label.EducationPassionMinor".Translate();
                case Passion.Major: return "SettlementServices.Label.EducationPassionMajor".Translate();
                case Passion.None:
                default: return "SettlementServices.Label.EducationPassionNone".Translate();
            }
        }
    }
}

using System.Collections.Generic;
using RimWorld;
using Verse;
using Settlement_Services.Domain.Records;

namespace Settlement_Services.Framework.Board
{
    internal static class BoardRequirementMatcher
    {
        internal static bool Qualifies(Pawn pawn, SkillDef skill, int minLevel, WorkTags requiredWorkTags)
        {
            if (pawn?.skills == null || skill == null) return false;

            SkillRecord record = pawn.skills.GetSkill(skill);
            if (record == null || record.TotallyDisabled) return false;
            if (requiredWorkTags != WorkTags.None && (pawn.CombinedDisabledWorkTags & requiredWorkTags) != WorkTags.None) return false;
            return record.Level >= minLevel;
        }

        internal static bool Qualifies(Pawn pawn, BoardSkillRequirement requirement) =>
            requirement != null && Qualifies(pawn, requirement.skill, requirement.minLevel, requirement.requiredWorkTags);

        internal static int QualifiedCount(BoardSkillRequirement requirement, IReadOnlyList<Pawn> party)
        {
            int count = 0;
            for (int i = 0; i < party.Count; i++)
            {
                if (Qualifies(party[i], requirement)) count++;
            }
            return count;
        }

        internal static bool IsSatisfied(IReadOnlyList<BoardSkillRequirement> requirements, int partySize, IReadOnlyList<Pawn> party)
        {
            if (party == null || party.Count != partySize) return false;

            var seen = new HashSet<Pawn>();
            for (int i = 0; i < party.Count; i++)
            {
                if (party[i] == null || !seen.Add(party[i])) return false;
            }

            for (int i = 0; i < requirements.Count; i++)
            {
                if (requirements[i]?.skill == null) return false;
                if (QualifiedCount(requirements[i], party) < requirements[i].requiredCount) return false;
            }
            return true;
        }
    }
}

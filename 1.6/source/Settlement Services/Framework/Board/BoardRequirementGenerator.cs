using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Framework.Board
{
    internal static class BoardRequirementGenerator
    {
        internal const int MinThreshold = 3;
        internal const int MaxThreshold = 18;

        private const float EasiestAnchor = 4f;
        private const float HardestAnchor = 13f;
        private const int MaxMixedSkills = 3;

        internal static List<BoardSkillRequirement> Generate(BoardRolePattern pattern, int partySize, float difficulty)
        {
            List<BoardSkillRoleTemplate> templates = pattern.skills.Where(t => t?.skill != null).ToList();
            if (templates.Count == 0) return null;

            var requirements = new List<BoardSkillRequirement>();
            switch (pattern.kind)
            {
                case BoardRoleKind.Specialist:
                    AddSpecialist(requirements, PickOne(templates), difficulty);
                    break;
                case BoardRoleKind.Crew:
                    AddCrew(requirements, PickOne(templates), partySize, difficulty);
                    break;
                case BoardRoleKind.SpecialistWithSupport:
                    AddSpecialistWithSupport(requirements, PickOne(templates), partySize, difficulty);
                    break;
                case BoardRoleKind.Mixed:
                    if (templates.Count < 2) return null;
                    AddMixed(requirements, templates, difficulty);
                    break;
            }

            return requirements.Count == 0 ? null : requirements;
        }

        internal static int CrewCount(int partySize) => partySize <= 2 ? partySize : partySize - 1;

        private static BoardSkillRoleTemplate PickOne(List<BoardSkillRoleTemplate> templates) =>
            templates.RandomElementByWeight(t => t.weight);

        private static void AddSpecialist(List<BoardSkillRequirement> requirements, BoardSkillRoleTemplate template, float difficulty)
        {
            int threshold = Threshold(difficulty, Rand.RangeInclusive(1, 2), 0);
            requirements.Add(Make(template, threshold, 1, BoardRoleKind.Specialist));
        }

        private static void AddCrew(List<BoardSkillRequirement> requirements, BoardSkillRoleTemplate template, int partySize, float difficulty)
        {
            int count = CrewCount(partySize);
            int threshold = Threshold(difficulty, 0, Mathf.Min(2, count - 1));
            requirements.Add(Make(template, threshold, count, BoardRoleKind.Crew));
        }

        private static void AddSpecialistWithSupport(List<BoardSkillRequirement> requirements, BoardSkillRoleTemplate template, int partySize, float difficulty)
        {
            int support = Threshold(difficulty, 0, Mathf.Min(2, partySize - 1));
            int specialist = Threshold(difficulty, Rand.RangeInclusive(1, 2), 0);

            if (specialist <= support) specialist = Mathf.Min(MaxThreshold, support + 1);
            if (specialist <= support) support = Mathf.Max(MinThreshold, specialist - 1);

            requirements.Add(Make(template, specialist, 1, BoardRoleKind.SpecialistWithSupport));
            requirements.Add(Make(template, support, partySize, BoardRoleKind.SpecialistWithSupport));
        }

        private static void AddMixed(List<BoardSkillRequirement> requirements, List<BoardSkillRoleTemplate> templates, float difficulty)
        {
            int skillCount = Mathf.Min(templates.Count, Rand.RangeInclusive(2, MaxMixedSkills));
            var remaining = new List<BoardSkillRoleTemplate>(templates);
            for (int i = 0; i < skillCount; i++)
            {
                BoardSkillRoleTemplate picked = PickOne(remaining);
                remaining.Remove(picked);
                requirements.Add(Make(picked, Threshold(difficulty, 0, 0), 1, BoardRoleKind.Mixed));
            }
        }

        private static int Threshold(float difficulty, int specialistBonus, int crewRelief)
        {
            float raw = Mathf.Lerp(EasiestAnchor, HardestAnchor, difficulty) + specialistBonus - crewRelief + Rand.Range(-1f, 1f);
            return Mathf.Clamp(Mathf.RoundToInt(raw), MinThreshold, MaxThreshold);
        }

        private static BoardSkillRequirement Make(BoardSkillRoleTemplate template, int threshold, int count, BoardRoleKind kind) =>
            new BoardSkillRequirement
            {
                skill = template.skill,
                minLevel = threshold,
                requiredCount = count,
                roleKind = kind,
                requiredWorkTags = template.requiredWorkTags,
            };
    }
}

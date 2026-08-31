using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Settlement_Services.Services.Education
{
    internal static class SkillEligibilityService
    {
        public const string SkillGroupKey = "SettlementServices.Label.SkillChoice";
        private const string KeyPrefix = "Skill_";
        private const string IconRoot = "SettlementServices/UI/Icons/";

        private static readonly Dictionary<string, string> IconByDefName = new Dictionary<string, string>
        {
            { "Intellectual", IconRoot + "brain" },
            { "Cooking", IconRoot + "cook" },
            { "Mining", IconRoot + "mining" },
            { "Construction", IconRoot + "hammer" },
            { "Artistic", IconRoot + "art" },
            { "Melee", IconRoot + "sword" },
            { "Shooting", IconRoot + "pistol" },
            { "Social", IconRoot + "mouth" },
            { "Crafting", IconRoot + "tools" },
            { "Animals", IconRoot + "animal-dog" },
            { "Medicine", IconRoot + "bandage" },
            { "Plants", IconRoot + "plant" },
        };

        public static IEnumerable<SkillDef> FindEligible(Pawn pawn)
        {
            if (pawn?.skills == null) yield break;
            foreach (SkillDef skillDef in DefDatabase<SkillDef>.AllDefsListForReading)
                if (!pawn.skills.GetSkill(skillDef).TotallyDisabled) yield return skillDef;
        }

        public static string KeyFor(SkillDef skillDef) => KeyPrefix + skillDef.defName;

        public static string IconPathFor(SkillDef skillDef) =>
            skillDef != null && IconByDefName.TryGetValue(skillDef.defName, out string path) ? path : null;

        public static SkillDef FindByKey(IEnumerable<string> selectedOptionKeys) =>
            selectedOptionKeys
                .Where(k => k.StartsWith(KeyPrefix))
                .Select(k => DefDatabase<SkillDef>.GetNamedSilentFail(k.Substring(KeyPrefix.Length)))
                .FirstOrDefault(sd => sd != null);
    }
}

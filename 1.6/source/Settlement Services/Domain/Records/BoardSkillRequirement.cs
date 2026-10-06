using RimWorld;
using Verse;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Domain.Records
{
    public class BoardSkillRequirement : IExposable
    {
        public SkillDef skill;
        public int minLevel;
        public int requiredCount = 1;
        public BoardRoleKind roleKind;
        public WorkTags requiredWorkTags = WorkTags.None;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref skill, "skill");
            Scribe_Values.Look(ref minLevel, "minLevel");
            Scribe_Values.Look(ref requiredCount, "requiredCount", 1);
            Scribe_Values.Look(ref roleKind, "roleKind");
            Scribe_Values.Look(ref requiredWorkTags, "requiredWorkTags", WorkTags.None);
        }
    }
}

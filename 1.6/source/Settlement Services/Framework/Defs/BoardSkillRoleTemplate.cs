using RimWorld;
using Verse;

namespace Settlement_Services.Framework.Defs
{
    public class BoardSkillRoleTemplate
    {
        public SkillDef skill;
        public float weight = 1f;
        public WorkTags requiredWorkTags = WorkTags.None;
    }
}

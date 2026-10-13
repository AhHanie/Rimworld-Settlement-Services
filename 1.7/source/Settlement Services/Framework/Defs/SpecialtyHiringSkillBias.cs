using Verse;

namespace Settlement_Services.Framework.Defs
{
    public class SpecialtyHiringSkillBias
    {
        public string skillDefName;
        public float chance = 0.35f;
        public IntRange levelRange = new IntRange(8, 12);
    }
}

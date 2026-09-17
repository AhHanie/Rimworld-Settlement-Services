using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Settlement_Services.Framework.Defs
{
    public class SettlementSpecialtyDef : Def
    {
        public bool disabled = false;

        public float selectionWeight = 1f;

        public TechLevel minTechLevel = TechLevel.Undefined;
        public TechLevel maxTechLevel = TechLevel.Undefined;

        public List<string> requiredFactionCategoryTags;

        public bool requiresFactionHasIdeo = false;

        public SettlementCapabilityModifiers modifiers = new SettlementCapabilityModifiers();
        public List<SpecialtyStockModifier> stockModifiers = new List<SpecialtyStockModifier>();
        public List<SpecialtyHiringSkillBias> hiringSkillBiases = new List<SpecialtyHiringSkillBias>();

        public string iconTexPath;

        public List<string> tooltipEffectKeys = new List<string>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;
            if (selectionWeight < 0f) yield return "selectionWeight must be >= 0.";
            if ((modifiers.qualityOffset != 0f || modifiers.priceModifierPct != 0f || modifiers.durationMultiplierOffset != 0f)
                && modifiers.relevantCategoryDefNames.NullOrEmpty() && modifiers.relevantServiceDefNames.NullOrEmpty())
                yield return "has a nonzero quality/price/duration modifier but no relevantCategoryDefNames or relevantServiceDefNames -- it will never apply to anything.";

            if (!hiringSkillBiases.NullOrEmpty())
            {
                foreach (SpecialtyHiringSkillBias bias in hiringSkillBiases)
                {
                    if (bias == null || bias.skillDefName.NullOrEmpty())
                    {
                        yield return "has a hiringSkillBiases entry with no skillDefName.";
                        continue;
                    }

                    if (DefDatabase<SkillDef>.GetNamedSilentFail(bias.skillDefName) == null)
                        yield return $"has a hiringSkillBiases entry with skillDefName '{bias.skillDefName}' that does not resolve to a SkillDef.";

                    if (bias.chance < 0f || bias.chance > 1f)
                        yield return $"has a hiringSkillBiases entry for '{bias.skillDefName}' with chance {bias.chance} outside 0..1.";

                    if (bias.levelRange.min < 0 || bias.levelRange.max > 20 || bias.levelRange.min > bias.levelRange.max)
                        yield return $"has a hiringSkillBiases entry for '{bias.skillDefName}' with an invalid levelRange ({bias.levelRange.min}~{bias.levelRange.max}); it must be within 0..20.";
                }
            }
        }
    }
}

using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Settlement_Services.Framework.Defs
{
    public class SettlementBoardJobDef : Def
    {
        public bool disabled = false;

        public float selectionWeight = 1f;

        public List<BoardRolePattern> patterns = new List<BoardRolePattern>();

        public FloatRange durationDays = new FloatRange(2f, 4f);
        public float difficultyBias = 0f;
        public float rewardMultiplier = 1f;

        public TechLevel minTechLevel = TechLevel.Undefined;
        public TechLevel maxTechLevel = TechLevel.Undefined;
        public List<string> requiredFactionCategoryTags;

        public RulePackDef flavorRulePack;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;

            if (selectionWeight < 0f) yield return "selectionWeight must be >= 0.";
            if (rewardMultiplier <= 0f) yield return "rewardMultiplier must be > 0.";
            if (durationDays.min <= 0f || durationDays.max < durationDays.min) yield return "durationDays must be a positive range.";
            if (difficultyBias < -0.5f || difficultyBias > 0.5f) yield return "difficultyBias should stay within -0.5..0.5.";

            if (patterns.NullOrEmpty())
            {
                yield return "has no role patterns.";
                yield break;
            }

            bool anyUsable = false;
            foreach (BoardRolePattern pattern in patterns)
            {
                if (pattern == null)
                {
                    yield return "has a null role pattern.";
                    continue;
                }

                foreach (string e in pattern.ConfigErrors(defName)) yield return e;
                if (pattern.weight > 0f && !pattern.skills.NullOrEmpty()) anyUsable = true;
            }

            if (!anyUsable) yield return "has no usable role pattern (positive weight with at least one skill).";
        }

        public bool IsEligibleFor(Faction faction)
        {
            if (disabled || selectionWeight <= 0f || faction?.def == null) return false;
            if (minTechLevel != TechLevel.Undefined && faction.def.techLevel < minTechLevel) return false;
            if (maxTechLevel != TechLevel.Undefined && faction.def.techLevel > maxTechLevel) return false;
            if (!requiredFactionCategoryTags.NullOrEmpty() && !requiredFactionCategoryTags.Contains(faction.def.categoryTag)) return false;
            return true;
        }
    }
}

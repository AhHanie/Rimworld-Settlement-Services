using System.Collections.Generic;
using System.Linq;
using Verse;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Services.Education
{
    public class SkillLessonServiceDef : SettlementServiceDef
    {
        public List<SkillLessonOption> lessonOptions = new List<SkillLessonOption>();

        public SkillLessonOption OptionFor(IEnumerable<string> selectedOptionKeys) =>
            lessonOptions?.FirstOrDefault(o => selectedOptionKeys.Contains(o.key));

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;

            if (lessonOptions.NullOrEmpty())
            {
                yield return "lessonOptions must contain at least one entry.";
                yield break;
            }

            var seenKeys = new HashSet<string>();
            for (int i = 0; i < lessonOptions.Count; i++)
            {
                SkillLessonOption option = lessonOptions[i];
                if (option == null) { yield return $"lessonOptions[{i}] is null."; continue; }

                string label = option.key.NullOrEmpty() ? $"index {i}" : option.key;
                if (option.key.NullOrEmpty()) yield return $"lessonOptions[{i}] has a missing or empty key.";
                else if (!seenKeys.Add(option.key)) yield return $"lessonOptions[{i}] ({label}) duplicates an earlier option key.";

                if (option.labelKey.NullOrEmpty()) yield return $"lessonOptions[{i}] ({label}) has a missing or empty labelKey.";

                if (float.IsNaN(option.baseXp) || float.IsInfinity(option.baseXp)) yield return $"lessonOptions[{i}] ({label}) baseXp must be finite.";
                else if (option.baseXp < 0f) yield return $"lessonOptions[{i}] ({label}) baseXp must be >= 0.";

                if (float.IsNaN(option.durationMultiplier) || float.IsInfinity(option.durationMultiplier))
                    yield return $"lessonOptions[{i}] ({label}) durationMultiplier must be finite.";
                else if (option.durationMultiplier <= 0f)
                    yield return $"lessonOptions[{i}] ({label}) durationMultiplier must be > 0.";

                if (float.IsNaN(option.priceMultiplier) || float.IsInfinity(option.priceMultiplier)) yield return $"lessonOptions[{i}] ({label}) priceMultiplier must be finite.";
                else if (option.priceMultiplier < 0f) yield return $"lessonOptions[{i}] ({label}) priceMultiplier must be >= 0.";
            }
        }
    }

    public class SkillLessonOption
    {
        public string key;
        public string labelKey;
        public float baseXp;
        public float durationMultiplier = 1f;
        public float priceMultiplier = 0f;
    }
}

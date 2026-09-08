using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Settlement_Services.Framework.Defs
{
    public class ServiceEventDef : Def
    {
        public bool disabled = false;

        public ServiceEventWeightClass weightClass = ServiceEventWeightClass.MinorPositive;
        public ServiceEventTriggerPhase triggerPhase = ServiceEventTriggerPhase.OnComplete;
        public float selectionWeight = 1f;

        public List<string> eligibleServiceDefNames;
        public List<string> eligibleCategoryDefNames;
        public bool appliesToAllCategories = false;
        public List<string> excludedServiceDefNames;

        public int cooldownTicks = 300000;

        public bool requiresPawnTarget = false;
        public List<string> requiredTraitDefNames;
        public List<string> requiredMemeDefNames;
        public TechLevel minTechLevel = TechLevel.Undefined;

        public ServiceEventEffects effects;
        public List<ServiceEventChoice> choices;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;

            int positiveSelectorCount = 0;
            if (!eligibleServiceDefNames.NullOrEmpty()) positiveSelectorCount++;
            if (!eligibleCategoryDefNames.NullOrEmpty()) positiveSelectorCount++;
            if (appliesToAllCategories) positiveSelectorCount++;

            if (positiveSelectorCount == 0)
                yield return "must set exactly one of eligibleServiceDefNames, eligibleCategoryDefNames, or appliesToAllCategories.";
            if (positiveSelectorCount > 1)
                yield return "must set only one of eligibleServiceDefNames, eligibleCategoryDefNames, and appliesToAllCategories; they are mutually exclusive.";

            foreach (string e in ValidateNameList(eligibleServiceDefNames, "eligibleServiceDefNames")) yield return e;
            foreach (string e in ValidateNameList(eligibleCategoryDefNames, "eligibleCategoryDefNames")) yield return e;

            if (choices.NullOrEmpty() && effects == null)
                yield return "must set either effects or exactly two choices.";
            if (!choices.NullOrEmpty() && effects != null)
                yield return "must not set both effects and choices.";
            if (!choices.NullOrEmpty() && choices.Count != 2)
                yield return $"choices must contain exactly 2 entries, found {choices.Count}.";

            if (RequiresPawn(effects) && !requiresPawnTarget)
                yield return "effects reference a pawn but requiresPawnTarget is false.";
            if (!choices.NullOrEmpty())
                foreach (ServiceEventChoice c in choices)
                    if (RequiresPawn(c.effects) && !requiresPawnTarget)
                        yield return $"choice '{c.labelKey}' effects reference a pawn but requiresPawnTarget is false.";

            if (effects != null && effects.durationDeltaTicks != 0 && triggerPhase == ServiceEventTriggerPhase.OnComplete)
                yield return "durationDeltaTicks has no effect at OnComplete; use OnStart or DuringService.";
        }

        private static bool RequiresPawn(ServiceEventEffects e) =>
            e != null && (e.experienceSkillDefName != null || e.thoughtDefName != null || e.hediffDefName != null);

        private static IEnumerable<string> ValidateNameList(List<string> names, string fieldName)
        {
            if (names.NullOrEmpty()) yield break;

            var seen = new HashSet<string>();
            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    yield return $"{fieldName} contains a blank entry.";
                    continue;
                }
                if (!seen.Add(name))
                    yield return $"{fieldName} contains duplicate entry '{name}'.";
            }
        }
    }
}

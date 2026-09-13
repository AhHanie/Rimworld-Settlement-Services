using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Events;

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

        public Type workerClass;

        [Unsaved(false)]
        private ServiceEventWorker workerInt;

        public ServiceEventWorker Worker
        {
            get
            {
                if (workerClass == null) return null;
                if (workerInt == null)
                {
                    workerInt = (ServiceEventWorker)Activator.CreateInstance(workerClass);
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

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

            if (choices.NullOrEmpty() && effects == null && workerClass == null)
                yield return "must set effects, exactly two choices, or workerClass.";
            if (!choices.NullOrEmpty() && effects != null)
                yield return "must not set both effects and choices.";
            if (!choices.NullOrEmpty() && choices.Count != 2)
                yield return $"choices must contain exactly 2 entries, found {choices.Count}.";

            if (workerClass != null)
            {
                if (!typeof(ServiceEventWorker).IsAssignableFrom(workerClass))
                    yield return $"workerClass {workerClass} does not derive from ServiceEventWorker.";
                else if (workerClass.IsAbstract)
                    yield return $"workerClass {workerClass} is abstract.";
                else if (workerClass.GetConstructor(Type.EmptyTypes) == null)
                    yield return $"workerClass {workerClass} lacks a public parameterless constructor.";
            }

            if (RequiresPawn(effects) && !requiresPawnTarget)
                yield return "effects reference a pawn but requiresPawnTarget is false.";
            if (!choices.NullOrEmpty())
                foreach (ServiceEventChoice c in choices)
                    if (RequiresPawn(c.effects) && !requiresPawnTarget)
                        yield return $"choice '{c.labelKey}' effects reference a pawn but requiresPawnTarget is false.";

            foreach (string e in ValidateDurationEffect(effects, "effects", triggerPhase)) yield return e;
            if (!choices.NullOrEmpty())
                foreach (ServiceEventChoice c in choices)
                    foreach (string e in ValidateDurationEffect(c.effects, $"choice '{c.labelKey}' effects", triggerPhase))
                        yield return e;

            foreach (string e in ValidateRefundEffect(effects, "effects")) yield return e;
            if (!choices.NullOrEmpty())
                foreach (ServiceEventChoice c in choices)
                    foreach (string e in ValidateRefundEffect(c.effects, $"choice '{c.labelKey}' effects"))
                        yield return e;

            ServiceEventItemRewardExtension itemRewardExtension = GetModExtension<ServiceEventItemRewardExtension>();
            if (itemRewardExtension != null)
            {
                if (itemRewardExtension.thingSetMakerDef == null)
                    yield return "ServiceEventItemRewardExtension.thingSetMakerDef did not resolve.";
                if (float.IsNaN(itemRewardExtension.rewardValueFactor) || float.IsInfinity(itemRewardExtension.rewardValueFactor) || itemRewardExtension.rewardValueFactor <= 0f)
                    yield return $"ServiceEventItemRewardExtension.rewardValueFactor must be a finite, positive number, found {itemRewardExtension.rewardValueFactor}.";
                if (float.IsNaN(itemRewardExtension.minimumRewardValue) || float.IsInfinity(itemRewardExtension.minimumRewardValue) || itemRewardExtension.minimumRewardValue < 0f)
                    yield return $"ServiceEventItemRewardExtension.minimumRewardValue must be a finite, non-negative number, found {itemRewardExtension.minimumRewardValue}.";
            }
        }

        private static bool RequiresPawn(ServiceEventEffects e) =>
            e != null && (e.experienceSkillDefName != null || e.thoughtDefName != null || e.hediffDefName != null);

        private static IEnumerable<string> ValidateDurationEffect(ServiceEventEffects e, string fieldName, ServiceEventTriggerPhase phase)
        {
            if (e == null) yield break;

            if (float.IsNaN(e.durationDeltaPct) || float.IsInfinity(e.durationDeltaPct))
                yield return $"{fieldName}.durationDeltaPct must be a finite number, found {e.durationDeltaPct}.";
            else if (e.durationDeltaPct < -1.0f)
                yield return $"{fieldName}.durationDeltaPct must be at least -1.0 (a 100% reduction), found {e.durationDeltaPct}.";

            bool hasDurationEffect = e.durationDeltaTicks != 0 || e.durationDeltaPct != 0f;
            if (hasDurationEffect && phase == ServiceEventTriggerPhase.OnComplete)
                yield return $"{fieldName}: durationDeltaTicks/durationDeltaPct have no effect at OnComplete; use OnStart or DuringService.";
        }

        private static IEnumerable<string> ValidateRefundEffect(ServiceEventEffects e, string fieldName)
        {
            if (e == null) yield break;

            if (float.IsNaN(e.refundFraction) || float.IsInfinity(e.refundFraction))
                yield return $"{fieldName}.refundFraction must be a finite number, found {e.refundFraction}.";
            else if (e.refundFraction < 0f)
                yield return $"{fieldName}.refundFraction must be non-negative, found {e.refundFraction}.";
            else if (e.refundFraction > ServiceEventEffects.MaxRefundFraction)
                yield return $"{fieldName}.refundFraction must not exceed {ServiceEventEffects.MaxRefundFraction}, found {e.refundFraction}.";

            if (e.refundAmount > 0 && e.refundFraction > 0f)
                yield return $"{fieldName} must not set both refundAmount and refundFraction.";
        }

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

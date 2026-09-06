using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Defs
{
    public class SettlementServiceDef : Def
    {
        public ServiceCategoryDef category;
        public Type workerClass;

        public bool disabled = false;

        public TechLevel requiredTechLevel = TechLevel.Undefined;

        public List<string> requiredFactionTags;

        public int minimumGoodwill = int.MinValue;

        public int minimumCost = 0;
        public float wealthScale = 0.003f;
        public bool difficultyScaling = true;

        public int duration = 0;
        public List<ServicePriorityTierDef> priorityTiers;
        public string priorityTierHeaderKey;
        public bool requireExplicitPriorityTier = false;
        public string durationLabelKey;

        public float eventChancePct = 0.15f;

        public List<ServiceStockRequirement> stockRequirements;

        public List<string> requiredCapabilityTags;

        public ServiceTargetRule targetRule = ServiceTargetRule.None;

        public bool allowRemoteDiscovery = false;
        public bool allowRemoteRequest = false;

        public bool allowGroupTarget = false;

        public bool restoresParticipantFoodAndRest = false;

        public ServiceBatchMode batchMode = ServiceBatchMode.None;
        public int maxBatchCount = 0;

        public string iconTexPath;

        public ServiceBatchMode EffectiveBatchMode =>
            batchMode != ServiceBatchMode.None ? batchMode : (allowGroupTarget ? ServiceBatchMode.Targets : ServiceBatchMode.None);

        [Unsaved(false)]
        private SettlementServiceWorker workerInt;

        public SettlementServiceWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (SettlementServiceWorker)Activator.CreateInstance(workerClass);
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;

            if (workerClass == null)
            {
                yield return "workerClass is null.";
                yield break;
            }

            if (!typeof(SettlementServiceWorker).IsAssignableFrom(workerClass))
                yield return $"workerClass {workerClass} does not derive from SettlementServiceWorker.";
            if (category == null) yield return "category is null.";
            if (minimumCost < 0) yield return "minimumCost must be >= 0.";
            if (wealthScale < 0f) yield return "wealthScale must be >= 0.";
            if (duration < 0) yield return "duration must be >= 0.";
            if (eventChancePct < 0f || eventChancePct > 1f) yield return "eventChancePct must be between 0 and 1.";
            if (allowRemoteRequest && !allowRemoteDiscovery)
                yield return "allowRemoteRequest requires allowRemoteDiscovery.";
            if (allowRemoteRequest && targetRule != ServiceTargetRule.None)
                yield return "allowRemoteRequest requires targetRule to be None.";

            if (maxBatchCount < 0) yield return "maxBatchCount must be >= 0.";
            if (batchMode == ServiceBatchMode.Targets && targetRule == ServiceTargetRule.None)
                yield return "batchMode Targets requires targetRule to not be None.";
            if (batchMode == ServiceBatchMode.Quantity && targetRule != ServiceTargetRule.None)
                yield return "batchMode Quantity requires targetRule to be None.";

            if (!priorityTiers.NullOrEmpty())
            {
                var seenKeys = new HashSet<string>();
                int defaultTierCount = 0;
                for (int i = 0; i < priorityTiers.Count; i++)
                {
                    ServicePriorityTierDef tier = priorityTiers[i];
                    if (tier == null) { yield return $"priorityTiers[{i}] is null."; continue; }

                    string label = tier.key.NullOrEmpty() ? $"index {i}" : tier.key;
                    if (tier.key.NullOrEmpty()) yield return $"priorityTiers[{i}] has a missing or empty key.";
                    else if (!seenKeys.Add(tier.key)) yield return $"priorityTiers[{i}] ({label}) duplicates an earlier tier key.";

                    if (float.IsNaN(tier.durationMultiplier) || float.IsInfinity(tier.durationMultiplier))
                        yield return $"priorityTiers[{i}] ({label}) durationMultiplier must be finite.";
                    else if (tier.durationMultiplier <= 0f)
                        yield return $"priorityTiers[{i}] ({label}) durationMultiplier must be > 0.";

                    if (tier.isDefaultTier) defaultTierCount++;
                }

                if (defaultTierCount > 1) yield return "priorityTiers has more than one tier marked isDefaultTier.";
                if (requireExplicitPriorityTier && defaultTierCount == 0)
                    yield return "requireExplicitPriorityTier is set but no priorityTiers entry is marked isDefaultTier.";
            }
            else if (requireExplicitPriorityTier)
            {
                yield return "requireExplicitPriorityTier is set but priorityTiers is empty.";
            }
        }
    }
}

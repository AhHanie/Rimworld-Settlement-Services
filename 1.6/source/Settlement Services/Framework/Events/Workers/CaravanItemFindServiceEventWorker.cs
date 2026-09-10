using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events.Workers
{
    public class CaravanItemFindServiceEventWorker : ServiceEventWorker
    {
        private static readonly SimpleCurve RewardValueFromPointsCurve = new SimpleCurve
        {
            new CurvePoint(200f, 550f),
            new CurvePoint(400f, 1100f),
            new CurvePoint(800f, 1600f),
            new CurvePoint(1600f, 2600f),
            new CurvePoint(3200f, 3600f),
            new CurvePoint(20000f, 20000f),
        };

        public override bool CanApply(ServiceJobContext ctx)
        {
            ServiceEventItemRewardExtension extension = def.GetModExtension<ServiceEventItemRewardExtension>();
            if (extension?.thingSetMakerDef?.root == null) return false;

            FloatRange? marketValueRange = CalculateMarketValueRange(extension);
            if (marketValueRange == null) return false;

            return extension.thingSetMakerDef.root.CanGenerate(BuildParams(marketValueRange.Value));
        }

        public override void Apply(ServiceJobContext ctx)
        {
            ServiceEventItemRewardExtension extension = def.GetModExtension<ServiceEventItemRewardExtension>();
            if (extension?.thingSetMakerDef?.root == null)
            {
                Settlement_Services.SupportLog.Error($"{def.defName}: missing or unresolved ServiceEventItemRewardExtension.thingSetMakerDef.");
                return;
            }

            FloatRange? marketValueRange = CalculateMarketValueRange(extension);
            if (marketValueRange == null)
            {
                Settlement_Services.SupportLog.Error($"{def.defName}: could not calculate a reward market value range.");
                return;
            }

            List<Thing> generated = extension.thingSetMakerDef.root.Generate(BuildParams(marketValueRange.Value));
            if (generated.NullOrEmpty())
            {
                Settlement_Services.SupportLog.Warning($"{def.defName}: item reward generation produced no items.");
                return;
            }

            foreach (Thing item in generated)
            {
                if (!ctx.Domain.TryAddJobResult(ctx.Job.jobId, item))
                {
                    Settlement_Services.SupportLog.Error($"{def.defName}: failed to bank generated item '{item.LabelCap}' as a job result.");
                    item.Destroy();
                }
            }
        }

        private static ThingSetMakerParams BuildParams(FloatRange marketValueRange) => new ThingSetMakerParams
        {
            totalMarketValueRange = marketValueRange,
            qualityGenerator = QualityGenerator.Reward,
        };

        private static FloatRange? CalculateMarketValueRange(ServiceEventItemRewardExtension extension)
        {
            if (Find.World == null) return null;

            float points = StorytellerUtility.DefaultThreatPointsNow(Find.World);
            float nominalValue = RewardValueFromPointsCurve.Evaluate(points);
            nominalValue *= Find.Storyteller.difficulty.EffectiveQuestRewardValueFactor;
            nominalValue *= extension.rewardValueFactor;
            if (extension.minimumRewardValue > 0f) nominalValue = Mathf.Max(nominalValue, extension.minimumRewardValue);

            return new FloatRange(0.7f, 1.3f) * nominalValue;
        }
    }
}

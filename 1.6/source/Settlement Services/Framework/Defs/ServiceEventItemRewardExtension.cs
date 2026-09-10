using RimWorld;
using Verse;

namespace Settlement_Services.Framework.Defs
{
    public class ServiceEventItemRewardExtension : DefModExtension
    {
        public ThingSetMakerDef thingSetMakerDef;
        public float rewardValueFactor = 1f;
        public float minimumRewardValue;
    }
}

using System.Collections.Generic;

namespace Settlement_Services.Framework.Defs
{
    public class SettlementCapabilityModifiers
    {
        public List<string> capabilityTags = new List<string>();

        public float qualityOffset = 0f;
        public float priceModifierPct = 0f;
        public float durationMultiplierOffset = 0f;

        public List<string> relevantCategoryDefNames = new List<string>();
    }
}

namespace Settlement_Services.Framework.Defs
{
    public class ServicePriorityTierDef
    {
        public string key;
        public string label;
        public float durationMultiplier = 1f;
        public float costSurchargePct = 0f;
        public int minimumGoodwill = int.MinValue;
    }
}

using UnityEngine;

namespace Settlement_Services.Framework.Pricing
{
    internal static class WealthScaling
    {
        private const float WealthReference = 500000f;
        private const float TaperExponent = 0.4f;

        public static float EffectiveWealth(float rawWealth)
        {
            if (rawWealth <= 0f) return rawWealth;
            float ratio = WealthReference / (WealthReference + rawWealth);
            return rawWealth * Mathf.Pow(ratio, TaperExponent);
        }
    }
}

namespace Settlement_Services.Framework.Pricing
{
    internal static class GoodwillBands
    {
        private static readonly (int threshold, float basePct)[] Bands =
        {
            (100, 0.25f),
            (80, 0.20f),
            (60, 0.15f),
            (40, 0.10f),
            (20, 0.05f),
        };

        public static float DiscountPctFor(float goodwill)
        {
            foreach ((int threshold, float basePct) in Bands)
            {
                if (goodwill >= threshold) return -basePct * ModSettings.Current.goodwillDiscountScalePct;
            }
            return 0f;
        }
    }
}

using RimWorld.Planet;
using UnityEngine;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Specialty;

namespace Settlement_Services.Services.Genetics
{
    internal static class BiosculpterQualityService
    {
        private const float DurationReductionPerQualityPoint = 0.50f;
        private const float MinDurationMultiplier = 0.10f;

        public static float Quality(Settlement settlement, ServiceCategoryDef category) =>
            settlement == null ? 1f : Mathf.Max(1f, 1f + SettlementSpecialtyService.TotalQualityOffset(settlement, category));

        public static float DurationMultiplier(Settlement settlement, ServiceCategoryDef category)
        {
            float quality = Quality(settlement, category);
            return Mathf.Max(MinDurationMultiplier, 1f - (quality - 1f) * DurationReductionPerQualityPoint);
        }
    }
}

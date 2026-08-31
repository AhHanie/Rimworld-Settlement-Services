using UnityEngine;
using RimWorld.Planet;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Specialty;

namespace Settlement_Services.Services.Crafting
{
    internal static class CraftingQualityService
    {
        private const float BaselineQuality = 0.6f;

        public static float Quality(Settlement settlement, ServiceCategoryDef category) =>
            settlement == null ? BaselineQuality : Mathf.Clamp01(BaselineQuality + SettlementSpecialtyService.TotalQualityOffset(settlement, category));
    }
}

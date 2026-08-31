using UnityEngine;
using RimWorld.Planet;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Specialty;

namespace Settlement_Services.Services.Medical
{
    internal static class MedicalQualityService
    {
        private const float BaselineQuality = 0.65f;

        public static float TreatmentQuality(Settlement settlement, ServiceCategoryDef category) =>
            settlement == null ? BaselineQuality : Mathf.Clamp01(BaselineQuality + SettlementSpecialtyService.TotalQualityOffset(settlement, category));
    }
}

using RimWorld.Planet;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Specialty;

namespace Settlement_Services.Services.Education
{
    internal static class EducationQualityService
    {
        private const float BaselineQuality = 1f;

        public static float TrainingQuality(Settlement settlement, ServiceCategoryDef category) =>
            settlement == null ? BaselineQuality : BaselineQuality + SettlementSpecialtyService.TotalQualityOffset(settlement, category);
    }
}

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Settlement_Services.Services.Education
{
    internal static class ResearchProjectEligibilityService
    {
        public static bool IsOrdinaryStartable(ResearchProjectDef proj) =>
            proj.baseCost > 0f && proj.CanStartNow;

        public static IEnumerable<ResearchProjectDef> EligibleProjects() =>
            DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(IsOrdinaryStartable);
    }
}

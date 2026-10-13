using System.Collections.Generic;
using Settlement_Services.Framework.Compat.RimPacts;
using Settlement_Services.Framework.Compat.RimEducation;
using Settlement_Services.Framework.Compat.VehicleFramework;
using Settlement_Services.Framework.Compat.ChargeableHediffs;
using Settlement_Services.Framework.Compat.ProgressionEducation;
using Settlement_Services.Framework.Compat.LifeLessons;
using Settlement_Services.Framework.Compat.Empire;

namespace Settlement_Services.Framework.Compatibility
{
    internal static class CompatibilityModuleCatalog
    {
        internal static IEnumerable<ISettlementServicesCompatibilityModule> CreateModules()
        {
            yield return new RimPactsCompatibilityModule();
            yield return new RimEducationCompatibilityModule();
            yield return new VehicleFrameworkCompatibilityModule();
            yield return new ChargeableHediffsCompatibilityModule();
            yield return new ProgressionEducationCompatibilityModule();
            yield return new LifeLessonsCompatibilityModule();
            yield return new EmpireCompatibilityModule();
        }
    }
}

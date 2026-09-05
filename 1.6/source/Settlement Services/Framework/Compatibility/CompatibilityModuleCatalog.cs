using System.Collections.Generic;
using Settlement_Services.Framework.Compat.RimPacts;
using Settlement_Services.Framework.Compat.RimEducation;
using Settlement_Services.Framework.Compat.VehicleFramework;
using Settlement_Services.Framework.Compat.ChargeableHediffs;

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
        }
    }
}

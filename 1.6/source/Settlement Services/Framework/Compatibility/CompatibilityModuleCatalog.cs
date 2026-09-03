using System.Collections.Generic;
using Settlement_Services.Framework.Compat.RimPacts;

namespace Settlement_Services.Framework.Compatibility
{
    internal static class CompatibilityModuleCatalog
    {
        internal static IEnumerable<ISettlementServicesCompatibilityModule> CreateModules()
        {
            yield return new RimPactsCompatibilityModule();
        }
    }
}

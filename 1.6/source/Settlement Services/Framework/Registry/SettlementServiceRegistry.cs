using System.Collections.Generic;
using System.Linq;
using Verse;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Framework.Registry
{
    public static class SettlementServiceRegistry
    {
        private static HashSet<SettlementServiceDef> disabledDefs = new HashSet<SettlementServiceDef>();
        private static readonly List<ISettlementServiceProvider> providers = new List<ISettlementServiceProvider>();

        public static IEnumerable<SettlementServiceDef> AllValid =>
            DefDatabase<SettlementServiceDef>.AllDefsListForReading
                .Concat(providers.SelectMany(p => p.ProvideDefs()))
                .Where(d => !disabledDefs.Contains(d) && !d.disabled);

        public static IEnumerable<SettlementServiceDef> ForCategory(ServiceCategoryDef category) =>
            AllValid.Where(d => d.category == category);

        public static void RegisterProvider(ISettlementServiceProvider provider)
        {
            providers.Add(provider);
        }

        public static void ValidateAll()
        {
            IEnumerable<SettlementServiceDef> allDefs = DefDatabase<SettlementServiceDef>.AllDefsListForReading
                .Concat(providers.SelectMany(p => p.ProvideDefs()));
            disabledDefs = SettlementServiceDefValidation.Validate(allDefs);
        }
    }
}

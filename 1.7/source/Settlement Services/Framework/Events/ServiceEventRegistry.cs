using System.Collections.Generic;
using System.Linq;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Registry;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events
{
    public static class ServiceEventRegistry
    {
        private static HashSet<ServiceEventDef> disabledDefs = new HashSet<ServiceEventDef>();

        public static void ValidateAll()
        {
            disabledDefs = ServiceEventDefValidation.Validate(DefDatabase<ServiceEventDef>.AllDefsListForReading);
        }

        public static IEnumerable<ServiceEventDef> EligibleEvents(SettlementServiceDef serviceDef, ServiceJobContext ctx)
        {
            return DefDatabase<ServiceEventDef>.AllDefsListForReading
                .Concat(serviceDef.Worker.GetEventPool(ctx))
                .Distinct()
                .Where(e => !disabledDefs.Contains(e) && !e.disabled)
                .Where(e => ServiceEventEligibility.IsEligible(e, serviceDef, ctx));
        }
    }
}

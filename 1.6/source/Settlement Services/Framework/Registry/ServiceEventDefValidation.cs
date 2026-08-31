using System.Collections.Generic;
using System.Linq;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Framework.Registry
{
    public static class ServiceEventDefValidation
    {
        public static HashSet<ServiceEventDef> Validate(IEnumerable<ServiceEventDef> defs)
        {
            var disabled = new HashSet<ServiceEventDef>();
            foreach (ServiceEventDef def in defs)
            {
                List<string> errors = def.ConfigErrors().ToList();
                if (errors.Count == 0) continue;
                disabled.Add(def);
                foreach (string e in errors) Settlement_Services.SupportLog.Error($"{def.defName}: {e}");
            }
            return disabled;
        }
    }
}

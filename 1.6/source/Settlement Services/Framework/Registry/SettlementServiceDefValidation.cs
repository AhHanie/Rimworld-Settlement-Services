using System.Collections.Generic;
using System.Linq;
using Verse;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Framework.Registry
{
    public static class SettlementServiceDefValidation
    {
        public static HashSet<SettlementServiceDef> Validate(IEnumerable<SettlementServiceDef> defs)
        {
            var disabled = new HashSet<SettlementServiceDef>();

            foreach (SettlementServiceDef def in defs)
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

using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Settlement_Services.Services.Hospitality
{
    public static class HospitalityPackageDefValidation
    {
        public static HashSet<HospitalityPackageDef> Validate(IEnumerable<HospitalityPackageDef> defs)
        {
            var disabled = new HashSet<HospitalityPackageDef>();

            foreach (HospitalityPackageDef def in defs)
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

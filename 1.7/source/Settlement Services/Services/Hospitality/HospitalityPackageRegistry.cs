using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Settlement_Services.Services.Hospitality
{
    public static class HospitalityPackageRegistry
    {
        private static HashSet<HospitalityPackageDef> disabledDefs = new HashSet<HospitalityPackageDef>();

        public static IEnumerable<HospitalityPackageDef> AllValid =>
            DefDatabase<HospitalityPackageDef>.AllDefsListForReading.Where(d => !disabledDefs.Contains(d));

        public static void ValidateAll()
        {
            disabledDefs = HospitalityPackageDefValidation.Validate(DefDatabase<HospitalityPackageDef>.AllDefsListForReading);
        }
    }
}

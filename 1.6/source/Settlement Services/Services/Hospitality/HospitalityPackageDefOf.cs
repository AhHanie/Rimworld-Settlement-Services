using RimWorld;
using Verse;

namespace Settlement_Services.Services.Hospitality
{
    [DefOf]
    public static class HospitalityPackageDefOf
    {
        public static HospitalityPackageDef HospitalityPackage_Recreation;
        public static HospitalityPackageDef HospitalityPackage_Spa;
        public static HospitalityPackageDef HospitalityPackage_Tavern;
        public static HospitalityPackageDef HospitalityPackage_Adult;
        public static HospitalityPackageDef HospitalityPackage_Substance;

        [MayRequire("Ludeon.RimWorld.Royalty")]
        public static HospitalityPackageDef HospitalityPackage_Meditation;

        static HospitalityPackageDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(HospitalityPackageDefOf));
        }
    }
}

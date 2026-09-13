using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events.Workers
{
    public class AcademicContactServiceEventWorker : ServiceEventWorker
    {
        private const string UniversitySpecialtyDefName = "SettlementSpecialty_University";
        private const string EducationCategoryDefName = "SettlementService_Education";

        public override bool CanApply(ServiceJobContext ctx) =>
            ServiceReferralResolver.HasUndiscoveredSpecialtyReferral(ctx, UniversitySpecialtyDefName, EducationCategoryDefName);

        public override void Apply(ServiceJobContext ctx) =>
            ServiceReferralResolver.TryRevealSpecialtyReferral(ctx, UniversitySpecialtyDefName, EducationCategoryDefName);
    }
}

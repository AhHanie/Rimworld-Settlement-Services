using System.Collections.Generic;
using System.Linq;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Medical
{
    public class ExtendedTreatmentServiceWorker : SettlementServiceWorker
    {
        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) return ServiceAvailabilityReport.Available;
            return MedicalTreatabilityService.FindTreatable(pawn, ctx.SelectedTierKey).Any()
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NothingToTreat");
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) =>
            Enumerable.Empty<ServiceLineItem>();

        public override List<ServiceStockRequirement> GetDynamicStockRequirements(SettlementServiceRequest request)
        {
            var result = new List<ServiceStockRequirement>();
            int amount = RequiredMedicine(request);
            if (amount > 0)
                result.Add(new ServiceStockRequirement { stockCategoryDefName = "SettlementStock_Medicine", preferredThingDefName = "MedicineIndustrial", amount = amount, playerCanSupply = true });
            return result;
        }

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote) =>
            MedicalInputPlanning.PlanFromCategory(request, "SettlementStock_Medicine", RequiredMedicine(request));

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (ctx.CurrentTarget?.liveThing is Pawn pawn)
            {
                float quality = MedicalQualityService.TreatmentQuality(ctx.ResolveSettlement(), def.category);
                string acceptedTierKey = ctx.Job.acceptedQuote?.selectedTierKey;
                List<MedicalTreatabilityService.TreatableCondition> treatable = MedicalTreatabilityService.FindTreatable(pawn, acceptedTierKey).ToList();

                foreach (MedicalTreatabilityService.TreatableCondition condition in treatable)
                {
                    if (pawn.health.hediffSet.hediffs.Contains(condition.hediff)) Treat(condition, quality);
                }
            }
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static int RequiredMedicine(SettlementServiceRequest request)
        {
            if (!(request.target.thing is Pawn pawn)) return 0;
            return MedicalTreatabilityService.FindTreatable(pawn, request.selectedTierKey).Count();
        }

        private static void Treat(MedicalTreatabilityService.TreatableCondition condition, float quality)
        {
            if (condition.action == MedicalTreatabilityService.TreatmentAction.TendOnly)
                condition.hediff.Tended(quality, 1f);
            else
                HealthUtility.Cure(condition.hediff);
        }
    }
}

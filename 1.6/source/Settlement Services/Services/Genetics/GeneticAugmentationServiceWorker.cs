using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;
using Settlement_Services.Services.Medical;

namespace Settlement_Services.Services.Genetics
{
    public class GeneticAugmentationServiceWorker : SettlementServiceWorker
    {
        private const int MedicineAmount = 4;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) return ServiceAvailabilityReport.Available;
            if (!GeneEligibilityService.IsValidRecipient(pawn))
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.InvalidGeneTarget");
            return GeneCatalogService.CuratedXenotypes().Any()
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoXenotypesOffered");
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            foreach (XenotypeDef xenotypeDef in GeneCatalogService.CuratedXenotypes())
                yield return new ServiceDisplayOption
                {
                    key = xenotypeDef.defName,
                    label = xenotypeDef.LabelCap,
                    description = xenotypeDef.descriptionShort,
                    groupKey = GeneCatalogService.XenotypeGroupKey,
                };
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote) =>
            MedicalInputPlanning.PlanFromCategory(request, "SettlementStock_Medicine", MedicineAmount);

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            if (!(request.target.thing is Pawn pawn)) return null;
            XenotypeDef chosen = DefDatabase<XenotypeDef>.GetNamedSilentFail(request.selectedOptionKeys.FirstOrDefault());
            if (chosen == null) return null;

            Xenogerm xenogerm = GeneCatalogService.BuildSyntheticXenogerm(chosen);
            return GeneEligibilityService.CanImplant(pawn, xenogerm, out _) ? null : "SettlementServices.Error.NoCarriedXenogerm";
        }

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTarget?.liveThing is Pawn pawn)) return ServiceCompletionResult.Ok();

            XenotypeDef chosen = DefDatabase<XenotypeDef>.GetNamedSilentFail(ctx.Job.selectedOptionKeys.FirstOrDefault());
            if (chosen == null) return ServiceCompletionResult.Ok();

            Xenogerm xenogerm = GeneCatalogService.BuildSyntheticXenogerm(chosen);
            if (!GeneEligibilityService.CanImplant(pawn, xenogerm, out _))
                return ServiceCompletionResult.Fail("SettlementServices.Error.NoCarriedXenogerm");

            GeneUtility.ImplantXenogermItem(pawn, xenogerm);
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();
    }
}

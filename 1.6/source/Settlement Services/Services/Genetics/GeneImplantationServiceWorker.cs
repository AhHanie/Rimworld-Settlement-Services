using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;
using Settlement_Services.Services.Medical;

namespace Settlement_Services.Services.Genetics
{
    public class GeneImplantationServiceWorker : SettlementServiceWorker
    {
        private const int MedicineAmount = 3;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) return ServiceAvailabilityReport.Available;
            if (!GeneEligibilityService.IsValidRecipient(pawn))
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.InvalidGeneTarget");

            bool anyLegal = GeneOptionService.FindCarried(ctx.RequestingCaravan)
                .Any(x => GeneEligibilityService.CanImplant(pawn, x, out _));
            return anyLegal
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoCarriedXenogerm");
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) yield break;
            foreach (Xenogerm xenogerm in GeneOptionService.FindCarried(ctx.RequestingCaravan))
            {
                if (!GeneEligibilityService.CanImplant(pawn, xenogerm, out _)) continue;
                yield return new ServiceDisplayOption { key = GeneOptionService.KeyFor(xenogerm), label = xenogerm.LabelCap, groupKey = GeneOptionService.XenogermGroupKey };
            }
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote) =>
            MedicalInputPlanning.PlanFromCategory(request, "SettlementStock_Medicine", MedicineAmount);

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            if (!(request.target.thing is Pawn pawn)) return null;
            string xenogermKey = request.selectedOptionKeys.FirstOrDefault();
            if (xenogermKey == null) return null;

            Caravan caravan = request.negotiator?.GetCaravan();
            Xenogerm xenogerm = caravan == null ? null : GeneOptionService.ResolveFromCaravan(caravan, xenogermKey);
            return GeneEligibilityService.CanImplant(pawn, xenogerm, out _) ? null : "SettlementServices.Error.NoCarriedXenogerm";
        }

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTarget?.liveThing is Pawn pawn)) return ServiceCompletionResult.Ok();

            string xenogermKey = ctx.Job.selectedOptionKeys.FirstOrDefault();
            Caravan caravan = xenogermKey == null ? null
                : Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == ctx.Job.requesterCaravanId);
            Xenogerm xenogerm = caravan == null ? null : GeneOptionService.ResolveFromCaravan(caravan, xenogermKey);

            if (!GeneEligibilityService.CanImplant(pawn, xenogerm, out _))
                return ServiceCompletionResult.Fail("SettlementServices.Error.NoCarriedXenogerm");

            GeneUtility.ImplantXenogermItem(pawn, xenogerm);
            xenogerm.Destroy();
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();
    }
}

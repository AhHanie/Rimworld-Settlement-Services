using System.Collections.Generic;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Mechanitor
{
    public abstract class SubcoreScanServiceWorker : SettlementServiceWorker
    {
        protected abstract string SubcoreDefName { get; }

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) return ServiceAvailabilityReport.Available;
            return SubcoreScanEligibility.IsValidDonor(pawn, out string errorKey)
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable(errorKey);
        }

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            if (!(request.target?.thing is Pawn pawn)) return "SettlementServices.Error.InvalidTarget";
            if (!SubcoreScanEligibility.SubcoreAvailable(SubcoreDefName)) return "SettlementServices.Error.SubcoreScanUnavailable";
            return SubcoreScanEligibility.IsValidDonor(pawn, out string errorKey) ? null : errorKey;
        }

        public override string GetTargetBlockReason(Thing target)
        {
            if (!(target is Pawn pawn)) return null;
            return SubcoreScanEligibility.IsValidDonor(pawn, out string errorKey) ? null : errorKey.Translate().ToString();
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote) =>
            SubcoreScanEligibility.PlanInputs(def, request);

        public override ServiceStartResult Start(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTargetThing is Pawn pawn)) return ServiceStartResult.Fail("SettlementServices.Error.TargetNoLongerExists");
            if (!SubcoreScanEligibility.SubcoreAvailable(SubcoreDefName)) return ServiceStartResult.Fail("SettlementServices.Error.SubcoreScanUnavailable");
            return SubcoreScanEligibility.IsValidDonor(pawn, out string errorKey) ? ServiceStartResult.Ok : ServiceStartResult.Fail(errorKey);
        }

        protected bool TryMakeSubcore(ServiceJobContext ctx, out Pawn pawn, out Thing subcore, out string errorKey)
        {
            subcore = null;
            pawn = ctx.CurrentTargetThing as Pawn;
            if (pawn == null || pawn.Destroyed || pawn.Dead) { errorKey = "SettlementServices.Error.TargetNoLongerExists"; return false; }

            subcore = SubcoreScanEligibility.MakeSubcore(SubcoreDefName);
            if (subcore == null) { errorKey = "SettlementServices.Error.SubcoreScanUnavailable"; return false; }

            errorKey = null;
            return true;
        }
    }
}

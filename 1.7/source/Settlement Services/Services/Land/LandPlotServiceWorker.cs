using System;
using System.Collections.Generic;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Land
{
    public sealed class LandPlotServiceWorker : SettlementServiceWorker
    {
        private const string DedicatedDialog = LandPlotService.Key + "DedicatedDialog";
        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx) =>
            !LandPlotPatches.Ready ? ServiceAvailabilityReport.Unavailable(LandPlotService.PatchFailure)
            : !LandPlotService.EligibleHost(ctx.Settlement) ? ServiceAvailabilityReport.Unavailable(LandPlotService.Key + "HostIneligible")
            : base.CanOffer(ctx);
        public override string ValidateUnitRequest(SettlementServiceRequest request) => DedicatedDialog;
        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => Array.Empty<ServiceLineItem>();
        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Fail(DedicatedDialog);
        public override ServiceCompletionResult Complete(ServiceJobContext ctx) => ServiceCompletionResult.Fail(DedicatedDialog);
        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();
        public override bool RequiresTargetCustody => false;
    }
}

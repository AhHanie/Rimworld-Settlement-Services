using System;
using System.Collections.Generic;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Transport
{
    public sealed class TransportServiceWorker : SettlementServiceWorker
    {
        private const string DedicatedDialog = TransportBooking.Key + "Error.DedicatedDialog";

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            string error = TransportBooking.ProviderBlock(ctx.Settlement, ctx.RequestingCaravan, def);
            return error == null ? ServiceAvailabilityReport.Available : ServiceAvailabilityReport.Unavailable(error);
        }

        public override string ValidateUnitRequest(SettlementServiceRequest request) => DedicatedDialog;
        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => Array.Empty<ServiceLineItem>();
        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Fail(DedicatedDialog);
        public override ServiceCompletionResult Complete(ServiceJobContext ctx) => ServiceCompletionResult.Fail(DedicatedDialog);
        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();
        public override bool RequiresTargetCustody => false;
    }
}

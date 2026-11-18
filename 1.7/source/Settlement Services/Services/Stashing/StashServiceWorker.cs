using System.Collections.Generic;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Stashing
{
    public sealed class StashServiceWorker : SettlementServiceWorker
    {
        private const string DedicatedDialogKey = "SettlementServices.Stash.Error.DedicatedDialog";
        public override string ValidateUnitRequest(SettlementServiceRequest request) => DedicatedDialogKey;
        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) =>
            new List<ServiceLineItem>();
        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Fail(DedicatedDialogKey);
        public override ServiceCompletionResult Complete(ServiceJobContext ctx) => ServiceCompletionResult.Fail(DedicatedDialogKey);
        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();
        public override bool RequiresTargetCustody => false;
    }
}

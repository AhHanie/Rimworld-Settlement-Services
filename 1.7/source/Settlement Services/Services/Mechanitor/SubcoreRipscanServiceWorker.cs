using System.Collections.Generic;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Mechanitor
{
    public class SubcoreRipscanServiceWorker : SubcoreScanServiceWorker
    {
        protected override string SubcoreDefName => "SubcoreHigh";

        public override IEnumerable<string> GetDisplaySummaryLines(SettlementServiceContext ctx)
        {
            if (ctx.SelectedTarget is Pawn pawn)
                yield return "SettlementServices.Warning.SubcoreRipscanDeath".Translate(pawn.LabelShortCap);
        }

        public override ServiceConfirmationWarning GetConfirmationWarning(SettlementServiceRequest request)
        {
            if (!(request.target?.thing is Pawn pawn)) return null;
            return new ServiceConfirmationWarning("SettlementServices.Message.SubcoreRipscanConfirm".Translate(pawn.LabelShortCap), destructive: true);
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!TryMakeSubcore(ctx, out _, out Thing subcore, out string errorKey)) return ServiceCompletionResult.Fail(errorKey);

            return ServiceCompletionResult.Ok(requiresCollection: true, resultThings: new List<Thing> { subcore }, targetDisposition: ServiceTargetDisposition.RipscanExecution);
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated)
        {
            if (playerInitiated && ctx.Job.status == ServiceJobStatus.Active)
                return ServiceCancelResult.Ok(new List<ServiceLineItem>(), ServiceTargetDisposition.RipscanExecution);

            return ServiceCancelResult.Ok();
        }
    }
}

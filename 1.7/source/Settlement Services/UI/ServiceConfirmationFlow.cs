using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Payment;
using Settlement_Services.Framework.Workers.Results;
using Settlement_Services.Services.Market;

namespace Settlement_Services.UI
{
    internal static class ServiceConfirmationFlow
    {
        public static void TryConfirm(ServiceRequestSession session, SettlementServiceQuote quote, Window dialogToClose) =>
            ConfirmThen(session, () => TryConfirm(session, quote, dialogToClose, ServicePaymentSelection.Silver(), out _));

        public static void ConfirmThen(ServiceRequestSession session, System.Action proceed)
        {
            if (!ServiceCaravanTargetSelector.RevalidateStillEligible(session))
            {
                Reject("SettlementServices.Message.TargetNoLongerEligible", out _);
                return;
            }

            SettlementServiceRequest request = session.BuildRequest();
            request.bookingTick = Find.TickManager.TicksGame;

            SettlementServiceQuote freshQuote = SettlementServiceOrchestrator.RequestQuote(request, session.def);
            if (!freshQuote.IsValid)
            {
                Reject(freshQuote.validationErrors.First(), out _);
                return;
            }

            ServiceConfirmationWarning warning = session.def.Worker.GetConfirmationWarning(request);
            if (warning == null || warning.Text.NullOrEmpty())
            {
                proceed();
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(warning.Text, proceed, warning.Destructive));
        }

        public static bool TryConfirm(ServiceRequestSession session, SettlementServiceQuote quote, Window dialogToClose, ServicePaymentSelection payment, out string failureMessage)
        {
            failureMessage = null;

            if (!ServiceCaravanTargetSelector.RevalidateStillEligible(session))
                return Reject("SettlementServices.Message.TargetNoLongerEligible", out failureMessage);

            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            SettlementServiceRequest request = session.BuildRequest();
            request.bookingTick = Find.TickManager.TicksGame;

            SettlementServiceQuote freshQuote = SettlementServiceOrchestrator.RequestQuote(request, session.def);
            if (!freshQuote.IsValid)
                return Reject(freshQuote.validationErrors.First(), out failureMessage);

            if (payment.IsBarter && freshQuote.totalCost != quote.totalCost)
                return Reject("SettlementServices.Error.BarterPriceChanged", out failureMessage);

            ServiceJobRecord job = SettlementServiceOrchestrator.CreateDraftJob(request, session.def);
            if (!domain.TryTransition(job.jobId, ServiceJobStatus.Quoted))
            {
                domain.TryTransition(job.jobId, ServiceJobStatus.Cancelled);
                return Reject("SettlementServices.Message.RequestFailed", out failureMessage);
            }

            if (!SettlementServiceOrchestrator.AcceptQuote(job.jobId, freshQuote, request, payment))
            {
                string reasonKey = domain.GetJob(job.jobId)?.lastErrorKey;
                SettlementServiceOrchestrator.CancelJob(job.jobId, playerInitiated: false);
                return Reject(reasonKey ?? "SettlementServices.Message.RequestFailed", out failureMessage);
            }

            if (!SettlementServiceOrchestrator.StartJob(job.jobId))
            {
                string reasonKey = domain.GetJob(job.jobId)?.lastErrorKey;
                Reject(reasonKey ?? "SettlementServices.Message.RequestFailed", out failureMessage);
                return true;
            }

            if (session.def.Worker is LocalMarketServiceWorker && session.caravan != null)
                SettlementServiceOrchestrator.CollectJob(job.jobId, session.caravan);

            Messages.Message("SettlementServices.Message.ServiceRequested".Translate(), MessageTypeDefOf.PositiveEvent, historical: false);
            dialogToClose.Close();
            return true;
        }

        private static bool Reject(string key, out string failureMessage)
        {
            failureMessage = key.Translate();
            Messages.Message(failureMessage, MessageTypeDefOf.RejectInput, historical: false);
            return false;
        }
    }
}

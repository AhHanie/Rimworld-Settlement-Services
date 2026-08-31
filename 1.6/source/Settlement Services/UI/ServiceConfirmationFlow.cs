using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.UI
{
    internal static class ServiceConfirmationFlow
    {
        public static void TryConfirm(ServiceRequestSession session, SettlementServiceQuote quote, Window dialogToClose)
        {
            if (!ServiceCaravanTargetSelector.RevalidateStillEligible(session))
            {
                Messages.Message("SettlementServices.Message.TargetNoLongerEligible".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            SettlementServiceRequest request = session.BuildRequest();
            request.bookingTick = Find.TickManager.TicksGame;

            SettlementServiceQuote freshQuote = SettlementServiceOrchestrator.RequestQuote(request, session.def);
            if (!freshQuote.IsValid)
            {
                Messages.Message(freshQuote.validationErrors.First().Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            ServiceJobRecord job = SettlementServiceOrchestrator.CreateDraftJob(request, session.def);
            if (!domain.TryTransition(job.jobId, ServiceJobStatus.Quoted))
            {
                domain.TryTransition(job.jobId, ServiceJobStatus.Cancelled);
                Messages.Message("SettlementServices.Message.RequestFailed".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            if (!SettlementServiceOrchestrator.AcceptQuote(job.jobId, freshQuote, request))
            {
                string reasonKey = domain.GetJob(job.jobId)?.lastErrorKey;
                SettlementServiceOrchestrator.CancelJob(job.jobId, playerInitiated: false);
                Messages.Message((reasonKey ?? "SettlementServices.Message.RequestFailed").Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            if (!SettlementServiceOrchestrator.StartJob(job.jobId))
            {
                string reasonKey = domain.GetJob(job.jobId)?.lastErrorKey;
                Messages.Message((reasonKey ?? "SettlementServices.Message.RequestFailed").Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Messages.Message("SettlementServices.Message.ServiceRequested".Translate(), MessageTypeDefOf.PositiveEvent, historical: false);
            dialogToClose.Close();
        }
    }
}

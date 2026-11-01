using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Genetics
{
    internal sealed class SurrogacyServiceWorker : SettlementServiceWorker
    {
        private const string RefundLineKey = "SettlementServices.LineItem.SurrogacyCancelRefund";
        private const string StateMissingKey = "SettlementServices.Error.SurrogacyStateMissing";
        private const string BirthFailedKey = "SettlementServices.Error.SurrogacyBirthFailed";

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!ModsConfig.BiotechActive) return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.RequiresBiotech");
            if (!Find.Storyteller.difficulty.ChildrenAllowed) return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.GrowthVatChildrenDisabled");

            if (ctx.SelectedTarget != null)
            {
                string blockKey = TargetBlockKey(ctx.SelectedTarget, ctx.RequestingCaravan, atStart: false);
                if (blockKey != null) return ServiceAvailabilityReport.Unavailable(blockKey);
            }

            return ServiceAvailabilityReport.Available;
        }

        public override bool ShouldListTarget(Thing target) => target is HumanEmbryo;

        public override string GetTargetBlockReason(Thing target)
        {
            string blockKey = TargetBlockKey(target, null, atStart: false);
            return blockKey == null ? null : blockKey.Translate().ToString();
        }

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            if (!Find.Storyteller.difficulty.ChildrenAllowed) return "SettlementServices.Error.GrowthVatChildrenDisabled";

            Thing target = request.target.thing;
            if (target == null) return "SettlementServices.Error.InvalidTarget";

            return TargetBlockKey(target, request.negotiator?.GetCaravan(), atStart: false);
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => Enumerable.Empty<ServiceLineItem>();

        public override int? ExpectedDurationTicksFor(SettlementServiceRequest request) => GestationTicks();

        public override IEnumerable<string> GetDisplaySummaryLines(SettlementServiceContext ctx)
        {
            if (ctx.SelectedTarget == null) yield break;
            if (TargetBlockKey(ctx.SelectedTarget, ctx.RequestingCaravan, atStart: false) != null) yield break;

            yield return "SettlementServices.Label.SurrogacyEndpoint".Translate().ToString();
            yield return "SettlementServices.Label.SurrogacyDuration".Translate(GestationTicks().ToStringTicksToPeriod()).ToString();
            if (!EmbryoBirthGeneration.BabiesAreHealthy) yield return "SettlementServices.Label.SurrogacyRisk".Translate().ToString();
            yield return "SettlementServices.Label.SurrogacyCollectNote".Translate().ToString();
        }

        public override ServiceConfirmationWarning GetConfirmationWarning(SettlementServiceRequest request)
        {
            string text = "SettlementServices.Warning.SurrogacyHold".Translate(GestationTicks().ToStringTicksToPeriod()).ToString();
            if (!EmbryoBirthGeneration.BabiesAreHealthy) text += " " + "SettlementServices.Warning.SurrogacyRisk".Translate().ToString();
            return new ServiceConfirmationWarning(text, destructive: false);
        }

        public override ServiceStartResult Start(ServiceJobContext ctx)
        {
            ServiceJobRecord job = ctx.Job;
            if (job.surrogacy != null) return ServiceStartResult.Ok;
            if (!Find.Storyteller.difficulty.ChildrenAllowed) return ServiceStartResult.Fail("SettlementServices.Error.GrowthVatChildrenDisabled");

            string blockKey = TargetBlockKey(ctx.CurrentTargetThing, null, atStart: true);
            if (blockKey != null) return ServiceStartResult.Fail(blockKey);

            int now = Find.TickManager.TicksGame;
            var state = new SurrogacyJobState
            {
                startTick = now,
                dueTick = now + ExpectedDurationTicks(ctx),
            };

            return ctx.Domain.AttachSurrogacyState(job.jobId, state)
                ? ServiceStartResult.Ok
                : ServiceStartResult.Fail("SettlementServices.Error.TargetCouldNotBeDetached");
        }

        public override ServiceTickResult Tick(ServiceJobContext ctx, int ticksSinceLastCall)
        {
            SurrogacyJobState state = ctx.Job.surrogacy;
            if (state == null) return ServiceTickResult.Failed(StateMissingKey);
            if (state.birthCommitted) return ServiceTickResult.NoChange;

            Thing embryo = ctx.CurrentTargetThing;
            if (!(embryo is HumanEmbryo) || embryo.Destroyed) return ServiceTickResult.Failed("SettlementServices.Error.TargetNoLongerExists");

            return ServiceTickResult.NoChange;
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            ServiceJobRecord job = ctx.Job;
            SurrogacyJobState state = job.surrogacy;
            if (state == null) return ServiceCompletionResult.Fail(StateMissingKey);
            if (state.completed) return ServiceCompletionResult.Ok(requiresCollection: state.stillborn);

            if (!state.birthCommitted)
            {
                if (!(ctx.CurrentTargetThing is HumanEmbryo embryo) || embryo.Destroyed)
                    return ServiceCompletionResult.Fail("SettlementServices.Error.TargetNoLongerExists");

                try
                {
                    if (!SurrogacyBirthService.TryBirth(ctx.Domain, job, embryo)) return ServiceCompletionResult.Fail(BirthFailedKey);
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex, $"Job {job.jobId}: surrogacy birth failed; the embryo stays in custody.");
                    return state.birthCommitted ? FinishCommittedBirth(ctx) : ServiceCompletionResult.Fail(BirthFailedKey);
                }
            }

            return FinishCommittedBirth(ctx);
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated)
        {
            ServiceJobRecord job = ctx.Job;
            SurrogacyJobState state = job.surrogacy;
            int total = TotalFee(job);

            if (state == null) return ServiceCancelResult.Ok(RefundLines(total));
            if (state.completed || state.birthCommitted) return ServiceCancelResult.Ok(RefundLines(0));

            int amount = playerInitiated ? UnusedFee(job, state) : total;
            return ServiceCancelResult.Ok(RefundLines(ctx.Domain.TakeSurrogacyRefund(job.jobId, amount)));
        }

        public override int? TerminationRefundFor(ServiceJobContext ctx)
        {
            SurrogacyJobState state = ctx.Job.surrogacy;
            if (state == null) return null;
            if (state.completed || state.birthCommitted) return 0;

            return ctx.Domain.TakeSurrogacyRefund(ctx.Job.jobId, TotalFee(ctx.Job));
        }

        private ServiceCompletionResult FinishCommittedBirth(ServiceJobContext ctx)
        {
            ServiceJobRecord job = ctx.Job;
            try
            {
                SurrogacyBirthService.AnnounceOnce(ctx.Domain, job);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, $"Job {job.jobId}: surrogacy birth announcement failed; the birth itself was committed.");
            }

            ctx.Domain.MarkSurrogacyCompleted(job.jobId);
            return ServiceCompletionResult.Ok(requiresCollection: job.surrogacy.stillborn);
        }

        private static string TargetBlockKey(Thing target, Caravan caravan, bool atStart)
        {
            switch (EmbryoEligibility.Check(target, caravan, atStart))
            {
                case EmbryoBlock.NotEmbryo: return "SettlementServices.Error.SurrogacyNotEmbryo";
                case EmbryoBlock.Gone: return "SettlementServices.Error.TargetNoLongerExists";
                case EmbryoBlock.NoGenes: return "SettlementServices.Error.SurrogacyEmbryoNoGenes";
                case EmbryoBlock.ImplantPending: return "SettlementServices.Error.SurrogacyEmbryoImplantPending";
                case EmbryoBlock.PartialStack: return "SettlementServices.Error.SurrogacyEmbryoPartialStack";
                case EmbryoBlock.NotInCaravan: return "SettlementServices.Error.TargetNotInCaravan";
                default: return null;
            }
        }

        private static int GestationTicks()
        {
            float days = ThingDefOf.Human?.race?.gestationPeriodDays ?? 18f;
            return Mathf.Clamp(Mathf.RoundToInt(days * GenDate.TicksPerDay), GenDate.TicksPerDay, 60 * GenDate.TicksPerDay);
        }

        private static int TotalFee(ServiceJobRecord job) => Mathf.Max(0, job.acceptedQuote?.totalCost ?? 0);

        private static int UnusedFee(ServiceJobRecord job, SurrogacyJobState state)
        {
            int span = state.dueTick - state.startTick;
            if (span <= 0) return 0;

            float elapsed = Mathf.Clamp01((Find.TickManager.TicksGame - state.startTick) / (float)span);
            return Mathf.Clamp(Mathf.RoundToInt(TotalFee(job) * (1f - elapsed)), 0, TotalFee(job));
        }

        private static List<ServiceLineItem> RefundLines(int amount) =>
            new List<ServiceLineItem> { new ServiceLineItem(RefundLineKey, amount) };
    }
}

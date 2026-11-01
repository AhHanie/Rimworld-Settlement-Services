using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Genetics
{
    internal abstract class GrowthVatServiceWorkerBase : SettlementServiceWorker
    {
        private const string MilestoneGroupKey = "SettlementServices.Label.GrowthVatMilestoneChoice";
        private const float NutritionTolerance = 0.001f;

        protected abstract GrowthVatKind Kind { get; }

        protected abstract string TargetBlockKey(Thing target, Caravan caravan, bool atStart);

        protected abstract GrowthVatProjection Project(Thing target, GrowthVatMilestone milestone);

        protected abstract IEnumerable<GrowthVatMilestone> OfferedMilestones(Thing target);

        protected abstract bool TryBeginOccupancy(ServiceJobContext ctx, Thing target);

        public override bool CompletesAtExpectedTick => false;

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

        public override string GetTargetBlockReason(Thing target)
        {
            string blockKey = TargetBlockKey(target, null, atStart: false);
            return blockKey == null ? null : blockKey.Translate().ToString();
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            foreach (GrowthVatMilestone milestone in OfferedMilestones(ctx.SelectedTarget))
            {
                yield return new ServiceDisplayOption
                {
                    key = milestone.Key,
                    label = milestone.Label,
                    description = milestone.Description,
                    groupKey = MilestoneGroupKey,
                    groupColumnCount = 2,
                };
            }
        }

        public override IEnumerable<string> GetDisplaySummaryLines(SettlementServiceContext ctx)
        {
            GrowthVatMilestone milestone = GrowthVatMilestone.FromSelection(ctx.SelectedOptionKeys, out _);
            if (milestone == null || ctx.SelectedTarget == null) yield break;
            if (TargetBlockKey(ctx.SelectedTarget, ctx.RequestingCaravan, atStart: false) != null) yield break;
            if (!OfferedMilestones(ctx.SelectedTarget).Contains(milestone)) yield break;

            GrowthVatProjection projection = Project(ctx.SelectedTarget, milestone);

            yield return "SettlementServices.Label.GrowthVatEndpoint".Translate(milestone.Label).ToString();
            yield return projection.GestationTicks > 0 && !milestone.IsBirth
                ? "SettlementServices.Label.GrowthVatProjectedTimeEmbryo".Translate(GrowthVatMath.ClampTicks(projection.TotalTicks).ToStringTicksToPeriod(), projection.GestationTicks.ToStringTicksToPeriod()).ToString()
                : "SettlementServices.Label.GrowthVatProjectedTime".Translate(GrowthVatMath.ClampTicks(projection.TotalTicks).ToStringTicksToPeriod()).ToString();
            yield return "SettlementServices.Label.GrowthVatRequiredNutrition".Translate(projection.Nutrition.ToString("F1")).ToString();

            ServiceInputPlan plan = GrowthVatInputPlanning.PlanNutrition(ctx.Settlement, projection.Nutrition, null);
            yield return plan.stockConsumed.Count == 0
                ? "SettlementServices.Label.GrowthVatNutritionUnavailable".Translate().ToString()
                : "SettlementServices.Label.GrowthVatNutritionCharge".Translate(GrowthVatInputPlanning.PreviewCost(plan), GrowthVatInputPlanning.AllocatedNutrition(plan).ToString("F1")).ToString();

            if (Kind == GrowthVatKind.Embryo) yield return "SettlementServices.Label.GrowthVatSpeedEstimateNote".Translate().ToString();
            yield return "SettlementServices.Label.GrowthVatGrowthMomentNote".Translate().ToString();
            yield return "SettlementServices.Label.GrowthVatCollectNote".Translate().ToString();
        }

        public override ServiceConfirmationWarning GetConfirmationWarning(SettlementServiceRequest request)
        {
            GrowthVatMilestone milestone = GrowthVatMilestone.FromSelection(request.selectedOptionKeys, out _);
            if (milestone == null) return null;

            return new ServiceConfirmationWarning("SettlementServices.Warning.GrowthVatHold".Translate(milestone.Label).ToString(), destructive: false);
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => Enumerable.Empty<ServiceLineItem>();

        public override IEnumerable<ServiceLineItem> BuildPassThroughLineItems(SettlementServiceRequest request, ServiceBatchAllocationContext batchContext)
        {
            ServiceInputPlan plan = NutritionPlanFor(request, batchContext);
            int cost = GrowthVatInputPlanning.PreviewCost(plan);
            if (cost > 0) yield return new ServiceLineItem(GrowthVatInputPlanning.NutritionLineItemKey, cost);
        }

        public override List<ServiceStockRequirement> GetDynamicStockRequirements(SettlementServiceRequest request)
        {
            if (!TryProject(request, out GrowthVatProjection projection)) return new List<ServiceStockRequirement>();
            return new List<ServiceStockRequirement> { GrowthVatInputPlanning.NutritionRequirement(projection.Nutrition) };
        }

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote) =>
            PlanInputs(request, quote, new ServiceBatchAllocationContext());

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote, ServiceBatchAllocationContext batchContext)
        {
            var plan = new ServiceInputPlan();
            plan.stockConsumed.AddRange(NutritionPlanFor(request, batchContext).stockConsumed);
            return plan;
        }

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            if (!Find.Storyteller.difficulty.ChildrenAllowed) return "SettlementServices.Error.GrowthVatChildrenDisabled";

            Thing target = request.target.thing;
            if (target == null) return "SettlementServices.Error.InvalidTarget";

            string blockKey = TargetBlockKey(target, request.negotiator?.GetCaravan(), atStart: false);
            if (blockKey != null) return blockKey;

            GrowthVatMilestone milestone = GrowthVatMilestone.FromSelection(request.selectedOptionKeys, out string milestoneError);
            if (milestone == null) return milestoneError;

            return OfferedMilestones(target).Contains(milestone) ? null : "SettlementServices.Error.GrowthVatMilestoneNotAhead";
        }

        public override int? ExpectedDurationTicksFor(SettlementServiceRequest request) =>
            TryProject(request, out GrowthVatProjection projection) ? GrowthVatMath.ClampTicks(projection.TotalTicks) : (int?)null;

        public override ServiceStartResult Start(ServiceJobContext ctx)
        {
            ServiceJobRecord job = ctx.Job;
            if (job.growthVat != null) return ServiceStartResult.Ok;
            if (!Find.Storyteller.difficulty.ChildrenAllowed) return ServiceStartResult.Fail("SettlementServices.Error.GrowthVatChildrenDisabled");

            Thing target = ctx.CurrentTargetThing;
            string blockKey = TargetBlockKey(target, null, atStart: true);
            if (blockKey != null) return ServiceStartResult.Fail(blockKey);

            GrowthVatMilestone milestone = GrowthVatMilestone.FromSelection(ctx.SelectedOptionKeys, out string milestoneError);
            if (milestone == null) return ServiceStartResult.Fail(milestoneError);
            if (!OfferedMilestones(target).Contains(milestone)) return ServiceStartResult.Fail("SettlementServices.Error.GrowthVatMilestoneNotAhead");

            GrowthVatProjection projection = Project(target, milestone);
            ReadPrepaid(ctx, out float prepaidNutrition, out int prepaidValue);
            if (prepaidNutrition <= 0f) return ServiceStartResult.Fail("SettlementServices.Error.InsufficientStock");
            if (projection.Nutrition > prepaidNutrition + NutritionTolerance) return ServiceStartResult.Fail("SettlementServices.Error.GrowthVatSpeedChanged");

            int totalCost = job.acceptedQuote?.totalCost ?? 0;
            var state = new GrowthVatJobState
            {
                kind = Kind,
                milestoneKey = milestone.Key,
                targetAgeTicks = milestone.TargetAgeTicks,
                gestationTicksLeft = projection.GestationTicks,
                lastProcessedTick = Find.TickManager.TicksGame,
                plannedNutrition = projection.Nutrition,
                prepaidNutrition = prepaidNutrition,
                prepaidValue = prepaidValue,
                facilityFee = Mathf.Max(0, totalCost - prepaidValue),
            };

            if (!ctx.Domain.AttachGrowthVatState(job.jobId, state)) return ServiceStartResult.Fail("SettlementServices.Error.TargetCouldNotBeDetached");

            if (!TryBeginOccupancy(ctx, target))
            {
                ctx.Domain.ClearGrowthVatState(job.jobId);
                return ServiceStartResult.Fail("SettlementServices.Error.TargetCouldNotBeDetached");
            }

            return ServiceStartResult.Ok;
        }

        public override ServiceTickResult Tick(ServiceJobContext ctx, int ticksSinceLastCall)
        {
            GrowthVatJobState state = ctx.Job.growthVat;
            if (state == null || state.completed) return ServiceTickResult.NoChange;
            if (state.targetReached) return ServiceTickResult.Completed;

            switch (GrowthVatSimulation.Advance(ctx.Domain, ctx.Job, state))
            {
                case GrowthVatAdvanceOutcome.TargetReached:
                case GrowthVatAdvanceOutcome.Stillborn:
                    return ServiceTickResult.Completed;
                case GrowthVatAdvanceOutcome.NutritionShortfall:
                    return ServiceTickResult.Failed("SettlementServices.Error.GrowthVatNutritionShortfall");
                case GrowthVatAdvanceOutcome.BirthFailed:
                    return ServiceTickResult.Failed("SettlementServices.Error.GrowthVatBirthFailed");
                case GrowthVatAdvanceOutcome.TargetLost:
                    return ServiceTickResult.Failed("SettlementServices.Error.TargetNoLongerExists");
                default:
                    return ServiceTickResult.NoChange;
            }
        }

        public override bool IsReadyToComplete(ServiceJobContext ctx)
        {
            GrowthVatJobState state = ctx.Job.growthVat;
            return state != null && (state.targetReached || state.completed);
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            GrowthVatJobState state = ctx.Job.growthVat;
            if (state == null || state.completed) return ServiceCompletionResult.Ok();
            if (!state.targetReached) return ServiceCompletionResult.Fail("SettlementServices.Error.GrowthVatNotReady");

            ctx.Domain.ReleaseGrowthVat(ctx.Job.jobId);
            int refund = ctx.Domain.TakeGrowthVatRefund(ctx.Job.jobId, UnusedNutritionValue(state));
            ctx.Domain.MarkGrowthVatCompleted(ctx.Job.jobId);
            return ServiceCompletionResult.Ok(requiresCollection: state.stillborn).WithRefund(refund);
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated)
        {
            GrowthVatJobState state = ctx.Job.growthVat;
            if (state == null) return ServiceCancelResult.Ok();

            OnJobTerminating(ctx);
            int refund = state.completed ? 0 : ctx.Domain.TakeGrowthVatRefund(ctx.Job.jobId, TerminationRefundAmount(state));
            return ServiceCancelResult.Ok(new List<ServiceLineItem> { new ServiceLineItem("SettlementServices.LineItem.GrowthVatCancelRefund", refund) });
        }

        public override int? TerminationRefundFor(ServiceJobContext ctx)
        {
            GrowthVatJobState state = ctx.Job.growthVat;
            if (state == null) return null;
            if (state.completed) return 0;

            return ctx.Domain.TakeGrowthVatRefund(ctx.Job.jobId, TerminationRefundAmount(state));
        }

        public override void OnJobTerminating(ServiceJobContext ctx)
        {
            if (ctx.Job.growthVat == null) return;
            ctx.Domain.ReleaseGrowthVat(ctx.Job.jobId);
        }

        private static int UnusedNutritionValue(GrowthVatJobState state)
        {
            if (state.prepaidNutrition <= 0f || state.prepaidValue <= 0) return 0;
            float unitValue = state.prepaidValue / state.prepaidNutrition;
            return Mathf.Clamp(Mathf.RoundToInt(unitValue * state.UnusedNutrition), 0, state.prepaidValue);
        }

        private static int TerminationRefundAmount(GrowthVatJobState state)
        {
            float progress = state.plannedNutrition > 0f ? Mathf.Clamp01(state.consumedNutrition / state.plannedNutrition) : 1f;
            int feeRefund = Mathf.RoundToInt(state.facilityFee * (1f - progress));
            return UnusedNutritionValue(state) + feeRefund;
        }

        private static void ReadPrepaid(ServiceJobContext ctx, out float nutrition, out int value)
        {
            nutrition = 0f;
            foreach (ReservationRecord reservation in ctx.Domain.ReservationsForJob(ctx.Job.jobId))
            {
                ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(reservation.stockThingDefName);
                if (thingDef != null) nutrition += GrowthVatInputPlanning.NutritionOf(thingDef) * reservation.amountReserved;
            }

            ServiceLineItem nutritionLine = ctx.Job.acceptedQuote?.lineItems?.FirstOrDefault(li => li.labelKey == GrowthVatInputPlanning.NutritionLineItemKey);
            value = nutritionLine?.amount ?? 0;
        }

        private bool TryProject(SettlementServiceRequest request, out GrowthVatProjection projection)
        {
            projection = default;
            Thing target = request.target.thing;
            if (target == null) return false;

            GrowthVatMilestone milestone = GrowthVatMilestone.FromSelection(request.selectedOptionKeys, out _);
            if (milestone == null || !OfferedMilestones(target).Contains(milestone)) return false;

            projection = Project(target, milestone);
            return true;
        }

        private ServiceInputPlan NutritionPlanFor(SettlementServiceRequest request, ServiceBatchAllocationContext batchContext) =>
            batchContext.GetOrCreateInputPlan(request, () =>
                TryProject(request, out GrowthVatProjection projection)
                    ? GrowthVatInputPlanning.PlanNutrition(request.settlement, projection.Nutrition, batchContext.StockLedger)
                    : new ServiceInputPlan());
    }
}

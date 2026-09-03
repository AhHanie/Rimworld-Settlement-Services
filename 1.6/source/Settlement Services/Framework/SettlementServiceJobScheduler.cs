using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Compatibility;
using Settlement_Services.Framework.Custody;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Events;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Framework
{
    public static class SettlementServiceJobScheduler
    {
        public static void TickAll(SettlementServicesWorldComponent domain, int ticksSinceLastCall)
        {
            foreach (ServiceJobRecord job in new List<ServiceJobRecord>(domain.ActiveJobs))
            {
                SettlementServiceDef def = DefDatabase<SettlementServiceDef>.GetNamedSilentFail(job.serviceDefName);
                if (def == null) continue;

                var ctx = new ServiceJobContext(domain, job);

                Settlement settlement = ctx.ResolveSettlement();
                if (settlement != null && settlement.Faction != null && settlement.Faction.HostileTo(Faction.OfPlayer))
                {
                    SettlementServiceOrchestrator.FailJob(domain, job.jobId, "SettlementServices.Error.FactionHostile");
                    continue;
                }

                IReadOnlyList<TargetSnapshot> targets = job.Targets;
                ServiceTickResult result = ServiceTickResult.NoChange;
                int tickTotalUnits = targets.Count > 0 ? targets.Count : 1;
                int tickSucceededUnits = 0;
                if (targets.Count > 0)
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        ServiceTickResult unitResult = def.Worker.Tick(ctx.ForTarget(targets[i], i), ticksSinceLastCall);
                        if (unitResult.FailedNow) { result = unitResult; break; }
                        if (unitResult.CompletedNow) result = unitResult;
                        tickSucceededUnits++;
                    }
                }
                else
                {
                    result = def.Worker.Tick(ctx, ticksSinceLastCall);
                    if (!result.FailedNow) tickSucceededUnits = 1;
                }

                PresentDueDuringServiceEvent(job, ctx);

                if (result.FailedNow)
                {
                    float tickRefundFraction = (tickTotalUnits - tickSucceededUnits) / (float)tickTotalUnits;
                    SettlementServiceOrchestrator.FailJob(domain, job.jobId, result.ErrorKey, tickRefundFraction);
                    continue;
                }

                bool naturallyDue = job.expectedCompletionTick >= 0 && Find.TickManager.TicksGame >= job.expectedCompletionTick;
                if (result.CompletedNow || naturallyDue) CompleteJob(domain, def, job, ctx);
            }
        }

        internal static bool TryCompleteActiveJobNow(int jobId)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            ServiceJobRecord job = domain?.GetJob(jobId);
            if (job == null || job.status != ServiceJobStatus.Active) return false;

            SettlementServiceDef def = DefDatabase<SettlementServiceDef>.GetNamedSilentFail(job.serviceDefName);
            if (def == null) return false;

            var ctx = new ServiceJobContext(domain, job);
            PresentDueDuringServiceEvent(job, ctx);
            CompleteJob(domain, def, job, ctx);
            return true;
        }

        private static void PresentDueDuringServiceEvent(ServiceJobRecord job, ServiceJobContext ctx)
        {
            if (job.eventOutcome != null && !job.eventOutcome.presented
                && job.eventOutcome.triggerPhase == ServiceEventTriggerPhase.DuringService
                && job.eventOutcome.scheduledTick >= 0 && Find.TickManager.TicksGame >= job.eventOutcome.scheduledTick)
            {
                ServiceEventDef eventDef = DefDatabase<ServiceEventDef>.GetNamedSilentFail(job.eventOutcome.eventDefName);
                if (eventDef != null) ServiceEventEffectApplier.Present(eventDef, job, ctx.ForUnitIndex(job.eventTargetIndex));
                else { job.eventOutcome.presented = true; job.eventOutcome.applied = true; }
            }
        }

        internal static void CompleteJob(SettlementServicesWorldComponent domain, SettlementServiceDef def, ServiceJobRecord job, ServiceJobContext ctx)
        {
            if (job.eventOutcome != null && !job.eventOutcome.presented && job.eventOutcome.triggerPhase == ServiceEventTriggerPhase.OnComplete)
            {
                ServiceEventDef eventDef = DefDatabase<ServiceEventDef>.GetNamedSilentFail(job.eventOutcome.eventDefName);
                if (eventDef != null) ServiceEventEffectApplier.Present(eventDef, job, ctx.ForUnitIndex(job.eventTargetIndex));
                else { job.eventOutcome.presented = true; job.eventOutcome.applied = true; }
            }

            IReadOnlyList<TargetSnapshot> targets = job.Targets;
            var allResultThings = new List<Thing>();
            bool requiresCollection = false;
            string failErrorKey = null;
            int totalUnits = targets.Count > 0 ? targets.Count : Mathf.Max(1, job.quantity);
            int succeededUnits = 0;

            if (targets.Count > 0)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    ServiceJobContext targetCtx = ctx.ForTarget(targets[i], i);
                    ServiceCompletionResult unitResult = def.Worker.Complete(targetCtx);
                    if (!unitResult.ResultThings.NullOrEmpty()) allResultThings.AddRange(unitResult.ResultThings);
                    if (!unitResult.Success) { failErrorKey = unitResult.ErrorKey; break; }
                    RestoreParticipantFoodAndRest(def, targetCtx);
                    requiresCollection |= unitResult.RequiresCollection;
                    succeededUnits++;
                }
            }
            else
            {
                for (int i = 0; i < totalUnits; i++)
                {
                    ServiceCompletionResult unitResult = def.Worker.Complete(ctx);
                    if (!unitResult.ResultThings.NullOrEmpty()) allResultThings.AddRange(unitResult.ResultThings);
                    if (!unitResult.Success) { failErrorKey = unitResult.ErrorKey; break; }
                    RestoreParticipantFoodAndRest(def, ctx);
                    requiresCollection |= unitResult.RequiresCollection;
                    succeededUnits++;
                }
            }

            BankResults(domain, job, allResultThings);

            if (failErrorKey != null)
            {
                float refundFraction = (totalUnits - succeededUnits) / (float)totalUnits;
                SettlementServiceOrchestrator.FailJob(domain, job.jobId, failErrorKey, refundFraction);
                return;
            }

            ServicePawnCaravanReturn.TryReturnCompletedPawn(ctx);

            SettlementServicesCompatibilityRegistry.NotifyCompleted(new CompatibilityCompletionContext(domain, job));

            bool needsCollection = requiresCollection || job.targetInCustody || !job.results.NullOrEmpty();
            domain.TryTransition(job.jobId, needsCollection ? ServiceJobStatus.AwaitingCollection : ServiceJobStatus.Completed);

            if (needsCollection)
            {
                SettlementServiceNotifier.NotifyReadyForCollection(job);
            }
            else
            {
                domain.TryTransition(job.jobId, ServiceJobStatus.Collected);
                SettlementServiceNotifier.NotifyCompleted(job);
            }
        }

        private static void RestoreParticipantFoodAndRest(SettlementServiceDef def, ServiceJobContext targetContext)
        {
            if (!def.restoresParticipantFoodAndRest) return;
            if (!(targetContext.CurrentTarget?.liveThing is Pawn pawn)) return;

            if (pawn.needs?.food != null) pawn.needs.food.CurLevelPercentage = 1f;
            if (pawn.needs?.rest != null) pawn.needs.rest.CurLevelPercentage = 1f;
        }

        private static void BankResults(SettlementServicesWorldComponent domain, ServiceJobRecord job, List<Thing> resultThings)
        {
            if (resultThings.Count == 0) return;

            foreach (Thing resultThing in resultThings)
            {
                domain.TakeItemCustody(resultThing);
                job.results.Add(new TargetSnapshot
                {
                    kind = TargetKind.Item,
                    liveThing = resultThing,
                    snapshotLabel = resultThing.LabelCap,
                    snapshotDefName = resultThing.def?.defName,
                    snapshotQuality = resultThing.TryGetQuality(out QualityCategory q) ? q : (QualityCategory?)null,
                });
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Custody;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Events;
using Settlement_Services.Framework.Payment;
using Settlement_Services.Framework.Pricing;
using Settlement_Services.Framework.Validation;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Framework
{
    public static class SettlementServiceOrchestrator
    {
        public static SettlementServiceQuote RequestQuote(SettlementServiceRequest request, SettlementServiceDef def)
        {
            if (!ServiceBatchPlanner.TryBuildBatchQuote(def, request, out SettlementServiceQuote quote, out string errorKey))
                return SettlementServiceQuote.Invalid(errorKey);
            return quote;
        }

        internal static bool TierGoodwillMet(SettlementServiceDef def, SettlementServiceRequest request, out string errorKey)
        {
            ServicePriorityTierDef tier = ServicePricingEngine.ResolveTier(def, request.selectedTierKey);
            Faction faction = request.settlement?.Faction;
            if (tier != null && tier.minimumGoodwill != int.MinValue && faction != null && faction.PlayerGoodwill < tier.minimumGoodwill)
            {
                errorKey = "SettlementServices.Error.PriorityTierRequiresHigherGoodwill";
                return false;
            }

            errorKey = null;
            return true;
        }

        public static ServiceJobRecord CreateDraftJob(SettlementServiceRequest request, SettlementServiceDef def)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            Caravan caravan = request.negotiator?.GetCaravan();
            string requesterFactionLoadId = (caravan?.Faction ?? Faction.OfPlayer)?.GetUniqueLoadID();
            List<TargetSnapshot> snapshots = request.targets.Select(t => t.ToSnapshot()).Where(s => s != null).ToList();
            int quantity = def.EffectiveBatchMode == ServiceBatchMode.Quantity ? Mathf.Max(1, request.quantity) : 1;

            return domain.CreateJob(
                request.settlement.ID,
                def.defName,
                request.channel,
                snapshots,
                quantity,
                caravan?.ID ?? -1,
                requesterFactionLoadId,
                caravan?.LabelCap);
        }

        public static bool AcceptQuote(int jobId, SettlementServiceQuote quote, SettlementServiceRequest request)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            ServiceJobRecord job = domain.GetJob(jobId);
            if (job == null || job.status != ServiceJobStatus.Quoted || !quote.IsValid) return false;

            SettlementServiceDef def = DefDatabase<SettlementServiceDef>.GetNamedSilentFail(job.serviceDefName);
            if (def == null) return false;

            if (!ServiceBatchPlanner.TryBuildAcceptancePlan(def, request, out ServiceBatchPlan plan, out string planErrorKey))
            {
                job.lastErrorKey = planErrorKey;
                return false;
            }

            IServicePaymentProvider provider = ResolvePaymentProvider(job.requestChannel);
            if (!provider.TryDebit(plan.Quote.totalCost, request, out string payErrorKey))
            {
                job.lastErrorKey = payErrorKey;
                return false;
            }

            foreach (ThingDefCountClass stockItem in plan.InputPlan.stockConsumed)
                domain.Reserve(jobId, stockItem.thingDef.defName, stockItem.count);

            Caravan caravan = request.negotiator?.GetCaravan();
            if (!plan.InputPlan.playerSuppliedConsumed.NullOrEmpty()
                && !CaravanInventoryTransfer.TryConsume(caravan, plan.InputPlan.playerSuppliedConsumed, out string consumeErrorKey))
            {
                domain.ReleaseAllReservations(jobId);
                provider.Refund(plan.Quote.totalCost, new ServiceJobContext(domain, job));
                job.lastErrorKey = consumeErrorKey;
                return false;
            }

            if (domain.TryTransition(jobId, ServiceJobStatus.Reserved))
            {
                domain.FreezeQuote(jobId, plan.Quote);
                domain.FreezeOptionSelections(jobId, request);
                if (plan.AcceptedWorkerData is CraftingProductionPlan productionPlan)
                    domain.FreezeCraftingProductionPlan(jobId, productionPlan);
                domain.FreezeConsumedPlayerSuppliedInputs(jobId, plan.InputPlan.playerSuppliedConsumed);
                return true;
            }

            domain.ReleaseAllReservations(jobId);
            if (!plan.InputPlan.playerSuppliedConsumed.NullOrEmpty()) CaravanInventoryTransfer.Refund(caravan, plan.InputPlan.playerSuppliedConsumed);
            provider.Refund(plan.Quote.totalCost, new ServiceJobContext(domain, job));
            return false;
        }

        public static bool StartJob(int jobId)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            ServiceJobRecord job = domain.GetJob(jobId);
            if (job == null || job.status != ServiceJobStatus.Reserved) return false;

            SettlementServiceDef def = DefDatabase<SettlementServiceDef>.GetNamedSilentFail(job.serviceDefName);
            if (def == null) return false;

            var ctx = new ServiceJobContext(domain, job);
            Caravan caravan = ResolveRequesterCaravan(job);

            if (!TargetCustodyService.TryTakeCustody(ctx, caravan, out string custodyErrorKey))
            {
                FailJob(domain, jobId, custodyErrorKey);
                return false;
            }

            List<TargetSnapshot> targets = job.Targets.ToList();
            ServiceStartResult result = ServiceStartResult.Ok;
            if (targets.Count > 0)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    result = def.Worker.Start(ctx.ForTarget(targets[i], i));
                    if (!result.Success) break;
                }
            }
            else
            {
                result = def.Worker.Start(ctx);
            }

            if (!result.Success)
            {
                TargetCustodyService.ReturnCustody(job, caravan);
                FailJob(domain, jobId, result.ErrorKey);
                return false;
            }

            if (!domain.TryTransition(jobId, ServiceJobStatus.Active))
            {
                TargetCustodyService.ReturnCustody(job, caravan);
                return false;
            }

            foreach (string thingDefName in domain.ReservationsForJob(jobId).Select(r => r.stockThingDefName).ToList())
                domain.ConsumeReservedStock(jobId, thingDefName);

            int duration = job.acceptedQuote?.expectedDurationTicks ?? def.duration;
            job.expectedCompletionTick = Find.TickManager.TicksGame + duration;

            job.eventTargetIndex = SelectEventTargetIndex(targets);
            ServiceEventRollService.RollAndPresent(domain, def, job, ctx.ForUnitIndex(job.eventTargetIndex));

            if (duration <= 0) SettlementServiceJobScheduler.CompleteJob(domain, def, job, ctx);
            return true;
        }

        private static int SelectEventTargetIndex(List<TargetSnapshot> targets)
        {
            for (int i = 0; i < targets.Count; i++)
                if (targets[i]?.liveThing is Pawn) return i;
            return -1;
        }

        public static bool CollectJob(int jobId, Caravan collectingCaravan)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            ServiceJobRecord job = domain.GetJob(jobId);
            if (job == null || job.status != ServiceJobStatus.AwaitingCollection) return false;
            if (collectingCaravan == null || collectingCaravan.Faction != Faction.OfPlayer) return false;

            var ctx = new ServiceJobContext(domain, job);
            Settlement settlement = ctx.ResolveSettlement();
            if (settlement == null || collectingCaravan.Tile != settlement.Tile) return false;

            TargetCustodyService.CollectAll(job, collectingCaravan);
            return domain.TryTransition(jobId, ServiceJobStatus.Collected);
        }

        private static Caravan ResolveRequesterCaravan(ServiceJobRecord job) =>
            job.requesterCaravanId < 0 ? null : Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == job.requesterCaravanId);

        public static void CancelJob(int jobId, bool playerInitiated)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            ServiceJobRecord job = domain.GetJob(jobId);
            if (job == null) return;

            if (job.status == ServiceJobStatus.Drafted || job.status == ServiceJobStatus.Quoted)
            {
                domain.TryTransition(jobId, ServiceJobStatus.Cancelled);
                return;
            }

            if (job.status != ServiceJobStatus.Reserved && job.status != ServiceJobStatus.Active) return;

            SettlementServiceDef def = DefDatabase<SettlementServiceDef>.GetNamedSilentFail(job.serviceDefName);
            var ctx = new ServiceJobContext(domain, job);

            ServiceCancelResult result = def != null ? def.Worker.Cancel(ctx, playerInitiated) : ServiceCancelResult.Ok();
            if (!result.Success)
            {
                job.lastErrorKey = result.ErrorKey;
                return;
            }

            if (job.acceptedQuote != null)
            {
                IServicePaymentProvider provider = ResolvePaymentProvider(job.requestChannel);
                int refundAmount = result.RefundLineItems != null
                    ? result.RefundLineItems.Sum(li => li.amount)
                    : job.acceptedQuote.totalCost;
                if (refundAmount > 0) provider.Refund(refundAmount, ctx);
            }

            domain.ReleaseAllReservations(jobId);
            RouteToTerminalOrCollectible(domain, job, "SettlementServices.Error.CancelledByPlayer", ServiceJobStatus.Cancelled);
        }

        private const string CraftingRecipeUnavailableErrorKey = "SettlementServices.Error.CommissionedItemNoLongerAvailable";

        public static void FailJob(SettlementServicesWorldComponent domain, int jobId, string errorKey) =>
            FailJob(domain, jobId, errorKey, 1f);

        public static void FailJob(SettlementServicesWorldComponent domain, int jobId, string errorKey, float refundFraction)
        {
            ServiceJobRecord job = domain.GetJob(jobId);
            if (job == null) return;

            RefundConsumedPlayerSuppliedInputsForMissingRecipe(domain, job, errorKey);

            if (job.acceptedQuote != null && job.acceptedQuote.totalCost > 0 && refundFraction > 0f)
            {
                var ctx = new ServiceJobContext(domain, job);
                IServicePaymentProvider provider = ResolvePaymentProvider(job.requestChannel);
                int refundAmount = Mathf.RoundToInt(job.acceptedQuote.totalCost * Mathf.Clamp01(refundFraction));
                if (refundAmount > 0) provider.Refund(refundAmount, ctx);
            }

            domain.ReleaseAllReservations(jobId);

            bool neverReserved = job.status == ServiceJobStatus.Drafted || job.status == ServiceJobStatus.Quoted;
            RouteToTerminalOrCollectible(domain, job, errorKey, neverReserved ? ServiceJobStatus.Cancelled : ServiceJobStatus.Failed);
        }

        private static void RefundConsumedPlayerSuppliedInputsForMissingRecipe(SettlementServicesWorldComponent domain, ServiceJobRecord job, string errorKey)
        {
            if (errorKey != CraftingRecipeUnavailableErrorKey) return;
            if (job.playerSuppliedInputsRefunded || job.consumedPlayerSuppliedInputs.NullOrEmpty()) return;

            Caravan caravan = ResolveRequesterCaravan(job);
            if (caravan != null)
            {
                CaravanInventoryTransfer.Refund(caravan, job.consumedPlayerSuppliedInputs);
            }
            else
            {
                foreach (ThingDefCountClass item in job.consumedPlayerSuppliedInputs)
                {
                    int remaining = Mathf.Max(1, item.count);
                    while (remaining > 0)
                    {
                        int stackCount = Mathf.Min(remaining, Mathf.Max(1, item.thingDef.stackLimit));
                        Thing thing = ThingMaker.MakeThing(item.thingDef);
                        thing.stackCount = stackCount;
                        domain.TakeItemCustody(thing);
                        job.results.Add(new TargetSnapshot
                        {
                            kind = TargetKind.Item,
                            liveThing = thing,
                            snapshotLabel = thing.LabelCap,
                            snapshotDefName = thing.def?.defName,
                            snapshotQuality = thing.TryGetQuality(out QualityCategory q) ? q : (QualityCategory?)null,
                        });
                        remaining -= stackCount;
                    }
                }
            }

            domain.MarkPlayerSuppliedInputsRefunded(job.jobId);
        }

        private static void RouteToTerminalOrCollectible(SettlementServicesWorldComponent domain, ServiceJobRecord job, string reasonKey, ServiceJobStatus terminalStatus)
        {
            job.lastErrorKey = reasonKey;
            bool holdsSomething = job.targetInCustody || !job.results.NullOrEmpty();

            if (holdsSomething)
            {
                if (new ServiceJobContext(domain, job).ResolveSettlement() != null)
                {
                    domain.TryTransition(job.jobId, ServiceJobStatus.AwaitingCollection);
                    SettlementServiceNotifier.NotifyInterrupted(job);
                }
                else
                {
                    domain.TryTransition(job.jobId, ServiceJobStatus.AwaitingCollection);
                    TargetCustodyService.QueueHomeDeliveryForJob(domain, job);
                    domain.TryTransition(job.jobId, ServiceJobStatus.Collected);
                    SettlementServiceNotifier.NotifyDeliveredHome(job);
                }
                return;
            }

            domain.TryTransition(job.jobId, terminalStatus);
            if (terminalStatus == ServiceJobStatus.Failed) SettlementServiceNotifier.NotifyFailed(job);
        }

        internal static IServicePaymentProvider ResolvePaymentProvider(RequestChannel channel)
        {
            return channel == RequestChannel.Remote
                ? (IServicePaymentProvider)HomeColonySilverPaymentProvider.Instance
                : CaravanSilverPaymentProvider.Instance;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Framework.Workers
{
    public abstract class SettlementServiceWorker
    {
        public SettlementServiceDef def;

        public virtual ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx) => ServiceAvailabilityReport.Available;

        public abstract IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request);

        public virtual IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request, ServiceBatchAllocationContext batchContext) =>
            BuildQuoteLineItems(request);

        public virtual ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote) => ServiceInputPlan.None;

        public virtual ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote, ServiceBatchAllocationContext batchContext) =>
            PlanInputs(request, quote);

        public virtual object BuildAcceptedData(SettlementServiceRequest request, SettlementServiceQuote quote) => null;

        public virtual string ValidateUnitRequest(SettlementServiceRequest request) => null;

        public abstract ServiceStartResult Start(ServiceJobContext ctx);

        public virtual ServiceTickResult Tick(ServiceJobContext ctx, int ticksSinceLastCall) => ServiceTickResult.NoChange;

        public abstract ServiceCompletionResult Complete(ServiceJobContext ctx);

        public abstract ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated);

        public virtual IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx) => Enumerable.Empty<ServiceDisplayOption>();

        public virtual IEnumerable<string> GetDisplaySummaryLines(SettlementServiceContext ctx) => Enumerable.Empty<string>();

        public virtual IEnumerable<ServiceEventDef> GetEventPool(ServiceJobContext ctx) => Enumerable.Empty<ServiceEventDef>();

        public virtual List<ServiceStockRequirement> GetDynamicStockRequirements(SettlementServiceRequest request) => new List<ServiceStockRequirement>();

        public virtual float DurationMultiplierFor(SettlementServiceRequest request) => 1f;

        public virtual int? BaseDurationTicksFor(SettlementServiceRequest request) => null;

        public virtual int? ExpectedDurationTicksFor(SettlementServiceRequest request) => null;

        public virtual float WealthScaleAdditionFor(SettlementServiceRequest request) => 0f;

        public virtual float MinimumCostMultiplierFor(SettlementServiceRequest request) => 1f;

        public virtual float BasePriceMultiplierFor(SettlementServiceRequest request) => 1f;

        public virtual float EventChanceMultiplierFor(ServiceJobContext ctx) => 1f;

        public virtual bool UsesPerTargetOptionSelections => false;

        public virtual bool RequiresTargetCustody => true;

        protected int ExpectedDurationTicks(ServiceJobContext ctx)
        {
            int quoted = ctx.Job.acceptedQuote?.expectedDurationTicks ?? 0;
            return Mathf.Max(1, quoted > 0 ? quoted : def.duration);
        }
    }
}

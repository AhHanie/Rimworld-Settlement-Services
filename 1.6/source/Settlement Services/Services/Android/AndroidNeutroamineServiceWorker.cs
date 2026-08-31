using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Compat;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Android
{
    public class AndroidNeutroamineServiceWorker : SettlementServiceWorker
    {
        private const float NeutroaminePerSeverityUnit = 100f;
        private const float MarkupPct = 1.4f;
        private const float ReferenceSeverity = 0.5f;
        private const float MinDurationFactor = 0.25f;
        private const float MaxDurationFactor = 2f;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            Thing android = ctx.SelectedTarget;
            if (android == null) return ServiceAvailabilityReport.Available;
            Hediff hediff = NeutroLossHediff(android as Pawn);
            return hediff != null && hediff.Severity > 0f
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.AndroidNoNeutrolossNeeded");
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request)
        {
            var items = new List<ServiceLineItem>();
            int cost = SettlementSuppliedNeutroamineCost(request);
            if (cost > 0) items.Add(new ServiceLineItem("SettlementServices.LineItem.AndroidNeutroamineTopUp", cost));
            return items;
        }

        public override float DurationMultiplierFor(SettlementServiceRequest request)
        {
            Hediff hediff = NeutroLossHediff(request.target.thing as Pawn);
            return hediff == null ? 1f : Mathf.Clamp(hediff.Severity / ReferenceSeverity, MinDurationFactor, MaxDurationFactor);
        }

        public override List<ServiceStockRequirement> GetDynamicStockRequirements(SettlementServiceRequest request)
        {
            var result = new List<ServiceStockRequirement>();
            int needed = NeutroamineNeeded(request.target.thing as Pawn);
            ThingDef neutroamine = AndroidsAdapter.NeutroamineThingDef;
            if (needed > 0 && neutroamine != null && SettlementStockCatalog.ItemFor(neutroamine) != null)
                result.Add(new ServiceStockRequirement { thingDefName = neutroamine.defName, amount = needed, playerCanSupply = true });
            return result;
        }

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote)
        {
            int needed = NeutroamineNeeded(request.target.thing as Pawn);
            ThingDef neutroamine = AndroidsAdapter.NeutroamineThingDef;
            if (needed <= 0 || neutroamine == null || SettlementStockCatalog.ItemFor(neutroamine) == null) return ServiceInputPlan.None;

            var requirement = new ServiceStockRequirement { thingDefName = neutroamine.defName, amount = needed, playerCanSupply = true };
            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, new List<ServiceStockRequirement> { requirement }, request.playerSuppliedInputs);
            if (!allocation.Success) return ServiceInputPlan.None;

            return new ServiceInputPlan { stockConsumed = allocation.SettlementSupplied, playerSuppliedConsumed = allocation.PlayerSupplied };
        }

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            Hediff hediff = NeutroLossHediff(ctx.CurrentTarget?.liveThing as Pawn);
            if (hediff == null) return ServiceCompletionResult.Fail("SettlementServices.Error.TargetNoLongerExists");

            hediff.Severity = 0f;
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static Hediff NeutroLossHediff(Pawn android)
        {
            if (!AndroidsAdapter.IsAndroid(android) || AndroidsAdapter.NeutroLossHediffDef == null) return null;
            GeneDef geneDef = AndroidsAdapter.NeutroCirculationGeneDef;
            if (geneDef == null || android.genes?.GetGene(geneDef)?.Active != true) return null;
            return android.health.hediffSet.GetFirstHediffOfDef(AndroidsAdapter.NeutroLossHediffDef);
        }

        private static int NeutroamineNeeded(Pawn android)
        {
            Hediff hediff = NeutroLossHediff(android);
            return hediff == null ? 0 : Mathf.CeilToInt(hediff.Severity * NeutroaminePerSeverityUnit);
        }

        private static int SettlementSuppliedNeutroamineCost(SettlementServiceRequest request)
        {
            int needed = NeutroamineNeeded(request.target.thing as Pawn);
            ThingDef neutroamine = AndroidsAdapter.NeutroamineThingDef;
            if (needed <= 0 || neutroamine == null) return 0;

            int suppliedByPlayer = Mathf.Min(needed, request.playerSuppliedInputs.Where(i => i.thingDef == neutroamine).Sum(i => i.count));
            int fromSettlement = needed - suppliedByPlayer;
            return Mathf.RoundToInt(fromSettlement * neutroamine.BaseMarketValue * MarkupPct);
        }
    }
}

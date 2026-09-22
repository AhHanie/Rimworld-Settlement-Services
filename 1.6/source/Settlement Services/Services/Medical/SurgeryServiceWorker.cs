using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Medical
{
    public class SurgeryServiceWorker : SettlementServiceWorker
    {
        private const string ImplantGroupKey = "SettlementServices.Label.ImplantChoice";
        private const string MedicineStockCategory = "SettlementStock_Medicine";
        private const int MedicineAmount = 2;
        private const int OperationDurationTicks = 2 * GenDate.TicksPerHour;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) return ServiceAvailabilityReport.Available;
            return SurgeryOptionService.FindOfferedOptions(pawn, ctx.Settlement, ctx.RequestingCaravan).Any()
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoCompatibleImplants");
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) yield break;
            List<SurgeryOptionService.ImplantOption> allOptions = SurgeryOptionService.FindOfferedOptions(pawn, ctx.Settlement, ctx.RequestingCaravan);
            foreach (SurgeryOptionService.ImplantOption option in allOptions)
                yield return new ServiceDisplayOption
                {
                    key = option.Key,
                    label = option.Label,
                    groupKey = ImplantGroupKey,
                    allowMultipleSelectionInGroup = true,
                    conflictingOptionKeys = SurgeryOptionService.ConflictingKeysFor(option, allOptions),
                };
        }

        public override IEnumerable<string> GetDisplaySummaryLines(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) yield break;
            if (!SurgeryOptionService.TryResolveSelected(pawn, ctx.SelectedOptionKeys, out List<SurgeryOptionService.ImplantOption> resolved, out _)) yield break;
            if (resolved.Count == 0) yield break;

            yield return "SettlementServices.Label.SurgeryPlayerSuppliedExplanation".Translate();
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) =>
            BuildQuoteLineItems(request, new ServiceBatchAllocationContext());

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request, ServiceBatchAllocationContext batchContext)
        {
            var lineItems = new List<ServiceLineItem>();
            if (!(request.target.thing is Pawn pawn)) return lineItems;

            List<SurgeryOptionService.ImplantOption> resolved = SurgeryOptionService.ResolveAvailable(pawn, request.selectedOptionKeys);
            if (resolved.Count == 0) return lineItems;

            ServiceInputPlan plan = batchContext.GetOrCreateInputPlan(request, () => AllocateInputs(request, resolved, batchContext.StockLedger));

            float medicineCostRaw = 0f;
            var settlementSuppliedParts = new List<ThingDefCountClass>();
            foreach (ThingDefCountClass entry in plan.stockConsumed)
            {
                if (SettlementStockCatalog.CategoryFor(entry.thingDef)?.defName == MedicineStockCategory)
                    medicineCostRaw += entry.count * entry.thingDef.BaseMarketValue;
                else
                    settlementSuppliedParts.Add(new ThingDefCountClass(entry.thingDef, entry.count));
            }

            int medicineCost = Mathf.RoundToInt(medicineCostRaw);
            if (medicineCost > 0)
                lineItems.Add(new ServiceLineItem("SettlementServices.LineItem.SurgeryMedicine", medicineCost));

            foreach (SurgeryOptionService.ImplantOption option in resolved)
            {
                ThingDefCountClass supplied = settlementSuppliedParts.Find(c => c.thingDef == option.itemDef);
                if (supplied == null || supplied.count <= 0) continue;

                supplied.count--;
                lineItems.Add(new ServiceLineItem("SettlementServices.LineItem.SurgeryPart", Mathf.RoundToInt(option.itemDef.BaseMarketValue), labelArgument: option.Label));
            }

            return lineItems;
        }

        public override List<ServiceStockRequirement> GetDynamicStockRequirements(SettlementServiceRequest request)
        {
            var result = new List<ServiceStockRequirement>();
            if (!(request.target.thing is Pawn pawn)) return result;

            List<SurgeryOptionService.ImplantOption> resolved = SurgeryOptionService.ResolveAvailable(pawn, request.selectedOptionKeys);
            foreach (SurgeryOptionService.ImplantOption option in resolved)
                result.Add(new ServiceStockRequirement { thingDefName = option.itemDef.defName, amount = 1, playerCanSupply = true });
            return result;
        }

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote) =>
            PlanInputs(request, quote, new ServiceBatchAllocationContext());

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote, ServiceBatchAllocationContext batchContext)
        {
            if (!(request.target.thing is Pawn pawn)) return new ServiceInputPlan();

            List<SurgeryOptionService.ImplantOption> resolved = SurgeryOptionService.ResolveAvailable(pawn, request.selectedOptionKeys);
            return batchContext.GetOrCreateInputPlan(request, () => AllocateInputs(request, resolved, batchContext.StockLedger));
        }

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            if (!(request.target.thing is Pawn pawn)) return null;
            if (request.selectedOptionKeys.Count == 0) return null;
            return SurgeryOptionService.TryResolveSelected(pawn, request.selectedOptionKeys, out _, out string errorKey) ? null : errorKey;
        }

        public override int? BaseDurationTicksFor(SettlementServiceRequest request)
        {
            if (!(request.target.thing is Pawn pawn)) return null;
            if (!SurgeryOptionService.TryResolveSelected(pawn, request.selectedOptionKeys, out List<SurgeryOptionService.ImplantOption> resolved, out _)) return null;
            if (resolved.Count == 0) return null;
            return def.duration + resolved.Count * OperationDurationTicks;
        }

        public override ServiceStartResult Start(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTargetThing is Pawn pawn)) return ServiceStartResult.Ok;
            return SurgeryOptionService.TryResolveSelected(pawn, ctx.SelectedOptionKeys, out _, out string errorKey)
                ? ServiceStartResult.Ok
                : ServiceStartResult.Fail(errorKey);
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTarget?.liveThing is Pawn pawn)) return ServiceCompletionResult.Ok();

            IReadOnlyList<string> keys = ctx.SelectedOptionKeys;
            if (keys.Count == 0) return ServiceCompletionResult.Ok();

            foreach (string key in keys)
            {
                SurgeryOptionService.ImplantOption? option = SurgeryOptionService.FindByKey(pawn, key);
                if (option == null) return ServiceCompletionResult.Fail("SettlementServices.Error.NoCompatibleImplants");
                PerformSurgery(pawn, option.Value);
            }

            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static ServiceInputPlan AllocateInputs(SettlementServiceRequest request, List<SurgeryOptionService.ImplantOption> resolved, StockAllocationLedger ledger)
        {
            if (resolved.Count == 0) return new ServiceInputPlan();

            var requirements = new List<ServiceStockRequirement>
            {
                new ServiceStockRequirement { stockCategoryDefName = MedicineStockCategory, preferredThingDefName = "MedicineIndustrial", amount = MedicineAmount, playerCanSupply = true },
            };
            foreach (SurgeryOptionService.ImplantOption option in resolved)
                requirements.Add(new ServiceStockRequirement { thingDefName = option.itemDef.defName, amount = 1, playerCanSupply = true });

            StockAllocationResult allocation = SettlementStockService.TryAllocate(request.settlement, requirements, request.playerSuppliedInputs, ledger);
            if (!allocation.Success) return new ServiceInputPlan();

            return new ServiceInputPlan { stockConsumed = allocation.SettlementSupplied, playerSuppliedConsumed = allocation.PlayerSupplied };
        }

        private static void PerformSurgery(Pawn pawn, SurgeryOptionService.ImplantOption option)
        {
            option.recipe.Worker.ApplyOnPawn(pawn, option.part, null, new List<Thing>(), null);
        }
    }
}

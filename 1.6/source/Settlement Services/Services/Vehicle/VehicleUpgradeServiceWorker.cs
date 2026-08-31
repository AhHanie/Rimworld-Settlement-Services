using System.Collections.Generic;
using System.Linq;
using Verse;
using Settlement_Services.Framework.Compat;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Vehicle
{
    public class VehicleUpgradeServiceWorker : SettlementServiceWorker
    {
        private const string UpgradeGroupKey = "SettlementServices.Label.VehicleUpgradeChoice";
        private const float MaterialMarkupPct = 1.4f;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            Thing vehicle = ctx.SelectedTarget;
            if (vehicle == null) return ServiceAvailabilityReport.Available;
            return VehicleFrameworkAdapter.HasUpgradeTree(vehicle) && VehicleFrameworkAdapter.GetInstallableUpgrades(vehicle).Count > 0
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoVehicleUpgradesAvailable");
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            Thing vehicle = ctx.SelectedTarget;
            if (vehicle == null) yield break;
            foreach (VehicleUpgradeOption option in VehicleFrameworkAdapter.GetInstallableUpgrades(vehicle))
                yield return new ServiceDisplayOption { key = option.key, label = option.label, groupKey = UpgradeGroupKey };
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request)
        {
            var items = new List<ServiceLineItem>();

            VehicleUpgradeOption option = ResolveOption(request.target.thing, request.selectedOptionKeys);
            int materialsCost = option != null ? VehicleMaterialPlanner.SettlementSuppliedCost(request, option.ingredients, MaterialMarkupPct) : 0;
            if (materialsCost > 0) items.Add(new ServiceLineItem("SettlementServices.LineItem.VehicleUpgradeMaterials", materialsCost));
            return items;
        }

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote)
        {
            VehicleUpgradeOption option = ResolveOption(request.target.thing, request.selectedOptionKeys);
            return option != null ? VehicleMaterialPlanner.PlanInputs(request, option.ingredients) : ServiceInputPlan.None;
        }

        public override List<ServiceStockRequirement> GetDynamicStockRequirements(SettlementServiceRequest request)
        {
            VehicleUpgradeOption option = ResolveOption(request.target.thing, request.selectedOptionKeys);
            return option != null ? VehicleMaterialPlanner.RequirementsFor(option.ingredients) : new List<ServiceStockRequirement>();
        }

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            if (request.selectedOptionKeys.Count == 0) return null;
            return ResolveOption(request.target.thing, request.selectedOptionKeys) != null ? null : "SettlementServices.Error.VehicleUpgradeNoLongerAvailable";
        }

        public override ServiceStartResult Start(ServiceJobContext ctx)
        {
            Thing vehicle = ctx.CurrentTarget?.liveThing;
            VehicleUpgradeOption option = vehicle != null ? ResolveOption(vehicle, ctx.Job.selectedOptionKeys) : null;
            return option != null ? ServiceStartResult.Ok : ServiceStartResult.Fail("SettlementServices.Error.VehicleUpgradeNoLongerAvailable");
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            Thing vehicle = ctx.CurrentTarget?.liveThing;
            VehicleUpgradeOption option = vehicle != null ? ResolveOption(vehicle, ctx.Job.selectedOptionKeys) : null;
            if (option == null || !VehicleFrameworkAdapter.InstallUpgrade(vehicle, option.key))
                return ServiceCompletionResult.Fail("SettlementServices.Error.VehicleUpgradeNoLongerAvailable");
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static VehicleUpgradeOption ResolveOption(Thing vehicle, IReadOnlyList<string> selectedOptionKeys)
        {
            if (vehicle == null || selectedOptionKeys == null || selectedOptionKeys.Count == 0) return null;
            List<VehicleUpgradeOption> options = VehicleFrameworkAdapter.GetInstallableUpgrades(vehicle);
            return selectedOptionKeys.Select(k => options.FirstOrDefault(o => o.key == k)).FirstOrDefault(o => o != null);
        }
    }
}

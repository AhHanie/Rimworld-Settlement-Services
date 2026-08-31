using System.Collections.Generic;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Compat;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Vehicle
{
    public class VehicleRepairServiceWorker : SettlementServiceWorker
    {
        public const string FullTierKey = "Full";
        private const float QuickRepairTargetPercent = 0.6f;
        private const float ReferenceDamageFraction = 0.5f;
        private const float MaxRepairFactor = 2.5f;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            Thing vehicle = ctx.SelectedTarget;
            if (vehicle == null) return ServiceAvailabilityReport.Available;
            return VehicleFrameworkAdapter.NeedsRepairs(vehicle)
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.VehicleNeedsNoRepairs");
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override float DurationMultiplierFor(SettlementServiceRequest request) =>
            Mathf.Max(0.1f, RepairFactor(request.target.thing, request.selectedTierKey));

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            Thing vehicle = ctx.CurrentTarget?.liveThing;
            if (vehicle == null) return ServiceCompletionResult.Fail("SettlementServices.Error.TargetNoLongerExists");

            string tierKey = ctx.Job.acceptedQuote?.selectedTierKey;
            VehicleFrameworkAdapter.RepairTo(vehicle, tierKey == FullTierKey ? 1f : QuickRepairTargetPercent);
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static float RepairFactor(Thing vehicle, string tierKey)
        {
            if (vehicle == null) return 0f;
            float healthPercent = VehicleFrameworkAdapter.HealthPercent(vehicle);
            float targetPercent = tierKey == FullTierKey ? 1f : QuickRepairTargetPercent;
            float amountRestored = Mathf.Max(0f, targetPercent - healthPercent);
            return Mathf.Clamp(amountRestored / ReferenceDamageFraction, 0f, MaxRepairFactor);
        }
    }
}

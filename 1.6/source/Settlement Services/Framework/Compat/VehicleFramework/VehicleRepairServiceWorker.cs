using System.Collections.Generic;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Framework.Compat.VehicleFramework
{
    public class VehicleRepairServiceWorker : SettlementServiceWorker
    {
        private const float FullRepairTargetPercent = 1f;
        private const float ReferenceDamageFraction = 0.5f;
        private const float MinRepairFactor = 0.1f;
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

        public override float DurationMultiplierFor(SettlementServiceRequest request) => RepairFactor(request.target.thing);

        public override float BasePriceMultiplierFor(SettlementServiceRequest request) => RepairFactor(request.target.thing);

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            Thing vehicle = ctx.CurrentTarget?.liveThing;
            if (vehicle == null) return ServiceCompletionResult.Fail("SettlementServices.Error.TargetNoLongerExists");

            VehicleFrameworkAdapter.RepairTo(vehicle, FullRepairTargetPercent);
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static float RepairFactor(Thing vehicle)
        {
            if (vehicle == null) return MinRepairFactor;
            float damageFraction = Mathf.Clamp01(FullRepairTargetPercent - VehicleFrameworkAdapter.HealthPercent(vehicle));
            return Mathf.Clamp(damageFraction / ReferenceDamageFraction, MinRepairFactor, MaxRepairFactor);
        }
    }
}

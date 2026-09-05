using System.Collections.Generic;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Compatibility;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Technical
{
    public class BionicRechargeServiceWorker : SettlementServiceWorker
    {
        private const float ReferenceDeficitFraction = 0.5f;
        private const float MinDurationFactor = 0.25f;
        private const float MaxDurationFactor = 2f;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!SettlementServicesCompatibilityRegistry.RechargeableHediffService.IsAvailable)
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.ChargeableHediffsUnavailable");

            Thing target = ctx.SelectedTarget;
            if (target == null) return ServiceAvailabilityReport.Available;

            RechargeableHediffStatus status = SettlementServicesCompatibilityRegistry.RechargeableHediffService.Inspect(target as Pawn);
            return status.NeedsCharge
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoRechargeableBionicsNeedCharge");
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override float DurationMultiplierFor(SettlementServiceRequest request)
        {
            RechargeableHediffStatus status = SettlementServicesCompatibilityRegistry.RechargeableHediffService.Inspect(request.target.thing as Pawn);
            if (!status.HasRechargeableHediffs) return 1f;
            return Mathf.Clamp(status.DeficitFraction / ReferenceDeficitFraction, MinDurationFactor, MaxDurationFactor);
        }

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTarget?.liveThing is Pawn pawn) || pawn.Destroyed)
                return ServiceCompletionResult.Fail("SettlementServices.Error.TargetNoLongerExists");

            if (!SettlementServicesCompatibilityRegistry.RechargeableHediffService.TryRechargeFully(pawn, out string errorKey))
                return ServiceCompletionResult.Fail(errorKey);

            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();
    }
}

using System.Collections.Generic;
using UnityEngine;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Mechanitor
{
    public class MechRechargeServiceWorker : SettlementServiceWorker
    {
        private const float ReferenceDeficitFraction = 0.5f;
        private const float MinDurationFactor = 0.25f;
        private const float MaxDurationFactor = 2f;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            Thing mech = ctx.SelectedTarget;
            if (mech == null) return ServiceAvailabilityReport.Available;
            return DeficitFraction(mech as Pawn) > 0f
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.MechEnergyAlreadyFull");
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override float DurationMultiplierFor(SettlementServiceRequest request) => DurationFactor(request.target.thing as Pawn);

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTarget?.liveThing is Pawn mech) || mech.needs?.energy == null)
                return ServiceCompletionResult.Fail("SettlementServices.Error.TargetNoLongerExists");

            mech.needs.energy.CurLevel = mech.needs.energy.MaxLevel;
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static float DeficitFraction(Pawn mech)
        {
            if (mech?.needs?.energy == null) return 0f;
            Need_MechEnergy energy = mech.needs.energy;
            return Mathf.Max(0f, energy.MaxLevel - energy.CurLevel) / energy.MaxLevel;
        }

        private static float DurationFactor(Pawn mech) =>
            Mathf.Clamp(DeficitFraction(mech) / ReferenceDeficitFraction, MinDurationFactor, MaxDurationFactor);
    }
}

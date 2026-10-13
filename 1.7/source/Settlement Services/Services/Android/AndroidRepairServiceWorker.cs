using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Compat;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Android
{
    public class AndroidRepairServiceWorker : SettlementServiceWorker
    {
        private const float ReferenceDamageFraction = 0.5f;
        private const float MaxRepairFactor = 2.5f;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            Thing android = ctx.SelectedTarget;
            if (android == null) return ServiceAvailabilityReport.Available;
            Pawn pawn = android as Pawn;
            if (pawn == null || !AndroidsAdapter.AutoRepairEnabled(pawn))
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.AndroidAutoRepairDisabled");
            return HasRepairableDamage(pawn)
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.AndroidNeedsNoRepairs");
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override float DurationMultiplierFor(SettlementServiceRequest request) =>
            Mathf.Max(0.1f, RepairFactor(request.target.thing as Pawn));

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTarget?.liveThing is Pawn android) || !AndroidsAdapter.IsAndroid(android))
                return ServiceCompletionResult.Fail("SettlementServices.Error.TargetNoLongerExists");

            foreach (Hediff hediff in android.health.hediffSet.hediffs.Where(h => h is Hediff_Injury).ToList())
                HealthUtility.Cure(hediff);

            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static bool HasRepairableDamage(Pawn android) =>
            AndroidsAdapter.IsAndroid(android) && android.health.hediffSet.hediffs.Any(h => h is Hediff_Injury);

        private static float RepairFactor(Pawn android)
        {
            if (android == null) return 0f;
            float amountToHeal = 1f - android.health.summaryHealth.SummaryHealthPercent;
            return Mathf.Clamp(amountToHeal / ReferenceDamageFraction, 0f, MaxRepairFactor);
        }
    }
}

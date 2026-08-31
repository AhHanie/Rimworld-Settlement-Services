using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Mechanitor
{
    public class MechRepairServiceWorker : SettlementServiceWorker
    {
        private const float ReferenceDamageFraction = 0.5f;
        private const float MaxRepairFactor = 2.5f;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            Thing mech = ctx.SelectedTarget;
            if (mech == null) return ServiceAvailabilityReport.Available;
            return HasRepairableDamage(mech as Pawn)
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.MechNeedsNoRepairs");
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override float DurationMultiplierFor(SettlementServiceRequest request) =>
            Mathf.Max(0.1f, RepairFactor(request.target.thing as Pawn));

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTarget?.liveThing is Pawn mech) || mech.TryGetComp<CompMechRepairable>() == null)
                return ServiceCompletionResult.Fail("SettlementServices.Error.TargetNoLongerExists");

            foreach (Hediff hediff in mech.health.hediffSet.hediffs.Where(h => h is Hediff_Injury || h is Hediff_MissingPart).ToList())
                HealthUtility.Cure(hediff);

            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static bool HasRepairableDamage(Pawn mech) =>
            mech != null && mech.TryGetComp<CompMechRepairable>() != null
            && mech.health.hediffSet.hediffs.Any(h => h is Hediff_Injury || h is Hediff_MissingPart);

        private static float RepairFactor(Pawn mech)
        {
            if (mech == null) return 0f;
            float amountToHeal = 1f - DamageAdjustedHealthPercent(mech);
            return Mathf.Clamp(amountToHeal / ReferenceDamageFraction, 0f, MaxRepairFactor);
        }

        private static float DamageAdjustedHealthPercent(Pawn mech)
        {
            float percent = 1f;
            foreach (Hediff h in mech.health.hediffSet.hediffs)
            {
                if (!(h is Hediff_Injury injury) || injury.IsPermanent() || !injury.Visible) continue;
                percent *= 1f - Mathf.Min(injury.Severity / (75f * mech.HealthScale), 0.95f);
            }
            foreach (Hediff_MissingPart missing in mech.health.hediffSet.GetMissingPartsCommonAncestors())
                percent *= 1f - Mathf.Min(missing.Part.def.hitPoints / (75f * mech.HealthScale), 0.95f);
            return Mathf.Clamp(percent, 0.05f, 1f);
        }
    }
}

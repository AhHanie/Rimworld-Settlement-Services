using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Medical
{
    public class UrgentStabilizationServiceWorker : SettlementServiceWorker
    {
        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) return ServiceAvailabilityReport.Available;
            return NeedsStabilization(pawn)
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoUrgentTreatmentNeeded");
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override List<ServiceStockRequirement> GetDynamicStockRequirements(SettlementServiceRequest request)
        {
            var result = new List<ServiceStockRequirement>();
            int amount = RequiredMedicine(request);
            if (amount > 0)
                result.Add(new ServiceStockRequirement { stockCategoryDefName = "SettlementStock_Medicine", preferredThingDefName = "MedicineIndustrial", amount = amount, playerCanSupply = true });
            return result;
        }

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote) =>
            MedicalInputPlanning.PlanFromCategory(request, "SettlementStock_Medicine", RequiredMedicine(request));

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (ctx.CurrentTarget?.liveThing is Pawn pawn)
                Stabilize(pawn, MedicalQualityService.TreatmentQuality(ctx.ResolveSettlement(), def.category));
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static bool IsUrgent(Hediff h) => h.IsCurrentlyLifeThreatening || h.Bleeding;

        private static bool NeedsStabilization(Pawn pawn) =>
            pawn.health?.hediffSet != null && pawn.health.hediffSet.hediffs.Any(IsUrgent);

        private static int RequiredMedicine(SettlementServiceRequest request) =>
            request.target.thing is Pawn pawn && pawn.health?.hediffSet != null
                ? pawn.health.hediffSet.hediffs.Count(IsUrgent)
                : 0;

        private static void Stabilize(Pawn pawn, float quality)
        {
            var treated = new HashSet<Hediff>();
            while (true)
            {
                Hediff worst = pawn.health.hediffSet.hediffs
                    .Where(h => IsUrgent(h) && !treated.Contains(h))
                    .OrderByDescending(h => h.IsCurrentlyLifeThreatening)
                    .ThenByDescending(h => h.BleedRate)
                    .FirstOrDefault();
                if (worst == null) return;

                treated.Add(worst);
                worst.Tended(quality, 1f);
                float ceiling = worst.IsCurrentlyLifeThreatening && worst.def.lethalSeverity > 0f
                    ? worst.def.lethalSeverity * Mathf.Lerp(0.85f, 0.55f, quality)
                    : worst.Severity * Mathf.Lerp(0.85f, 0.4f, quality);
                worst.Severity = Mathf.Min(worst.Severity, ceiling);
            }
        }
    }
}

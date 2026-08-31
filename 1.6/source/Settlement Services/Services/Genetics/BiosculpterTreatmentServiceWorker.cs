using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;
using Settlement_Services.Services.Medical;

namespace Settlement_Services.Services.Genetics
{
    public class BiosculpterTreatmentServiceWorker : SettlementServiceWorker
    {
        private const string CycleGroupKey = "SettlementServices.Label.BiosculpterCycleChoice";
        private const int MedicineAmount = 3;

        private static readonly (string key, string labelKey)[] Cycles =
        {
            ("Medic", "SettlementServices.Label.BiosculpterCycle.Medic"),
            ("Regeneration", "SettlementServices.Label.BiosculpterCycle.Regeneration"),
            ("AgeReversal", "SettlementServices.Label.BiosculpterCycle.AgeReversal"),
        };

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!BiosculpterCycleService.PodDefExists())
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.RequiresIdeology");
            if (!(ctx.SelectedTarget is Pawn pawn)) return ServiceAvailabilityReport.Available;
            return EligibleCycleKeys(pawn).Any()
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoBiosculpterCycleAvailable");
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) yield break;
            foreach (string key in EligibleCycleKeys(pawn))
            {
                string labelKey = Cycles.First(c => c.key == key).labelKey;
                yield return new ServiceDisplayOption { key = key, label = labelKey.Translate(), groupKey = CycleGroupKey };
            }
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote) =>
            MedicalInputPlanning.PlanFromCategory(request, "SettlementStock_Medicine", MedicineAmount);

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            if (!(request.target.thing is Pawn pawn)) return null;
            string key = request.selectedOptionKeys.FirstOrDefault();
            if (key == null) return null;
            return EligibleCycleKeys(pawn).Contains(key) ? null : "SettlementServices.Error.NoBiosculpterCycleAvailable";
        }

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTarget?.liveThing is Pawn pawn)) return ServiceCompletionResult.Ok();

            switch (ctx.Job.selectedOptionKeys.FirstOrDefault())
            {
                case "Medic":
                    BiosculpterCycleService.ResolveCycle<CompBiosculpterPod_MedicCycle>()?.CycleCompleted(pawn);
                    break;
                case "Regeneration":
                    BiosculpterCycleService.ResolveCycle<CompBiosculpterPod_RegenerationCycle>()?.CycleCompleted(pawn);
                    break;
                case "AgeReversal":
                    if (BiosculpterCycleService.CanAgeReverse(pawn))
                        BiosculpterCycleService.ResolveCycle<CompBiosculpterPod_AgeReversalCycle>()?.CycleCompleted(pawn);
                    break;
            }
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static IEnumerable<string> EligibleCycleKeys(Pawn pawn)
        {
            yield return "Medic";
            yield return "Regeneration";
            if (BiosculpterCycleService.CanAgeReverse(pawn)) yield return "AgeReversal";
        }
    }
}

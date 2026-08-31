using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;
using Settlement_Services.Services.Medical;

namespace Settlement_Services.Services.Genetics
{
    public class GeneExtractionServiceWorker : SettlementServiceWorker
    {
        private const int MedicineAmount = 2;
        private const int RegrowDurationDays = 15;

        private static readonly SimpleCurve GeneCountChanceCurve = new SimpleCurve
        {
            new CurvePoint(1f, 0.75f),
            new CurvePoint(2f, 0.25f),
        };

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn pawn)) return ServiceAvailabilityReport.Available;
            return GeneEligibilityService.IsValidExtractionSource(pawn, out string reasonKey)
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable(reasonKey);
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote) =>
            MedicalInputPlanning.PlanFromCategory(request, "SettlementStock_Medicine", MedicineAmount);

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!(ctx.CurrentTarget?.liveThing is Pawn pawn) || !GeneEligibilityService.IsValidExtractionSource(pawn, out _))
                return ServiceCompletionResult.Ok();

            List<GeneDef> extracted = PickGenesToExtract(pawn);
            GeneUtility.ExtractXenogerm(pawn, RegrowDurationDays * 60000);
            if (extracted.Count == 0) return ServiceCompletionResult.Ok();

            var genepack = (Genepack)ThingMaker.MakeThing(ThingDefOf.Genepack);
            genepack.Initialize(extracted);
            return ServiceCompletionResult.Ok(resultThings: new List<Thing> { genepack });
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static List<GeneDef> PickGenesToExtract(Pawn pawn)
        {
            var chosen = new List<GeneDef>();
            int count = Mathf.Min(
                (int)GeneCountChanceCurve.RandomElementByWeight(p => p.y).x,
                pawn.genes.GenesListForReading.Count(g => g.def.biostatArc == 0));

            for (int i = 0; i < count; i++)
            {
                if (!pawn.genes.GenesListForReading.Where(g => Eligible(g, chosen)).TryRandomElementByWeight(g => Weight(g), out Gene picked))
                    break;
                chosen.Add(picked.def);
            }
            return chosen;
        }

        private static bool Eligible(Gene g, List<GeneDef> chosen) =>
            g.def.biostatArc == 0 && g.def.endogeneCategory != EndogeneCategory.Melanin && !chosen.Contains(g.def);

        private static float Weight(Gene g) => g.def.biostatCpx > 0 ? 3f : 1f;
    }
}

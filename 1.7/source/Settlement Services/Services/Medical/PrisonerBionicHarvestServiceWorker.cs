using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Delivery;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Medical
{
    public sealed class PrisonerBionicHarvestServiceWorker : SettlementServiceWorker
    {
        public override bool PercentageModifiersIncludeWorkerCharges => true;

        private static string CheckTarget(Thing target)
        {
            if (!(target is Pawn pawn) || pawn.Destroyed || pawn.Dead) return "SettlementServices.Error.TargetNoLongerExists";
            if (!pawn.RaceProps.Humanlike || !pawn.IsPrisonerOfColony) return "SettlementServices.Error.HarvestPrisonerOnly";
            if (pawn.IsQuestLodger() || pawn.IsSubhuman || pawn.IsShambler || pawn.IsWildMan()) return "SettlementServices.Error.HarvestProtectedTarget";
            return null;
        }

        public override bool ShouldListTarget(Thing target) => target is Pawn pawn && pawn.IsPrisonerOfColony;

        public override string GetTargetBlockReason(Thing target)
        {
            string error = CheckTarget(target);
            if (error == null && BionicHarvestOptionService.Find((Pawn)target).Count == 0) error = "SettlementServices.Error.HarvestNoParts";
            return error?.Translate();
        }

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (ctx.SelectedTarget == null) return ServiceAvailabilityReport.Available;
            string error = CheckTarget(ctx.SelectedTarget);
            if (error == null && BionicHarvestOptionService.Find((Pawn)ctx.SelectedTarget).Count == 0) error = "SettlementServices.Error.HarvestNoParts";
            return error == null ? ServiceAvailabilityReport.Available : ServiceAvailabilityReport.Unavailable(error);
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx) =>
            ctx.SelectedTarget is Pawn pawn ? BionicHarvestOptionService.Display(pawn) : Enumerable.Empty<ServiceDisplayOption>();

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            string error = CheckTarget(request.target?.thing);
            if (error != null) return error;
            Pawn pawn = (Pawn)request.target.thing;
            if (BionicHarvestOptionService.Find(pawn).Count == 0) return "SettlementServices.Error.HarvestNoParts";
            return BionicHarvestOptionService.TryResolve(pawn, request.selectedOptionKeys, out _, out error) ? null : error;
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request)
        {
            if (!(request.target?.thing is Pawn pawn) || !BionicHarvestOptionService.TryResolve(pawn, request.selectedOptionKeys, out List<BionicHarvestOptionService.Option> options, out _))
                return Enumerable.Empty<ServiceLineItem>();
            int charge = Mathf.RoundToInt(options.Sum(o => o.item.BaseMarketValue) * 0.1f);
            return new[] { new ServiceLineItem("SettlementServices.LineItem.BionicHarvestParts", charge) };
        }

        public override object BuildAcceptedData(SettlementServiceRequest request, SettlementServiceQuote quote)
        {
            Pawn pawn = request.target?.thing as Pawn;
            if (!BionicHarvestOptionService.TryResolve(pawn, request.selectedOptionKeys, out List<BionicHarvestOptionService.Option> options, out _)) return null;
            var data = new BionicHarvestAcceptedData();
            int totalCharge = Mathf.RoundToInt(options.Sum(o => o.item.BaseMarketValue) * 0.1f);
            float cumulativeMarketValue = 0f;
            int priorRefund = 0;
            foreach (BionicHarvestOptionService.Option option in options)
            {
                data.keys.Add(option.Key);
                cumulativeMarketValue += option.item.BaseMarketValue;
                int cumulativeCharge = Mathf.RoundToInt(cumulativeMarketValue * 0.1f);
                int cumulative = totalCharge > 0 ? Mathf.RoundToInt(quote.BaseCost * cumulativeCharge / (float)(def.minimumCost + totalCharge)) : 0;
                data.refundShares.Add(Mathf.Max(0, cumulative - priorRefund));
                priorRefund = cumulative;
            }
            return data;
        }

        public override int? BaseDurationTicksFor(SettlementServiceRequest request) =>
            request.selectedOptionKeys == null ? null : def.duration + request.selectedOptionKeys.Count * 2 * GenDate.TicksPerHour;

        public override ServiceConfirmationWarning GetConfirmationWarning(SettlementServiceRequest request)
        {
            if (!(request.target?.thing is Pawn pawn)) return null;
            if (!BionicHarvestOptionService.TryResolve(pawn, request.selectedOptionKeys, out List<BionicHarvestOptionService.Option> options, out _)) return null;
            return new ServiceConfirmationWarning("SettlementServices.Message.BionicHarvestConfirm".Translate(pawn.LabelShortCap, string.Join(", ", options.Select(o => o.Label))), destructive: true);
        }

        public override ServiceStartResult Start(ServiceJobContext ctx)
        {
            string error = CheckTarget(ctx.CurrentTargetThing);
            if (error == null) BionicHarvestOptionService.TryResolve((Pawn)ctx.CurrentTargetThing, ctx.SelectedOptionKeys, out _, out error);
            return error == null ? ServiceStartResult.Ok : ServiceStartResult.Fail(error);
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            string error = CheckTarget(ctx.CurrentTargetThing);
            if (error != null) return ServiceCompletionResult.Fail(error);
            Pawn pawn = (Pawn)ctx.CurrentTargetThing;
            if (!BionicHarvestOptionService.TryResolve(pawn, ctx.SelectedOptionKeys, out List<BionicHarvestOptionService.Option> selected, out error))
                return ServiceCompletionResult.Fail(error);
            BionicHarvestAcceptedData data = ctx.Job.bionicHarvest;
            if (data == null || data.keys.Count != selected.Count || data.refundShares.Count != selected.Count || !data.keys.SequenceEqual(ctx.SelectedOptionKeys))
                return ServiceCompletionResult.Fail("SettlementServices.Error.HarvestStaleSelection");

            var produced = new List<Thing>();
            var completed = new HashSet<string>();
            foreach (BionicHarvestOptionService.Option option in selected.OrderBy(o => o.part == pawn.RaceProps.body.corePart || o.part.depth == BodyPartDepth.Inside ? 1 : 0).ThenBy(o => o.Key))
            {
                if (pawn.Dead) break;
                if (!pawn.health.hediffSet.hediffs.Contains(option.hediff) || pawn.health.hediffSet.PartIsMissing(option.part)) break;
                BionicHarvestCustody.Detach(pawn);
                try
                {
                    pawn.TakeDamage(new DamageInfo(DamageDefOf.SurgicalCut, 99999f, 999f, -1f, null, option.part));
                }
                finally
                {
                    if (!pawn.Dead) BionicHarvestCustody.Restore(pawn);
                }
                pawn.Drawer.renderer.SetAllGraphicsDirty();
                if (pawn.health.hediffSet.hediffs.Contains(option.hediff) || !pawn.health.hediffSet.PartIsMissing(option.part)) break;
                produced.Add(ThingMaker.MakeThing(option.item));
                completed.Add(option.Key);
            }

            if (produced.Count == 0) return ServiceCompletionResult.Fail("SettlementServices.Error.HarvestRemovalFailed");
            if (pawn.Dead && pawn.Corpse != null && !pawn.Corpse.Destroyed) produced.Add(pawn.Corpse);
            int refund = 0;
            for (int i = 0; i < data.keys.Count; i++)
                if (!completed.Contains(data.keys[i])) refund += data.refundShares[i];
            if (completed.Count < selected.Count)
                Messages.Message("SettlementServices.Message.BionicHarvestPartial".Translate(pawn.LabelShortCap), MessageTypeDefOf.NeutralEvent);
            return ServiceCompletionResult.Ok(requiresCollection: true, resultThings: produced).WithRefund(refund);
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        public override ServicePodManifestEstimate EstimatePodManifest(SettlementServiceRequest unit, ServicePodManifestContext context)
        {
            ServicePodManifestEstimate estimate = ServicePodManifestEstimate.ForTargets(unit, context);
            if (estimate == null || !(unit.target?.thing is Pawn pawn)) return null;
            if (!BionicHarvestOptionService.TryResolve(pawn, unit.selectedOptionKeys, out List<BionicHarvestOptionService.Option> options, out _)) return null;
            if (estimate.unsplittableMasses.Count > 0 && pawn.RaceProps.corpseDef != null)
            {
                float pawnMass = pawn.GetStatValue(StatDefOf.Mass);
                float corpseMass = pawn.RaceProps.corpseDef.GetStatValueAbstract(StatDefOf.Mass);
                estimate.unsplittableMasses[0] += Mathf.Max(0f, corpseMass - pawnMass);
            }
            foreach (BionicHarvestOptionService.Option option in options)
                estimate.unsplittableMasses.Add(option.item.GetStatValueAbstract(StatDefOf.Mass));
            return estimate;
        }
    }
}

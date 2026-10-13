using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Pricing;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Mechanitor
{
    public class MechanoidHiringServiceWorker : SettlementServiceWorker
    {
        private const string MechGroupKey = "SettlementServices.Label.MechanoidChoice";
        private const float WealthFeeFraction = 0.00005f;

        public override bool RequiresTargetCustody => false;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (ctx.Settlement == null) return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.SettlementNoLongerExists");

            if (ctx.RequestingCaravan == null)
            {
                return MechanoidHiringStock.AnyAvailable(ctx.Settlement)
                    ? ServiceAvailabilityReport.Available
                    : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoMechsInStock");
            }

            if (!HasLivingMechanitor(ctx.RequestingCaravan))
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoCaravanMechanitor");

            if (ctx.SelectedOptionKeys.Count > 0)
            {
                if (!TryResolveOfferedEntry(ctx.Settlement, ctx.SelectedOptionKeys, out MechanoidHiringEntry entry, out string entryErrorKey))
                    return ServiceAvailabilityReport.Unavailable(entryErrorKey);

                if (!AnyMechanitorHasBandwidth(ctx.RequestingCaravan, entry))
                    return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.InsufficientMechBandwidth");
            }

            return ServiceAvailabilityReport.Available;
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            if (ctx.Settlement == null) yield break;

            foreach (MechanoidHiringEntry entry in MechanoidHiringCatalog.AllEntries)
            {
                int stock = MechanoidHiringStock.Available(ctx.Settlement, entry);
                if (stock <= 0) continue;

                yield return new ServiceDisplayOption
                {
                    key = entry.optionKey,
                    label = entry.raceDef.LabelCap,
                    description = "SettlementServices.Label.MechanoidHiringOptionDescription".Translate(
                        Mathf.RoundToInt(entry.bandwidthCost), stock, MechanoidHiringStock.Capacity, Mathf.RoundToInt(entry.baseMarketValue)),
                    groupKey = MechGroupKey,
                    allowMultipleSelectionInGroup = false,
                };
            }
        }

        public override string ValidateUnitRequest(SettlementServiceRequest request) =>
            TryResolveOfferedEntry(request.settlement, request.selectedOptionKeys, out _, out string errorKey) ? null : errorKey;

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request)
        {
            if (!TryResolveOfferedEntry(request.settlement, request.selectedOptionKeys, out MechanoidHiringEntry entry, out _))
                return new List<ServiceLineItem>();

            int wealthFee = Mathf.RoundToInt(ServicePricingContext.Current.TotalPlayerWealth * WealthFeeFraction);
            int marketValue = Mathf.RoundToInt(entry.baseMarketValue);

            return new List<ServiceLineItem>
            {
                new ServiceLineItem("SettlementServices.LineItem.MechHireWealthFee", wealthFee),
                new ServiceLineItem("SettlementServices.LineItem.MechHireMarketValue", marketValue, false, entry.raceDef.LabelCap),
            };
        }

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote)
        {
            if (!TryResolveOfferedEntry(request.settlement, request.selectedOptionKeys, out MechanoidHiringEntry entry, out _))
                return ServiceInputPlan.None;

            return new ServiceInputPlan
            {
                specialStockReservations = new List<ServiceStockReservation>
                {
                    new ServiceStockReservation(MechanoidHiringStock.StockKeyFor(entry), 1),
                },
            };
        }

        public override ServiceStartResult Start(ServiceJobContext ctx)
        {
            Settlement settlement = ctx.ResolveSettlement();
            if (settlement == null) return ServiceStartResult.Fail("SettlementServices.Error.SettlementNoLongerExists");

            if (!TryResolveSelectedEntryKey(ctx.Job.selectedOptionKeys, out MechanoidHiringEntry entry, out string entryErrorKey))
                return ServiceStartResult.Fail(entryErrorKey);

            Caravan caravan = ResolveRequesterCaravan(ctx.Job);
            if (caravan == null) return ServiceStartResult.Fail("SettlementServices.Error.NoCaravanMechanitor");

            if (!TryFindBandwidthCapableMechanitor(caravan, entry, out _))
                return ServiceStartResult.Fail("SettlementServices.Error.InsufficientMechBandwidth");

            return ServiceStartResult.Ok;
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            Settlement settlement = ctx.ResolveSettlement();
            if (settlement == null) return ServiceCompletionResult.Fail("SettlementServices.Error.SettlementNoLongerExists");

            if (!TryResolveSelectedEntryKey(ctx.Job.selectedOptionKeys, out MechanoidHiringEntry entry, out string entryErrorKey))
                return ServiceCompletionResult.Fail(entryErrorKey);

            Caravan caravan = ResolveRequesterCaravan(ctx.Job);
            if (caravan == null) return ServiceCompletionResult.Fail("SettlementServices.Error.NoCaravanMechanitor");

            if (!TryFindBandwidthCapableMechanitor(caravan, entry, out Pawn mechanitor))
                return ServiceCompletionResult.Fail("SettlementServices.Error.InsufficientMechBandwidth");

            Pawn mech = GenerateMech(entry);
            if (mech.OverseerSubject == null)
            {
                mech.Destroy();
                return ServiceCompletionResult.Fail("SettlementServices.Error.MechContentUnavailable");
            }

            mechanitor.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
            mechanitor.mechanitor.AssignPawnControlGroup(mech, MechWorkModeDefOf.Escort);

            caravan.AddPawn(mech, true);
            Find.WorldPawns.PassToWorld(mech);

            AnnounceHired(mech, mechanitor);
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static Pawn GenerateMech(MechanoidHiringEntry entry)
        {
            var request = new PawnGenerationRequest(
                entry.kindDef,
                Faction.OfPlayer,
                PawnGenerationContext.NonPlayer,
                developmentalStages: DevelopmentalStage.Newborn);
            return PawnGenerator.GeneratePawn(request);
        }

        private static void AnnounceHired(Pawn mech, Pawn mechanitor) =>
            Messages.Message("SettlementServices.Message.MechanoidHired".Translate(mech.LabelShortCap, mechanitor.LabelShortCap), mech, MessageTypeDefOf.PositiveEvent);

        private static bool TryResolveSelectedEntryKey(IReadOnlyList<string> selectedOptionKeys, out MechanoidHiringEntry entry, out string errorKey)
        {
            entry = null;
            errorKey = null;

            var mechKeys = new List<string>();
            foreach (string key in selectedOptionKeys)
            {
                if (key == null || !key.StartsWith(MechanoidHiringEntry.OptionKeyPrefix)) continue;
                if (mechKeys.Contains(key)) { errorKey = "SettlementServices.Error.NoMechSelected"; return false; }
                mechKeys.Add(key);
            }

            if (mechKeys.Count != 1) { errorKey = "SettlementServices.Error.NoMechSelected"; return false; }

            entry = MechanoidHiringCatalog.ResolveOption(mechKeys[0]);
            if (entry == null) { errorKey = "SettlementServices.Error.MechContentUnavailable"; return false; }

            return true;
        }

        private static bool TryResolveOfferedEntry(Settlement settlement, IReadOnlyList<string> selectedOptionKeys, out MechanoidHiringEntry entry, out string errorKey)
        {
            if (!TryResolveSelectedEntryKey(selectedOptionKeys, out entry, out errorKey)) return false;

            if (settlement == null || MechanoidHiringStock.Available(settlement, entry) <= 0)
            {
                entry = null;
                errorKey = "SettlementServices.Error.MechNoLongerAvailable";
                return false;
            }

            return true;
        }

        private static bool HasLivingMechanitor(Caravan caravan) => LivingMechanitors(caravan).Any();

        private static bool AnyMechanitorHasBandwidth(Caravan caravan, MechanoidHiringEntry entry) =>
            LivingMechanitors(caravan).Any(p => FreeBandwidth(p) >= entry.bandwidthCost);

        private static bool TryFindBandwidthCapableMechanitor(Caravan caravan, MechanoidHiringEntry entry, out Pawn mechanitor)
        {
            mechanitor = LivingMechanitors(caravan).FirstOrDefault(p => FreeBandwidth(p) >= entry.bandwidthCost);
            return mechanitor != null;
        }

        private static IEnumerable<Pawn> LivingMechanitors(Caravan caravan) =>
            caravan.PawnsListForReading.Where(p => !p.Dead && MechanitorUtility.IsMechanitor(p)).OrderBy(p => p.thingIDNumber);

        private static int FreeBandwidth(Pawn mechanitor) => mechanitor.mechanitor.TotalBandwidth - mechanitor.mechanitor.UsedBandwidth;

        private static Caravan ResolveRequesterCaravan(ServiceJobRecord job) =>
            job.requesterCaravanId < 0 ? null : Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == job.requesterCaravanId);
    }
}

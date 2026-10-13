using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Specialty;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Animals
{
    public class AnimalVendorServiceWorker : SettlementServiceWorker
    {
        public const string RequiredSpecialtyDefName = "SettlementSpecialty_AnimalCenter";

        private const string OfferGroupKey = "SettlementServices.Label.AnimalVendorChoice";
        private const string OfferKeyPrefix = "AnimalOffer:";
        private const float RetailMarkupFactor = 1.4f;

        public override bool RequiresTargetCustody => false;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            Settlement settlement = ctx.Settlement;
            if (settlement?.Faction == null) return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.SettlementNoLongerExists");
            if (!HasRequiredSpecialty(settlement)) return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.SpecialtyRequired");
            if (ctx.RequestingCaravan != null && !IsValidCaravan(ctx.RequestingCaravan, settlement))
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoRequesterCaravan");
            if (Offers(settlement).Count == 0) return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.AnimalVendorSoldOut");

            if (ctx.SelectedOptionKeys.Count > 0 && !TryResolveSelectedOffers(settlement, ctx.SelectedOptionKeys, out _, out string errorKey))
                return ServiceAvailabilityReport.Unavailable(errorKey);

            return ServiceAvailabilityReport.Available;
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            if (ctx.Settlement?.Faction == null || !HasRequiredSpecialty(ctx.Settlement)) yield break;

            foreach (AnimalVendorOfferRecord offer in Offers(ctx.Settlement))
            {
                yield return new ServiceDisplayOption
                {
                    key = OfferKeyPrefix + offer.offerId,
                    label = offer.pawn.LabelShortCap,
                    description = Describe(offer.pawn),
                    groupKey = OfferGroupKey,
                    allowMultipleSelectionInGroup = true,
                    pawnPreview = offer.pawn,
                };
            }
        }

        public override IEnumerable<string> GetDisplaySummaryLines(SettlementServiceContext ctx)
        {
            if (ctx.Settlement?.Faction == null || !HasRequiredSpecialty(ctx.Settlement)) yield break;

            Offers(ctx.Settlement);
            if (ctx.Domain.TryGetAnimalVendorRefreshTick(ctx.Settlement.ID, out int nextRefreshTick))
            {
                int remaining = Mathf.Max(0, nextRefreshTick - Find.TickManager.TicksGame);
                yield return "SettlementServices.Label.AnimalVendorRefresh".Translate(remaining.ToStringTicksToPeriod());
            }
        }

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            Settlement settlement = request.settlement;
            if (settlement?.Faction == null) return "SettlementServices.Error.SettlementNoLongerExists";
            if (!HasRequiredSpecialty(settlement)) return "SettlementServices.Error.SpecialtyRequired";
            if (!IsValidCaravan(request.negotiator?.GetCaravan(), settlement)) return "SettlementServices.Error.NoRequesterCaravan";

            return TryResolveSelectedOffers(settlement, request.selectedOptionKeys, out _, out string errorKey) ? null : errorKey;
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request)
        {
            var items = new List<ServiceLineItem>();
            if (request.settlement == null) return items;
            if (!TryResolveSelectedOffers(request.settlement, request.selectedOptionKeys, out List<AnimalVendorOfferRecord> offers, out _)) return items;

            foreach (AnimalVendorOfferRecord offer in offers)
                items.Add(new ServiceLineItem("SettlementServices.LineItem.AnimalVendorPurchase", PriceOf(offer.pawn), false, offer.pawn.LabelShortCap));
            return items;
        }

        public override ServiceStartResult Start(ServiceJobContext ctx)
        {
            if (!TryResolveJobSale(ctx, out _, out _, out _, out string errorKey)) return ServiceStartResult.Fail(errorKey);
            return ServiceStartResult.Ok;
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!TryResolveJobSale(ctx, out Settlement settlement, out List<AnimalVendorOfferRecord> offers, out Caravan caravan, out string errorKey))
                return ServiceCompletionResult.Fail(errorKey);

            Pawn negotiator = BestCaravanPawnUtility.FindBestNegotiator(caravan);
            if (negotiator == null) return ServiceCompletionResult.Fail("SettlementServices.Error.AnimalVendorNoNegotiator");

            var handedOver = new List<(AnimalVendorOfferRecord offer, Faction originalFaction)>();
            foreach (AnimalVendorOfferRecord offer in offers)
            {
                bool delivered = false;
                if (ctx.Domain.TryClaimAnimalVendorOffer(settlement.ID, offer.offerId, out AnimalVendorOfferRecord claimed))
                {
                    handedOver.Add((claimed, claimed.pawn.Faction));

                    claimed.pawn.PreTraded(TradeAction.PlayerBuys, negotiator, settlement);
                    caravan.AddPawn(claimed.pawn, addCarriedPawnToWorldPawnsIfAny: true);
                    delivered = caravan.ContainsPawn(claimed.pawn);
                }

                if (delivered) continue;

                foreach ((AnimalVendorOfferRecord rolledBack, Faction faction) in handedOver)
                {
                    caravan.RemovePawn(rolledBack.pawn);
                    if (rolledBack.pawn.Faction != faction) rolledBack.pawn.SetFaction(faction);
                    ctx.Domain.RestoreAnimalVendorOffer(settlement.ID, rolledBack);
                }
                return ServiceCompletionResult.Fail(handedOver.Count == 0
                    ? "SettlementServices.Error.AnimalVendorOfferNoLongerAvailable"
                    : "SettlementServices.Error.AnimalVendorHandoffFailed");
            }

            foreach ((AnimalVendorOfferRecord sold, Faction _) in handedOver) Find.WorldPawns.PassToWorld(sold.pawn);

            if (handedOver.Count == 1)
                Messages.Message("SettlementServices.Message.AnimalVendorPurchased".Translate(handedOver[0].offer.pawn.LabelShortCap), handedOver[0].offer.pawn, MessageTypeDefOf.PositiveEvent);
            else
                Messages.Message("SettlementServices.Message.AnimalVendorPurchasedMany".Translate(handedOver.Count), MessageTypeDefOf.PositiveEvent);
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static bool TryResolveJobSale(ServiceJobContext ctx, out Settlement settlement, out List<AnimalVendorOfferRecord> offers, out Caravan caravan, out string errorKey)
        {
            offers = null;
            caravan = null;

            settlement = ctx.ResolveSettlement();
            if (settlement?.Faction == null) { errorKey = "SettlementServices.Error.SettlementNoLongerExists"; return false; }
            if (!HasRequiredSpecialty(settlement)) { errorKey = "SettlementServices.Error.SpecialtyRequired"; return false; }

            caravan = ctx.Job.requesterCaravanId < 0 ? null : Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == ctx.Job.requesterCaravanId);
            if (!IsValidCaravan(caravan, settlement)) { errorKey = "SettlementServices.Error.NoRequesterCaravan"; return false; }

            return TryResolveSelectedOffers(settlement, ctx.Job.selectedOptionKeys, out offers, out errorKey);
        }

        private static bool TryResolveSelectedOffers(Settlement settlement, IReadOnlyList<string> selectedOptionKeys, out List<AnimalVendorOfferRecord> offers, out string errorKey)
        {
            offers = null;

            var selectedKeys = selectedOptionKeys.Where(k => k != null && k.StartsWith(OfferKeyPrefix)).ToList();
            if (selectedKeys.Count == 0) { errorKey = "SettlementServices.Error.AnimalVendorNoneSelected"; return false; }
            if (selectedKeys.Distinct().Count() != selectedKeys.Count) { errorKey = "SettlementServices.Error.AnimalVendorDuplicateSelection"; return false; }

            IReadOnlyList<AnimalVendorOfferRecord> current = Offers(settlement);
            var resolved = new List<AnimalVendorOfferRecord>();
            foreach (string key in selectedKeys)
            {
                AnimalVendorOfferRecord offer = int.TryParse(key.Substring(OfferKeyPrefix.Length), out int offerId)
                    ? current.FirstOrDefault(o => o.offerId == offerId)
                    : null;
                if (offer == null) { errorKey = "SettlementServices.Error.AnimalVendorOfferNoLongerAvailable"; return false; }
                resolved.Add(offer);
            }

            offers = resolved;
            errorKey = null;
            return true;
        }

        private static IReadOnlyList<AnimalVendorOfferRecord> Offers(Settlement settlement) =>
            SettlementServicesWorldComponent.Current.GetOrRefreshAnimalVendorOffers(settlement, () => AnimalVendorOfferGenerator.Generate(settlement));

        private static bool HasRequiredSpecialty(Settlement settlement) =>
            SettlementSpecialtyService.GetSpecialties(settlement).Any(s => s.defName == RequiredSpecialtyDefName);

        private static bool IsValidCaravan(Caravan caravan, Settlement settlement) =>
            caravan != null && caravan.Faction == Faction.OfPlayer && caravan.Tile == settlement.Tile;

        private static int PriceOf(Pawn pawn) => Mathf.Max(1, Mathf.RoundToInt(pawn.MarketValue * RetailMarkupFactor));

        private static string Describe(Pawn pawn) =>
            "SettlementServices.Label.AnimalVendorOfferDescription".Translate(
                pawn.gender.GetLabel(animal: true), pawn.def.LabelCap, pawn.ageTracker.AgeBiologicalYears, PriceOf(pawn));
    }
}

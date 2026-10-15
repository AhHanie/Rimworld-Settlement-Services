using System;
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

namespace Settlement_Services.Framework.Compat.VehicleFramework
{
    public class VehicleShopServiceWorker : SettlementServiceWorker
    {
        public const string RequiredSpecialtyDefName = "SettlementSpecialty_VehicleGarage";

        private const string OfferGroupKey = "SettlementServices.Label.VehicleShopChoice";
        private const string OfferKeyPrefix = "VehicleOffer:";

        public override bool RequiresTargetCustody => false;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            Settlement settlement = ctx.Settlement;
            if (settlement?.Faction == null) return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.SettlementNoLongerExists");
            if (!HasRequiredSpecialty(settlement)) return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.SpecialtyRequired");
            if (!VehicleShopAdapter.Available) return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.VehicleShopUnavailable");
            if (ctx.RequestingCaravan != null && !IsValidCaravan(ctx.RequestingCaravan, settlement))
                return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoRequesterCaravan");

            if (Offers(settlement).Count == 0)
            {
                if (!ctx.Domain.TryGetVehicleShopRefreshTick(settlement.ID, out _)) return ServiceAvailabilityReport.Unavailable("SettlementServices.Error.VehicleShopUnavailable");
                return ServiceAvailabilityReport.Unavailable(VehicleShopCatalog.EligibleDefs().Count == 0
                    ? "SettlementServices.Error.VehicleShopNoVehicles"
                    : "SettlementServices.Error.VehicleShopSoldOut");
            }

            if (ctx.SelectedOptionKeys.Count > 0)
            {
                if (!TryResolveSelectedOffer(settlement, ctx.SelectedOptionKeys, out _, out ThingDef vehicleDef, out string errorKey))
                    return ServiceAvailabilityReport.Unavailable(errorKey);

                string navigationErrorKey = ctx.RequestingCaravan != null ? ValidateFleetNavigation(vehicleDef, ctx.RequestingCaravan, settlement) : null;
                if (navigationErrorKey != null) return ServiceAvailabilityReport.Unavailable(navigationErrorKey);
            }

            return ServiceAvailabilityReport.Available;
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            if (ctx.Settlement?.Faction == null || !HasRequiredSpecialty(ctx.Settlement) || !VehicleShopAdapter.Available) yield break;

            foreach (VehicleShopOfferRecord offer in Offers(ctx.Settlement))
            {
                ThingDef vehicleDef = DefDatabase<ThingDef>.GetNamedSilentFail(offer.vehicleDefName);
                if (vehicleDef == null) continue;

                string typeLabel = TypeLabel(vehicleDef);
                yield return new ServiceDisplayOption
                {
                    key = OfferKeyPrefix + offer.offerId,
                    label = "SettlementServices.Label.VehicleShopOfferLabel".Translate(vehicleDef.LabelCap, typeLabel, offer.tier, offer.listedPrice),
                    description = "SettlementServices.Label.VehicleShopOfferDescription".Translate(vehicleDef.LabelCap, typeLabel, offer.tier, offer.listedPrice),
                    groupKey = OfferGroupKey,
                    allowMultipleSelectionInGroup = false,
                };
            }
        }

        public override IEnumerable<string> GetDisplaySummaryLines(SettlementServiceContext ctx)
        {
            if (ctx.Settlement?.Faction == null || !HasRequiredSpecialty(ctx.Settlement) || !VehicleShopAdapter.Available) yield break;

            Offers(ctx.Settlement);
            if (ctx.Domain.TryGetVehicleShopRefreshTick(ctx.Settlement.ID, out int nextRefreshTick))
            {
                int remaining = Mathf.Max(0, nextRefreshTick - Find.TickManager.TicksGame);
                yield return "SettlementServices.Label.VehicleShopRefresh".Translate(remaining.ToStringTicksToPeriod());
            }
        }

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            Settlement settlement = request.settlement;
            if (settlement?.Faction == null) return "SettlementServices.Error.SettlementNoLongerExists";
            if (!HasRequiredSpecialty(settlement)) return "SettlementServices.Error.SpecialtyRequired";
            if (!VehicleShopAdapter.Available) return "SettlementServices.Error.VehicleShopUnavailable";

            Caravan caravan = request.negotiator?.GetCaravan();
            if (!IsValidCaravan(caravan, settlement)) return "SettlementServices.Error.NoRequesterCaravan";

            if (!TryResolveSelectedOffer(settlement, request.selectedOptionKeys, out _, out ThingDef vehicleDef, out string errorKey)) return errorKey;
            return ValidateFleetNavigation(vehicleDef, caravan, settlement);
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request)
        {
            var items = new List<ServiceLineItem>();
            if (request.settlement == null || !VehicleShopAdapter.Available) return items;
            if (!TryResolveSelectedOffer(request.settlement, request.selectedOptionKeys, out VehicleShopOfferRecord offer, out ThingDef vehicleDef, out _)) return items;

            items.Add(new ServiceLineItem("SettlementServices.LineItem.VehicleShopPurchase", offer.listedPrice, false, vehicleDef.LabelCap));
            return items;
        }

        public override ServiceStartResult Start(ServiceJobContext ctx)
        {
            if (!TryResolveJobSale(ctx, out _, out _, out _, out _, out string errorKey)) return ServiceStartResult.Fail(errorKey);
            return ServiceStartResult.Ok;
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (!TryResolveJobSale(ctx, out Settlement settlement, out VehicleShopOfferRecord offer, out ThingDef vehicleDef, out Caravan caravan, out string errorKey))
                return ServiceCompletionResult.Fail(errorKey);

            string navigationErrorKey = ValidateFleetNavigation(vehicleDef, caravan, settlement);
            if (navigationErrorKey != null) return ServiceCompletionResult.Fail(navigationErrorKey);

            if (!ctx.Domain.TryClaimVehicleShopOffer(settlement.ID, offer.offerId, out VehicleShopOfferRecord claimed))
                return ServiceCompletionResult.Fail("SettlementServices.Error.VehicleShopOfferNoLongerAvailable");

            Pawn vehicle = VehicleShopAdapter.GenerateVehicle(vehicleDef);
            if (vehicle == null)
            {
                ctx.Domain.RestoreVehicleShopOffer(settlement.ID, claimed);
                return ServiceCompletionResult.Fail("SettlementServices.Error.VehicleShopGenerationFailed");
            }

            List<Pawn> caravanPawnsBefore = caravan.PawnsListForReading.ToList();
            try
            {
                caravan.AddPawn(vehicle, addCarriedPawnToWorldPawnsIfAny: true);
            }
            catch (Exception ex)
            {
                SupportLog.Error($"Adding vehicle '{vehicleDef.defName}' to the caravan threw during a vehicle shop purchase: {ex}");
            }

            Caravan current = FindCaravanContaining(vehicle);
            if (current == null || current.Faction != Faction.OfPlayer || current.Tile != settlement.Tile)
            {
                RollBackHandoff(ctx, settlement, claimed, vehicle, caravan, caravanPawnsBefore);
                return ServiceCompletionResult.Fail("SettlementServices.Error.VehicleShopHandoffFailed");
            }

            if (!Find.WorldPawns.Contains(vehicle)) Find.WorldPawns.PassToWorld(vehicle);
            ctx.Domain.UpdateJobRequesterCaravan(ctx.Job.jobId, current);

            Messages.Message("SettlementServices.Message.VehicleShopPurchased".Translate(vehicle.LabelShortCap), vehicle, MessageTypeDefOf.PositiveEvent);
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static void RollBackHandoff(ServiceJobContext ctx, Settlement settlement, VehicleShopOfferRecord claimed, Pawn vehicle, Caravan originalCaravan, List<Pawn> caravanPawnsBefore)
        {
            try
            {
                FindCaravanContaining(vehicle)?.RemovePawn(vehicle);
                if (!vehicle.Destroyed) vehicle.Destroy(DestroyMode.Vanish);
            }
            catch (Exception ex)
            {
                SupportLog.Error($"Rolling back a failed vehicle shop handoff for '{claimed.vehicleDefName}' threw: {ex}");
            }

            ctx.Domain.RestoreVehicleShopOffer(settlement.ID, claimed);

            Caravan survivor = originalCaravan != null && !originalCaravan.Destroyed && Find.WorldObjects.Caravans.Contains(originalCaravan)
                ? originalCaravan
                : caravanPawnsBefore.Select(p => p.GetCaravan()).FirstOrDefault(c => c != null);
            if (survivor != null) ctx.Domain.UpdateJobRequesterCaravan(ctx.Job.jobId, survivor);
        }

        private static Caravan FindCaravanContaining(Pawn vehicle)
        {
            Caravan holder = vehicle.GetCaravan();
            if (holder != null) return holder;
            return Find.WorldObjects.Caravans.FirstOrDefault(c => c.ContainsPawn(vehicle));
        }

        private static bool TryResolveJobSale(ServiceJobContext ctx, out Settlement settlement, out VehicleShopOfferRecord offer, out ThingDef vehicleDef, out Caravan caravan, out string errorKey)
        {
            offer = null;
            vehicleDef = null;
            caravan = null;

            settlement = ctx.ResolveSettlement();
            if (settlement?.Faction == null) { errorKey = "SettlementServices.Error.SettlementNoLongerExists"; return false; }
            if (!HasRequiredSpecialty(settlement)) { errorKey = "SettlementServices.Error.SpecialtyRequired"; return false; }
            if (!VehicleShopAdapter.Available) { errorKey = "SettlementServices.Error.VehicleShopUnavailable"; return false; }

            caravan = ctx.Job.requesterCaravanId < 0 ? null : Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == ctx.Job.requesterCaravanId);
            if (!IsValidCaravan(caravan, settlement)) { errorKey = "SettlementServices.Error.NoRequesterCaravan"; return false; }

            return TryResolveSelectedOffer(settlement, ctx.Job.selectedOptionKeys, out offer, out vehicleDef, out errorKey);
        }

        private static bool TryResolveSelectedOffer(Settlement settlement, IReadOnlyList<string> selectedOptionKeys, out VehicleShopOfferRecord offer, out ThingDef vehicleDef, out string errorKey)
        {
            offer = null;
            vehicleDef = null;

            List<string> selectedKeys = selectedOptionKeys.Where(k => k != null && k.StartsWith(OfferKeyPrefix, StringComparison.Ordinal)).ToList();
            if (selectedKeys.Count == 0) { errorKey = "SettlementServices.Error.VehicleShopNoneSelected"; return false; }
            if (selectedKeys.Count > 1) { errorKey = "SettlementServices.Error.VehicleShopSingleOfferOnly"; return false; }

            if (!int.TryParse(selectedKeys[0].Substring(OfferKeyPrefix.Length), out int offerId) || offerId <= 0)
            {
                errorKey = "SettlementServices.Error.VehicleShopSingleOfferOnly";
                return false;
            }

            VehicleShopOfferRecord found = Offers(settlement).FirstOrDefault(o => o.offerId == offerId);
            ThingDef foundDef = found == null ? null : DefDatabase<ThingDef>.GetNamedSilentFail(found.vehicleDefName);
            if (foundDef == null || !VehicleShopAdapter.IsPlayerPurchasable(foundDef))
            {
                errorKey = "SettlementServices.Error.VehicleShopOfferNoLongerAvailable";
                return false;
            }

            offer = found;
            vehicleDef = foundDef;
            errorKey = null;
            return true;
        }

        private static string ValidateFleetNavigation(ThingDef vehicleDef, Caravan caravan, Settlement settlement)
        {
            switch (VehicleShopAdapter.CheckNavigation(vehicleDef, VehicleShopAdapter.VehicleDefsIn(caravan), settlement.Tile))
            {
                case VehicleShopNavigation.Ok: return null;
                case VehicleShopNavigation.Blocked: return "SettlementServices.Error.VehicleShopCannotTravel";
                default: return "SettlementServices.Error.VehicleShopUnavailable";
            }
        }

        private static IReadOnlyList<VehicleShopOfferRecord> Offers(Settlement settlement) =>
            SettlementServicesWorldComponent.Current.GetOrRefreshVehicleShopOffers(settlement, seed => VehicleShopOfferGenerator.Generate(settlement, seed));

        private static bool HasRequiredSpecialty(Settlement settlement) =>
            SettlementSpecialtyService.GetSpecialties(settlement).Any(s => s.defName == RequiredSpecialtyDefName);

        private static bool IsValidCaravan(Caravan caravan, Settlement settlement) =>
            caravan != null && caravan.Faction == Faction.OfPlayer && caravan.Tile == settlement.Tile;

        private static string TypeLabel(ThingDef vehicleDef)
        {
            string typeName = VehicleShopAdapter.VehicleTypeName(vehicleDef);
            if (typeName.NullOrEmpty()) return "SettlementServices.Label.VehicleShopTypeUnknown".Translate();
            return ("SettlementServices.Label.VehicleShopType" + typeName).TryTranslate(out TaggedString label) ? label.ToString() : typeName;
        }
    }
}

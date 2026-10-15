using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.Framework.Payment
{
    public static class ServiceBarterGoodsSource
    {
        public static bool TryResolveAccess(SettlementServiceRequest request, out Settlement settlement, out Caravan caravan, out Pawn negotiator, out string errorKey)
        {
            settlement = request?.settlement;
            caravan = null;
            negotiator = null;

            if (request == null || request.channel != RequestChannel.InPerson || settlement == null)
            {
                errorKey = "SettlementServices.Error.BarterGoodsInPersonOnly";
                return false;
            }

            caravan = request.negotiator?.GetCaravan();
            if (caravan == null || caravan.Destroyed || caravan.Faction != Faction.OfPlayer)
            {
                errorKey = "SettlementServices.Error.CaravanNoLongerExists";
                return false;
            }

            if (settlement.Destroyed || settlement.Faction == null || settlement.trader == null || settlement.TraderKind == null
                || CaravanVisitUtility.SettlementVisitedNow(caravan) != settlement
                || !(bool)CaravanArrivalAction_Trade.CanTradeWith(caravan, settlement))
            {
                errorKey = "SettlementServices.Error.BarterGoodsNoTradeAccess";
                return false;
            }

            negotiator = BestCaravanPawnUtility.FindBestNegotiator(caravan, settlement.Faction, settlement.TraderKind);
            if (negotiator == null)
            {
                errorKey = "SettlementServices.Error.BarterGoodsNoTradeAccess";
                return false;
            }

            errorKey = null;
            return true;
        }

        public static List<Thing> StockItems(Settlement settlement)
        {
            var items = new List<Thing>();
            IEnumerable<Thing> goods = settlement?.Goods;
            if (goods == null) return items;

            foreach (Thing thing in goods.ToList())
                if (IsBuyable(settlement, thing)) items.Add(thing);

            return items;
        }

        public static ThingOwner StockOwner(Settlement settlement) => settlement?.trader?.GetDirectlyHeldThings();

        public static bool IsInStock(Settlement settlement, Thing thing)
        {
            ThingOwner owner = StockOwner(settlement);
            return owner != null && thing != null && !thing.Destroyed && thing.holdingOwner == owner;
        }

        public static bool IsBuyable(Settlement settlement, Thing thing)
        {
            if (thing == null || thing.Destroyed || thing.stackCount <= 0) return false;

            TraderKindDef traderKind = settlement?.TraderKind;
            if (traderKind == null || !thing.def.tradeability.TraderCanSell() || !traderKind.WillTrade(thing.def)) return false;

            if (thing is Pawn pawn && (pawn.Dead || !settlement.trader.ContainsPawn(pawn))) return false;

            return ServiceBarterValuation.TryUnitValue(thing, out decimal unitValue) && unitValue > 0m;
        }

        public static bool TryValidate(ServicePaymentSelection payment, SettlementServiceRequest request, out decimal total, out string errorKey)
        {
            total = 0m;
            errorKey = null;
            if (payment == null || !payment.HasGoods) return true;

            if (!TryResolveAccess(request, out Settlement settlement, out _, out _, out errorKey)) return false;

            var seen = new HashSet<Thing>();
            foreach (ServicePaymentEntry entry in payment.goods)
            {
                if (entry?.thing == null || entry.count <= 0 || !seen.Add(entry.thing) || (entry.thing is Pawn && entry.count != 1))
                {
                    total = 0m;
                    errorKey = "SettlementServices.Error.BarterInvalidSelection";
                    return false;
                }

                if (!IsInStock(settlement, entry.thing) || !IsBuyable(settlement, entry.thing) || entry.count > entry.thing.stackCount)
                {
                    total = 0m;
                    errorKey = "SettlementServices.Error.BarterGoodsStale";
                    return false;
                }

                if (!ServiceBarterValuation.TryUnitValue(entry.thing, out decimal unitValue))
                {
                    total = 0m;
                    errorKey = "SettlementServices.Error.BarterInvalidValue";
                    return false;
                }

                if (entry.expectedUnitValue.HasValue && entry.expectedUnitValue.Value != unitValue)
                {
                    total = 0m;
                    errorKey = "SettlementServices.Error.BarterGoodsStale";
                    return false;
                }

                total += unitValue * entry.count;
            }

            return true;
        }

        public static bool TryPreflightDelivery(ServicePaymentSelection payment, Caravan caravan, Pawn negotiator, out string errorKey)
        {
            errorKey = null;
            if (payment == null || !payment.HasGoods) return true;

            List<Pawn> receivers = caravan.PawnsListForReading;
            foreach (ServicePaymentEntry entry in payment.goods)
            {
                if (entry.thing is Pawn pawn)
                {
                    if (pawn.Dead || caravan.ContainsPawn(pawn))
                    {
                        errorKey = "SettlementServices.Error.BarterGoodsStale";
                        return false;
                    }

                    continue;
                }

                if (CaravanInventoryUtility.FindPawnToMoveInventoryTo(entry.thing, receivers, null) == null)
                {
                    errorKey = "SettlementServices.Error.BarterGoodsDeliveryFailed";
                    return false;
                }
            }

            if (ModsConfig.IdeologyActive && payment.goods.Any(g => g.thing.GetInnerIfMinified().def.IsNaturalOrgan))
            {
                var historyEvent = new HistoryEvent(HistoryEventDefOf.TradedOrgan, negotiator.Named(HistoryEventArgsNames.Doer));
                if (!historyEvent.Notify_PawnAboutToDo())
                {
                    errorKey = "SettlementServices.Error.BarterGoodsOrganRefused";
                    return false;
                }
            }

            return true;
        }
    }
}

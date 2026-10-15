using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.Framework.Payment
{
    internal sealed class ServiceBarterHold
    {
        public Thing source;
        public Thing held;
        public ThingOwner originOwner;
        public Map originMap;
        public IntVec3 originPosition;
    }

    internal sealed class ServiceBarterBundle
    {
        public readonly List<ServiceBarterHold> payerHolds = new List<ServiceBarterHold>();
        public readonly List<ServiceBarterHold> goodsHolds = new List<ServiceBarterHold>();
        public Settlement settlement;
        public Caravan caravan;
        public Pawn negotiator;
    }

    internal static class ServiceBarterTransaction
    {
        private sealed class DeliveredGood
        {
            public Thing held;
            public Pawn receiver;
        }

        public static bool TryTake(ServicePaymentSelection payment, SettlementServiceRequest request, int price, IEnumerable<ThingDefCountClass> requiredMaterials,
            out ServiceBarterBundle bundle, out decimal offeredValue, out string errorKey)
        {
            bundle = null;
            if (!ServiceBarterSource.TryValidate(payment, request, requiredMaterials, out offeredValue, out errorKey)) return false;
            if (!ServiceBarterGoodsSource.TryValidate(payment, request, out decimal goodsValue, out errorKey)) return false;

            if (!new ServiceBarterBalance(offeredValue, price, goodsValue).Covers)
            {
                errorKey = "SettlementServices.Error.BarterInsufficientValue";
                return false;
            }

            var newBundle = new ServiceBarterBundle { caravan = request.negotiator?.GetCaravan() };
            if (payment.HasGoods)
            {
                if (!ServiceBarterGoodsSource.TryResolveAccess(request, out newBundle.settlement, out newBundle.caravan, out newBundle.negotiator, out errorKey)) return false;
                if (!ServiceBarterGoodsSource.TryPreflightDelivery(payment, newBundle.caravan, newBundle.negotiator, out errorKey)) return false;
            }

            foreach (ServicePaymentEntry entry in payment.entries)
                newBundle.payerHolds.Add(Hold(entry));

            foreach (ServicePaymentEntry entry in payment.goods)
                newBundle.goodsHolds.Add(Hold(entry));

            bundle = newBundle;
            return true;
        }

        public static bool TryComplete(ServiceBarterBundle bundle, SettlementServiceRequest request, out string errorKey)
        {
            errorKey = null;
            if (bundle.goodsHolds.Count > 0 && !TryDeliverGoods(bundle, out errorKey))
            {
                Restore(bundle, request);
                return false;
            }

            Commit(bundle);
            return true;
        }

        public static void Restore(ServiceBarterBundle bundle, SettlementServiceRequest request)
        {
            if (bundle == null) return;

            RestoreHolds(bundle.goodsHolds, null);
            RestoreHolds(bundle.payerHolds, request);
            bundle.caravan?.RecacheInventory();
        }

        private static ServiceBarterHold Hold(ServicePaymentEntry entry)
        {
            var hold = new ServiceBarterHold
            {
                source = entry.thing,
                originOwner = entry.thing.holdingOwner,
                originMap = entry.thing.Spawned ? entry.thing.Map : null,
                originPosition = entry.thing.Position,
            };
            hold.held = entry.thing.SplitOff(entry.count);
            return hold;
        }

        private static void RestoreHolds(List<ServiceBarterHold> holds, SettlementServiceRequest request)
        {
            for (int i = holds.Count - 1; i >= 0; i--)
            {
                ServiceBarterHold hold = holds[i];
                Thing held = hold.held;
                if (held == null || held.Destroyed) continue;

                if (held != hold.source && !hold.source.Destroyed && hold.source.TryAbsorbStack(held, respectStackLimit: false)) continue;

                if (hold.originOwner != null && hold.originOwner.TryAdd(held, canMergeWithExistingStacks: false)) continue;

                if (hold.originMap != null)
                {
                    GenPlace.TryPlaceThing(held, hold.originPosition, hold.originMap, ThingPlaceMode.Near);
                    continue;
                }

                Caravan caravan = request?.negotiator?.GetCaravan();
                if (caravan != null) CaravanInventoryUtility.GiveThing(caravan, held);
                else LogLostItem(held);
            }
        }

        private static void LogLostItem(Thing held) =>
            SupportLog.Error($"Barter could not return {held} to its origin; the item was lost.");

        private static bool TryDeliverGoods(ServiceBarterBundle bundle, out string errorKey)
        {
            errorKey = null;
            Caravan caravan = bundle.caravan;
            if (caravan == null || caravan.Destroyed)
            {
                errorKey = "SettlementServices.Error.CaravanNoLongerExists";
                return false;
            }

            var delivered = new List<DeliveredGood>();
            foreach (ServiceBarterHold hold in bundle.goodsHolds)
            {
                Thing held = hold.held;
                if (held == null || held.Destroyed)
                {
                    RollBackDelivery(delivered);
                    errorKey = "SettlementServices.Error.BarterGoodsDeliveryFailed";
                    return false;
                }

                if (held is Pawn pawn)
                {
                    if (pawn.Dead || caravan.ContainsPawn(pawn))
                    {
                        RollBackDelivery(delivered);
                        errorKey = "SettlementServices.Error.BarterGoodsDeliveryFailed";
                        return false;
                    }

                    continue;
                }

                Pawn receiver = CaravanInventoryUtility.FindPawnToMoveInventoryTo(held, caravan.PawnsListForReading, null);
                if (receiver == null || !receiver.inventory.innerContainer.TryAdd(held, canMergeWithExistingStacks: false))
                {
                    RollBackDelivery(delivered);
                    errorKey = "SettlementServices.Error.BarterGoodsDeliveryFailed";
                    return false;
                }

                delivered.Add(new DeliveredGood { held = held, receiver = receiver });
            }

            foreach (ServiceBarterHold hold in bundle.goodsHolds)
            {
                hold.held.PreTraded(TradeAction.PlayerBuys, bundle.negotiator, bundle.settlement);

                if (hold.held is Pawn pawn)
                {
                    caravan.AddPawn(pawn, addCarriedPawnToWorldPawnsIfAny: true);
                    if (!caravan.ContainsPawn(pawn)) SupportLog.Error($"Barter could not add purchased pawn {pawn} to caravan {caravan}.");
                }
            }

            foreach (DeliveredGood good in delivered)
                MergeIntoInventory(good);

            return true;
        }

        private static void RollBackDelivery(List<DeliveredGood> delivered)
        {
            for (int i = delivered.Count - 1; i >= 0; i--)
                delivered[i].held.holdingOwner?.Remove(delivered[i].held);
        }

        private static void MergeIntoInventory(DeliveredGood good)
        {
            if (good.held.Destroyed || good.held.holdingOwner != good.receiver.inventory.innerContainer) return;

            foreach (Thing other in good.receiver.inventory.innerContainer.InnerListForReading.ToList())
            {
                if (other == good.held) continue;
                if (other.TryAbsorbStack(good.held, respectStackLimit: true)) return;
            }
        }

        private static void Commit(ServiceBarterBundle bundle)
        {
            foreach (ServiceBarterHold hold in bundle.payerHolds)
                if (hold.held != null && !hold.held.Destroyed) hold.held.Destroy();

            if (bundle.goodsHolds.Count > 0 && ModsConfig.IdeologyActive && bundle.goodsHolds.Any(h => h.held.GetInnerIfMinified().def.IsNaturalOrgan))
                Find.HistoryEventsManager.RecordEvent(new HistoryEvent(HistoryEventDefOf.TradedOrgan, bundle.negotiator.Named(HistoryEventArgsNames.Doer)));

            bundle.caravan?.RecacheInventory();
        }
    }
}

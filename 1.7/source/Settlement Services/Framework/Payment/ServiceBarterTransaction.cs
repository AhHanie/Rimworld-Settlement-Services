using System.Collections.Generic;
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

    internal static class ServiceBarterTransaction
    {
        public static bool TryTake(ServicePaymentSelection payment, SettlementServiceRequest request, int price, IEnumerable<ThingDefCountClass> requiredMaterials,
            out List<ServiceBarterHold> holds, out decimal value, out string errorKey)
        {
            holds = null;
            if (!ServiceBarterSource.TryValidate(payment, request, requiredMaterials, out value, out errorKey)) return false;

            if (!ServiceBarterValuation.Covers(value, price))
            {
                errorKey = "SettlementServices.Error.BarterInsufficientValue";
                return false;
            }

            holds = new List<ServiceBarterHold>();
            foreach (ServicePaymentEntry entry in payment.entries)
            {
                var hold = new ServiceBarterHold
                {
                    source = entry.thing,
                    originOwner = entry.thing.holdingOwner,
                    originMap = entry.thing.Spawned ? entry.thing.Map : null,
                    originPosition = entry.thing.Position,
                };
                hold.held = entry.thing.SplitOff(entry.count);
                holds.Add(hold);
            }

            return true;
        }

        public static void Restore(List<ServiceBarterHold> holds, SettlementServiceRequest request)
        {
            if (holds == null) return;

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

                Caravan caravan = request.negotiator?.GetCaravan();
                if (caravan != null) CaravanInventoryUtility.GiveThing(caravan, held);
            }
        }

        public static void Commit(List<ServiceBarterHold> holds)
        {
            if (holds == null) return;

            foreach (ServiceBarterHold hold in holds)
                if (hold.held != null && !hold.held.Destroyed) hold.held.Destroy();
        }
    }
}

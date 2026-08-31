using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Framework.Payment
{
    public static class CaravanInventoryTransfer
    {
        public static bool TryConsume(Caravan caravan, List<ThingDefCountClass> items, out string errorKey)
        {
            if (caravan == null) { errorKey = "SettlementServices.Error.CaravanNoLongerExists"; return false; }

            foreach (ThingDefCountClass required in items)
            {
                int have = CaravanInventoryUtility.AllInventoryItems(caravan).Where(t => t.def == required.thingDef).Sum(t => t.stackCount);
                if (have < required.count) { errorKey = "SettlementServices.Error.InsufficientPlayerSuppliedInput"; return false; }
            }

            foreach (ThingDefCountClass required in items)
            {
                int remaining = required.count;
                foreach (Thing thing in CaravanInventoryUtility.AllInventoryItems(caravan).Where(t => t.def == required.thingDef).ToList())
                {
                    if (remaining <= 0) break;
                    int take = Math.Min(remaining, thing.stackCount);
                    thing.SplitOff(take).Destroy();
                    remaining -= take;
                }
            }

            errorKey = null;
            return true;
        }

        public static void Refund(Caravan caravan, List<ThingDefCountClass> items)
        {
            if (caravan == null) return;

            foreach (ThingDefCountClass item in items)
            {
                Thing thing = ThingMaker.MakeThing(item.thingDef);
                thing.stackCount = item.count;
                CaravanInventoryUtility.GiveThing(caravan, thing);
            }
        }
    }
}

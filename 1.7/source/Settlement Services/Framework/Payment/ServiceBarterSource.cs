using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.Framework.Payment
{
    public sealed class BarterCandidate
    {
        public Thing thing;
        public int reservedUnits;

        public int AvailableUnits => thing == null || thing.Destroyed ? 0 : Math.Max(0, thing.stackCount - reservedUnits);
    }

    public static class ServiceBarterSource
    {
        public static List<Thing> PayerItems(SettlementServiceRequest request)
        {
            var items = new List<Thing>();

            if (request.channel == RequestChannel.Remote)
            {
                foreach (Map map in Find.Maps)
                {
                    if (!map.IsPlayerHome) continue;
                    foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).ToList())
                        if (thing.Spawned && IsPledgeable(thing) && thing.IsInValidStorage()) items.Add(thing);
                }

                return items;
            }

            Caravan caravan = request.negotiator?.GetCaravan();
            if (caravan == null) return items;

            foreach (Thing thing in CaravanInventoryUtility.AllInventoryItems(caravan).ToList())
                if (IsPledgeable(thing)) items.Add(thing);

            return items;
        }

        public static List<BarterCandidate> BuildCandidates(SettlementServiceRequest request)
        {
            HashSet<Thing> targets = TargetSet(request);
            Dictionary<ThingDef, int> reserveLeft = ReservedMaterialCounts(request.channel, request.playerSuppliedInputs);

            var candidates = new List<BarterCandidate>();
            foreach (Thing thing in PayerItems(request))
            {
                if (targets.Contains(thing)) continue;

                int reserved = 0;
                if (reserveLeft.TryGetValue(thing.def, out int left) && left > 0)
                {
                    reserved = Math.Min(left, thing.stackCount);
                    reserveLeft[thing.def] = left - reserved;
                }

                candidates.Add(new BarterCandidate { thing = thing, reservedUnits = reserved });
            }

            return candidates;
        }

        public static bool TryValidate(ServicePaymentSelection payment, SettlementServiceRequest request, IEnumerable<ThingDefCountClass> requiredMaterials,
            out decimal total, out string errorKey)
        {
            total = 0m;
            if (payment == null || !payment.IsBarter) { errorKey = "SettlementServices.Error.BarterInvalidSelection"; return false; }

            List<Thing> pool = PayerItems(request);
            var poolSet = new HashSet<Thing>(pool);
            HashSet<Thing> targets = TargetSet(request);
            var seen = new HashSet<Thing>();
            var pledgedByDef = new Dictionary<ThingDef, int>();

            foreach (ServicePaymentEntry entry in payment.entries)
            {
                if (entry?.thing == null || entry.count <= 0) { errorKey = "SettlementServices.Error.BarterInvalidSelection"; return false; }
                if (entry.thing.Destroyed || !poolSet.Contains(entry.thing) || entry.count > entry.thing.stackCount)
                { errorKey = "SettlementServices.Error.BarterItemUnavailable"; return false; }
                if (targets.Contains(entry.thing) || !seen.Add(entry.thing)) { errorKey = "SettlementServices.Error.BarterInvalidSelection"; return false; }

                pledgedByDef.TryGetValue(entry.thing.def, out int pledged);
                pledgedByDef[entry.thing.def] = pledged + entry.count;
            }

            if (!ServiceBarterValuation.TryTotal(payment.entries, out total)) { errorKey = "SettlementServices.Error.BarterInvalidValue"; return false; }

            if (request.channel != RequestChannel.Remote && requiredMaterials != null)
            {
                foreach (ThingDefCountClass required in requiredMaterials)
                {
                    int have = pool.Where(t => t.def == required.thingDef).Sum(t => t.stackCount);
                    pledgedByDef.TryGetValue(required.thingDef, out int pledged);
                    if (have - pledged < required.count) { errorKey = "SettlementServices.Error.BarterReservedForMaterials"; return false; }
                }
            }

            errorKey = null;
            return true;
        }

        private static bool IsPledgeable(Thing thing) =>
            thing != null && !thing.Destroyed && thing.stackCount > 0 && !(thing is Pawn) && thing.def.destroyable;

        private static HashSet<Thing> TargetSet(SettlementServiceRequest request) =>
            new HashSet<Thing>((request.targets ?? new List<ServiceTargetSelection>()).Where(t => t?.thing != null).Select(t => t.thing));

        private static Dictionary<ThingDef, int> ReservedMaterialCounts(RequestChannel channel, IEnumerable<ThingDefCountClass> materials)
        {
            var result = new Dictionary<ThingDef, int>();
            if (channel == RequestChannel.Remote || materials == null) return result;

            foreach (ThingDefCountClass material in materials)
            {
                if (material?.thingDef == null || material.count <= 0) continue;
                result.TryGetValue(material.thingDef, out int existing);
                result[material.thingDef] = existing + material.count;
            }

            return result;
        }
    }
}

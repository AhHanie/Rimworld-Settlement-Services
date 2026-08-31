using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Framework;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Stock;

namespace Settlement_Services.UI
{
    public class StockInputRow
    {
        public readonly ServiceStockRequirement requirement;
        public readonly ThingDef thingDef;
        public readonly int availableInCaravan;
        public int suppliedAmount;

        public StockInputRow(ServiceStockRequirement requirement, ThingDef thingDef, int availableInCaravan)
        {
            this.requirement = requirement;
            this.thingDef = thingDef;
            this.availableInCaravan = availableInCaravan;
        }
    }

    public static class ServiceStockInputPicker
    {
        public static List<StockInputRow> BuildRows(ServiceRequestSession session)
        {
            var rows = new List<StockInputRow>();
            Caravan caravan = session?.caravan;
            SettlementServiceDef def = session?.def;
            if (caravan == null || def == null) return rows;

            SettlementServiceRequest request = session.BuildRequest();
            if (!ServiceBatchPlanner.TryBuildUnits(def, request, out List<SettlementServiceRequest> units, out _)) return rows;

            var requirements = new List<ServiceStockRequirement>();
            foreach (SettlementServiceRequest unit in units)
            {
                if (!def.stockRequirements.NullOrEmpty()) ServiceBatchPlanner.MergeRequirements(requirements, def.stockRequirements);
                ServiceBatchPlanner.MergeRequirements(requirements, def.Worker.GetDynamicStockRequirements(unit));
            }

            var caravanTotals = new Dictionary<ThingDef, int>();
            foreach (Thing thing in CaravanInventoryUtility.AllInventoryItems(caravan))
            {
                caravanTotals.TryGetValue(thing.def, out int existing);
                caravanTotals[thing.def] = existing + thing.stackCount;
            }

            var claimed = new Dictionary<ThingDef, int>();
            foreach (ServiceStockRequirement req in requirements.Where(r => r.playerCanSupply))
            {
                foreach (ThingDef thingDef in CandidateThingDefs(req))
                {
                    caravanTotals.TryGetValue(thingDef, out int total);
                    claimed.TryGetValue(thingDef, out int alreadyClaimed);
                    int remaining = Mathf.Max(0, total - alreadyClaimed);
                    if (remaining <= 0) continue;

                    int rowMax = Mathf.Min(remaining, req.amount);
                    var row = new StockInputRow(req, thingDef, rowMax)
                    {
                        suppliedAmount = Mathf.Min(rowMax, session.playerSuppliedInputs.Where(i => i.thingDef == thingDef).Sum(i => i.count)),
                    };
                    rows.Add(row);
                    claimed[thingDef] = alreadyClaimed + rowMax;
                }
            }
            return rows;
        }

        private static IEnumerable<ThingDef> CandidateThingDefs(ServiceStockRequirement req)
        {
            if (req.IsExact)
            {
                ThingDef exact = DefDatabase<ThingDef>.GetNamedSilentFail(req.thingDefName);
                if (exact != null) yield return exact;
                yield break;
            }

            SettlementStockCategoryDef category = DefDatabase<SettlementStockCategoryDef>.GetNamedSilentFail(req.stockCategoryDefName);
            if (category == null) yield break;
            foreach (SettlementStockItemReference reference in SettlementStockCatalog.ItemsFor(category))
                yield return reference.thing;
        }

        public static void ApplyTo(ServiceRequestSession session, List<StockInputRow> rows)
        {
            var rowThingDefs = new HashSet<ThingDef>(rows.Select(r => r.thingDef));
            session.playerSuppliedInputs.RemoveAll(i => rowThingDefs.Contains(i.thingDef));
            foreach (StockInputRow row in rows.Where(r => r.suppliedAmount > 0))
                session.playerSuppliedInputs.Add(new ThingDefCountClass(row.thingDef, row.suppliedAmount));
        }
    }
}

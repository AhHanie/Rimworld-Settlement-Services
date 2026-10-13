using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Stock;
using Settlement_Services.UI;

namespace Settlement_Services.Services.Crafting
{
    public class CraftingMaterialPreviewRow
    {
        public ThingDef thingDef;
        public int required;
        public int carried;
        public int playerSupplied;
        public int settlementSupplied;
        public bool blocking;
    }

    public class CraftingMaterialPreview
    {
        public bool planValid;
        public readonly List<CraftingMaterialPreviewRow> rows = new List<CraftingMaterialPreviewRow>();
    }

    internal static class CraftingMaterialPreviewBuilder
    {
        public static CraftingMaterialPreview Build(ServiceRequestSession session)
        {
            var preview = new CraftingMaterialPreview();
            if (session?.caravan == null || session.settlement == null || session.craftingCommissionLines.Count == 0)
                return preview;

            Dictionary<ThingDef, int> carried = CarriedTotals(session.caravan);
            NormalizeToCarried(session, carried);

            if (CraftingProductionPlanner.TryPlan(session.settlement, session.craftingCommissionLines, session.playerSuppliedInputs, session.craftingCrafterCount,
                out CraftingProductionPlan plan, out _, out CraftingMaterialRequirement blocking))
            {
                preview.planValid = true;
                BuildValidRows(session, plan, carried, preview);
            }
            else if (blocking != null)
            {
                BuildBlockingRow(session, blocking, carried, preview);
            }

            return preview;
        }

        public static void SetPlayerSupplied(ServiceRequestSession session, ThingDef thingDef, int amount)
        {
            if (thingDef == null) return;
            List<ThingDefCountClass> inputs = session.playerSuppliedInputs;
            ThingDefCountClass existing = inputs.Find(i => i.thingDef == thingDef);

            if (amount <= 0)
            {
                if (existing != null) inputs.Remove(existing);
                return;
            }

            if (existing != null) existing.count = amount;
            else inputs.Add(new ThingDefCountClass(thingDef, amount));
        }

        private static void BuildValidRows(ServiceRequestSession session, CraftingProductionPlan plan, Dictionary<ThingDef, int> carried, CraftingMaterialPreview preview)
        {
            foreach (CraftingMaterialRequirement raw in plan.rawMaterials)
            {
                if (!raw.playerCanSupply) continue;
                ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(raw.thingDefName);
                if (thingDef == null) continue;
                if (!carried.TryGetValue(thingDef, out int carriedAmount) || carriedAmount <= 0) continue;

                ClampPlayerSupplied(session, thingDef, Mathf.Min(raw.amount, carriedAmount));
            }

            List<ServiceStockRequirement> stockRequirements = CraftingStockRequirements.ToStockRequirements(plan);
            StockAllocationResult allocation = SettlementStockService.TryAllocate(session.settlement, stockRequirements, session.playerSuppliedInputs);

            foreach (CraftingMaterialRequirement raw in plan.rawMaterials)
            {
                if (!raw.playerCanSupply) continue;
                ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(raw.thingDefName);
                if (thingDef == null) continue;
                if (!carried.TryGetValue(thingDef, out int carriedAmount) || carriedAmount <= 0) continue;

                int playerSupplied = session.playerSuppliedInputs.Where(i => i.thingDef == thingDef).Sum(i => i.count);
                int settlementSupplied = allocation.Success
                    ? allocation.SettlementSupplied.Where(c => c.thingDef == thingDef).Sum(c => c.count)
                    : 0;

                preview.rows.Add(new CraftingMaterialPreviewRow
                {
                    thingDef = thingDef,
                    required = raw.amount,
                    carried = carriedAmount,
                    playerSupplied = playerSupplied,
                    settlementSupplied = settlementSupplied,
                    blocking = false,
                });
            }
        }

        private static void BuildBlockingRow(ServiceRequestSession session, CraftingMaterialRequirement blocking, Dictionary<ThingDef, int> carried, CraftingMaterialPreview preview)
        {
            if (!blocking.playerCanSupply) return;
            ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(blocking.thingDefName);
            if (thingDef == null) return;
            if (!carried.TryGetValue(thingDef, out int carriedAmount) || carriedAmount <= 0) return;

            ServiceStockRequirement grossRequirement = CraftingStockRequirements.ToBlockingRequirement(blocking, session.playerSuppliedInputs);
            ClampPlayerSupplied(session, thingDef, Mathf.Min(grossRequirement.amount, carriedAmount));

            int playerSupplied = session.playerSuppliedInputs.Where(i => i.thingDef == thingDef).Sum(i => i.count);

            preview.rows.Add(new CraftingMaterialPreviewRow
            {
                thingDef = thingDef,
                required = grossRequirement.amount,
                carried = carriedAmount,
                playerSupplied = playerSupplied,
                settlementSupplied = 0,
                blocking = true,
            });
        }

        private static Dictionary<ThingDef, int> CarriedTotals(Caravan caravan)
        {
            var totals = new Dictionary<ThingDef, int>();
            foreach (Thing thing in CaravanInventoryUtility.AllInventoryItems(caravan))
            {
                totals.TryGetValue(thing.def, out int existing);
                totals[thing.def] = existing + thing.stackCount;
            }
            return totals;
        }

        private static void NormalizeToCarried(ServiceRequestSession session, Dictionary<ThingDef, int> carried)
        {
            List<ThingDefCountClass> inputs = session.playerSuppliedInputs;
            for (int i = inputs.Count - 1; i >= 0; i--)
            {
                ThingDefCountClass item = inputs[i];
                carried.TryGetValue(item.thingDef, out int carriedAmount);
                int clamped = Mathf.Min(item.count, carriedAmount);
                if (clamped <= 0) inputs.RemoveAt(i);
                else item.count = clamped;
            }
        }

        private static void ClampPlayerSupplied(ServiceRequestSession session, ThingDef thingDef, int max)
        {
            List<ThingDefCountClass> inputs = session.playerSuppliedInputs;
            ThingDefCountClass existing = inputs.Find(i => i.thingDef == thingDef);
            if (existing == null) return;

            int clamped = Mathf.Clamp(existing.count, 0, max);
            if (clamped <= 0) inputs.Remove(existing);
            else existing.count = clamped;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Specialty;

namespace Settlement_Services.Framework.Stock
{
    public static class SettlementStockService
    {
        public static bool IsEligibleForSettlement(Settlement settlement, SettlementStockItemReference reference)
        {
            if (settlement?.Faction?.def == null || reference == null) return false;
            if (reference.item.minFactionTechLevel == TechLevel.Undefined) return true;
            return (int)reference.item.minFactionTechLevel <= (int)settlement.Faction.def.techLevel;
        }

        public static IEnumerable<SettlementStockItemReference> ItemsFor(Settlement settlement, SettlementStockCategoryDef category) =>
            SettlementStockCatalog.ItemsFor(category).Where(r => IsEligibleForSettlement(settlement, r));

        public static IEnumerable<ThingDef> AllStockedThingDefsFor(Settlement settlement)
        {
            foreach (ThingDef thing in SettlementStockCatalog.AllStockedThingDefs())
                if (IsEligibleForSettlement(settlement, SettlementStockCatalog.ItemFor(thing))) yield return thing;

            foreach (DynamicStockEntryView view in OfferedDynamicStock(settlement))
            {
                ThingDef thing = DefDatabase<ThingDef>.GetNamedSilentFail(view.thingDefName);
                if (thing != null) yield return thing;
            }
        }

        public static bool IsOffered(Settlement settlement, ThingDef thingDef)
        {
            if (settlement?.Faction?.def == null || thingDef == null) return false;

            SettlementStockItemReference reference = SettlementStockCatalog.ItemFor(thingDef);
            if (reference != null) return IsEligibleForSettlement(settlement, reference);

            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            domain.SyncDynamicStock(settlement);
            return domain.TryGetOfferedDynamicStock(settlement.ID, thingDef.defName, out _);
        }

        public static IEnumerable<ThingDef> ThingDefsFor(Settlement settlement, SettlementStockCategoryDef category)
        {
            foreach (SettlementStockItemReference reference in ItemsFor(settlement, category))
                yield return reference.thing;

            foreach (DynamicStockEntryView view in OfferedDynamicStock(settlement))
            {
                if (view.categoryDefName != category.defName) continue;

                ThingDef thing = DefDatabase<ThingDef>.GetNamedSilentFail(view.thingDefName);
                if (thing != null) yield return thing;
            }
        }

        public static IEnumerable<ThingDef> CandidateThingDefsFor(SettlementStockCategoryDef category) =>
            SettlementStockCatalog.ItemsFor(category).Select(r => r.thing).Concat(SettlementDynamicStockCatalog.CandidatesFor(category));

        public static IReadOnlyList<DynamicStockEntryView> OfferedDynamicStock(Settlement settlement)
        {
            if (settlement?.Faction?.def == null) return Array.Empty<DynamicStockEntryView>();

            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            domain.SyncDynamicStock(settlement);
            return domain.OfferedDynamicStock(settlement.ID);
        }

        public static int GetAvailableStock(Settlement settlement, ThingDef thingDef)
        {
            SettlementStockItemReference reference = SettlementStockCatalog.ItemFor(thingDef);
            if (reference == null) return GetAvailableDynamicStock(settlement, thingDef);
            if (!IsEligibleForSettlement(settlement, reference)) return 0;

            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            int currentAmount = domain.CatchUpStock(settlement.ID, thingDef.defName,
                EffectiveCapacity(settlement, reference),
                EffectiveRefreshAmount(settlement, reference),
                EffectiveRefreshIntervalTicks(settlement, reference));

            int reserved = domain.TotalReserved(settlement.ID, thingDef.defName);
            return Mathf.Max(0, currentAmount - reserved);
        }

        private static int GetAvailableDynamicStock(Settlement settlement, ThingDef thingDef)
        {
            if (settlement?.Faction?.def == null || thingDef == null) return 0;

            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            domain.SyncDynamicStock(settlement);
            if (!domain.TryGetOfferedDynamicStock(settlement.ID, thingDef.defName, out DynamicStockEntryView view)) return 0;

            SettlementStockCategoryDef category = DefDatabase<SettlementStockCategoryDef>.GetNamedSilentFail(view.categoryDefName);
            int capacity = EffectiveDynamicCapacity(view.baseCapacity, CapacityMultiplier(settlement, category));
            int reserved = domain.TotalDynamicReserved(settlement.ID, thingDef.defName);
            return Mathf.Max(0, Mathf.Min(view.currentAmount, capacity) - reserved);
        }

        public static int GetAvailableStock(Settlement settlement, SettlementStockCategoryDef category) =>
            ThingDefsFor(settlement, category).Sum(t => GetAvailableStock(settlement, t));

        public static SettlementStockEffectiveSettings EffectiveSettings(Settlement settlement, SettlementStockItemReference reference) =>
            SettlementStockEffectiveSettings.For(reference, settlement.Faction.def.techLevel);

        public static float CapacityMultiplier(Settlement settlement, SettlementStockCategoryDef category) =>
            SettlementSpecialtyService.GetSpecialties(settlement)
                .SelectMany(d => d.stockModifiers)
                .Where(m => m.stockCategoryDefName == category.defName)
                .Aggregate(1f, (acc, m) => acc * m.capacityMultiplier);

        public static float RefreshMultiplier(Settlement settlement, SettlementStockCategoryDef category) =>
            SettlementSpecialtyService.GetSpecialties(settlement)
                .SelectMany(d => d.stockModifiers)
                .Where(m => m.stockCategoryDefName == category.defName)
                .Aggregate(1f, (acc, m) => acc * m.refreshRateMultiplier);

        public static int EffectiveDynamicCapacity(int baseCapacity, float capacityMultiplier) =>
            Mathf.Max(0, Mathf.RoundToInt(baseCapacity * capacityMultiplier));

        public static int EffectiveCapacity(Settlement settlement, SettlementStockItemReference reference) =>
            Mathf.RoundToInt(EffectiveSettings(settlement, reference).capacity * CapacityMultiplier(settlement, reference.category));

        public static int EffectiveRefreshAmount(Settlement settlement, SettlementStockItemReference reference) =>
            Mathf.RoundToInt(EffectiveSettings(settlement, reference).refreshAmount * RefreshMultiplier(settlement, reference.category));

        public static int EffectiveRefreshIntervalTicks(Settlement settlement, SettlementStockItemReference reference) =>
            EffectiveSettings(settlement, reference).refreshIntervalTicks;

        public static int EffectiveCapacity(Settlement settlement, SettlementStockCategoryDef category)
        {
            int total = ItemsFor(settlement, category).Sum(r => EffectiveCapacity(settlement, r));

            List<DynamicStockEntryView> dynamicViews = OfferedDynamicStock(settlement).Where(v => v.categoryDefName == category.defName).ToList();
            if (dynamicViews.Count == 0) return total;

            float multiplier = CapacityMultiplier(settlement, category);
            return total + dynamicViews.Sum(v => EffectiveDynamicCapacity(v.baseCapacity, multiplier));
        }

        public static StockAvailabilityReport CheckAvailability(Settlement settlement, IEnumerable<ServiceStockRequirement> requirements, IEnumerable<ThingDefCountClass> playerSuppliedInputs = null)
        {
            StockAllocationResult result = TryAllocate(settlement, requirements, playerSuppliedInputs);
            return result.Success ? StockAvailabilityReport.Available : StockAvailabilityReport.Unavailable(result.ErrorKey);
        }

        public static StockAllocationResult TryAllocate(Settlement settlement, IEnumerable<ServiceStockRequirement> requirements, IEnumerable<ThingDefCountClass> playerSuppliedInputs) =>
            TryAllocate(settlement, requirements, playerSuppliedInputs, null);

        public static StockAllocationResult TryAllocate(Settlement settlement, IEnumerable<ServiceStockRequirement> requirements, IEnumerable<ThingDefCountClass> playerSuppliedInputs, StockAllocationLedger ledger)
        {
            Dictionary<ThingDef, int> settlementLedger = ledger?.Settlement ?? new Dictionary<ThingDef, int>();
            var playerLedger = new Dictionary<ThingDef, int>();
            if (playerSuppliedInputs != null)
                foreach (ThingDefCountClass item in playerSuppliedInputs)
                {
                    playerLedger.TryGetValue(item.thingDef, out int existing);
                    playerLedger[item.thingDef] = existing + item.count;
                }

            var settlementResult = new List<ThingDefCountClass>();
            var playerResult = new List<ThingDefCountClass>();

            foreach (ServiceStockRequirement req in requirements)
            {
                int remaining = req.amount;
                if (remaining <= 0 && req.nutritionRequired <= 0f) continue;

                if (req.IsExact)
                {
                    ThingDef thing = DefDatabase<ThingDef>.GetNamedSilentFail(req.thingDefName);
                    if (thing == null)
                    {
                        Settlement_Services.SupportLog.Error($"Stock requirement references unknown ThingDef {req.thingDefName}; skipping it.");
                        continue;
                    }

                    remaining = TakeFromPlayer(req, thing, remaining, playerLedger, playerResult);
                    remaining = TakeFromSettlement(settlement, thing, remaining, settlementLedger, settlementResult);
                    if (remaining > 0) return StockAllocationResult.Fail("SettlementServices.Error.InsufficientStock");
                    continue;
                }

                SettlementStockCategoryDef category = DefDatabase<SettlementStockCategoryDef>.GetNamedSilentFail(req.stockCategoryDefName);
                if (category == null)
                {
                    Settlement_Services.SupportLog.Error($"Stock requirement references unknown SettlementStockCategoryDef {req.stockCategoryDefName}; skipping it.");
                    continue;
                }

                if (req.nutritionRequired > 0f)
                {
                    float remainingNutrition = req.nutritionRequired;

                    if (req.playerCanSupply)
                        remainingNutrition = TakeNutritionFromPlayer(category, req.preferredThingDefName, remainingNutrition, playerLedger, playerResult);

                    if (remainingNutrition > 0f)
                        remainingNutrition = TakeNutritionFromSettlement(settlement, category, req.preferredThingDefName, remainingNutrition, settlementLedger, settlementResult);

                    if (remainingNutrition > 0f) return StockAllocationResult.Fail("SettlementServices.Error.InsufficientStock");
                    continue;
                }

                if (req.playerCanSupply)
                {
                    foreach (ThingDef thing in OrderCandidatesByPlayerAvailability(category, req.preferredThingDefName, playerLedger))
                    {
                        if (remaining <= 0) break;
                        remaining = TakeFromPlayer(req, thing, remaining, playerLedger, playerResult);
                    }
                }

                if (remaining > 0)
                {
                    foreach (ThingDef thing in OrderCandidatesBySettlementAvailability(settlement, category, req.preferredThingDefName, settlementLedger))
                    {
                        if (remaining <= 0) break;
                        remaining = TakeFromSettlement(settlement, thing, remaining, settlementLedger, settlementResult);
                    }
                }

                if (remaining > 0) return StockAllocationResult.Fail("SettlementServices.Error.InsufficientStock");
            }

            return StockAllocationResult.Ok(settlementResult, playerResult);
        }

        private static int TakeFromPlayer(ServiceStockRequirement req, ThingDef thing, int remaining, Dictionary<ThingDef, int> playerLedger, List<ThingDefCountClass> playerResult)
        {
            if (!req.playerCanSupply) return remaining;
            playerLedger.TryGetValue(thing, out int available);
            int take = Mathf.Min(remaining, available);
            if (take <= 0) return remaining;

            playerLedger[thing] = available - take;
            AddCount(playerResult, thing, take);
            return remaining - take;
        }

        private static int TakeFromSettlement(Settlement settlement, ThingDef thing, int remaining, Dictionary<ThingDef, int> settlementLedger, List<ThingDefCountClass> settlementResult)
        {
            int available = GetLedgerAvailable(settlement, thing, settlementLedger);
            int take = Mathf.Min(remaining, available);
            if (take <= 0) return remaining;

            settlementLedger[thing] = available - take;
            AddCount(settlementResult, thing, take);
            return remaining - take;
        }

        private static float TakeNutritionFromPlayer(SettlementStockCategoryDef category, string preferredThingDefName, float remainingNutrition, Dictionary<ThingDef, int> playerLedger, List<ThingDefCountClass> playerResult)
        {
            foreach (ThingDef thing in OrderCandidatesByPlayerAvailability(category, preferredThingDefName, playerLedger))
            {
                if (remainingNutrition <= 0f) break;

                float nutritionPerItem = thing.GetStatValueAbstract(StatDefOf.Nutrition);
                if (nutritionPerItem <= 0f) continue;

                playerLedger.TryGetValue(thing, out int available);
                int take = Mathf.Min(available, Mathf.CeilToInt(remainingNutrition / nutritionPerItem));
                if (take <= 0) continue;

                playerLedger[thing] = available - take;
                AddCount(playerResult, thing, take);
                remainingNutrition -= take * nutritionPerItem;
            }
            return remainingNutrition;
        }

        private static float TakeNutritionFromSettlement(Settlement settlement, SettlementStockCategoryDef category, string preferredThingDefName, float remainingNutrition, Dictionary<ThingDef, int> settlementLedger, List<ThingDefCountClass> settlementResult)
        {
            foreach (ThingDef thing in OrderCandidatesBySettlementAvailability(settlement, category, preferredThingDefName, settlementLedger))
            {
                if (remainingNutrition <= 0f) break;

                float nutritionPerItem = thing.GetStatValueAbstract(StatDefOf.Nutrition);
                if (nutritionPerItem <= 0f) continue;

                int available = GetLedgerAvailable(settlement, thing, settlementLedger);
                int take = Mathf.Min(available, Mathf.CeilToInt(remainingNutrition / nutritionPerItem));
                if (take <= 0) continue;

                settlementLedger[thing] = available - take;
                AddCount(settlementResult, thing, take);
                remainingNutrition -= take * nutritionPerItem;
            }
            return remainingNutrition;
        }

        private static int GetLedgerAvailable(Settlement settlement, ThingDef thing, Dictionary<ThingDef, int> settlementLedger)
        {
            if (!settlementLedger.TryGetValue(thing, out int available))
            {
                available = GetAvailableStock(settlement, thing);
                settlementLedger[thing] = available;
            }
            return available;
        }

        private static void AddCount(List<ThingDefCountClass> list, ThingDef thing, int amount)
        {
            ThingDefCountClass existing = list.Find(c => c.thingDef == thing);
            if (existing != null) existing.count += amount;
            else list.Add(new ThingDefCountClass(thing, amount));
        }

        private static IEnumerable<ThingDef> OrderCandidatesByPlayerAvailability(SettlementStockCategoryDef category, string preferredThingDefName, Dictionary<ThingDef, int> playerLedger)
        {
            return CandidateThingDefsFor(category)
                .Where(t => playerLedger.TryGetValue(t, out int amount) && amount > 0)
                .OrderByDescending(t => t.defName == preferredThingDefName)
                .ThenByDescending(t => playerLedger[t])
                .ThenBy(t => t.BaseMarketValue)
                .ThenBy(t => t.defName, StringComparer.Ordinal);
        }

        private static IEnumerable<ThingDef> OrderCandidatesBySettlementAvailability(Settlement settlement, SettlementStockCategoryDef category, string preferredThingDefName, Dictionary<ThingDef, int> settlementLedger)
        {
            List<ThingDef> things = ThingDefsFor(settlement, category).ToList();
            foreach (ThingDef t in things) GetLedgerAvailable(settlement, t, settlementLedger);

            return things
                .Where(t => settlementLedger[t] > 0)
                .OrderByDescending(t => t.defName == preferredThingDefName)
                .ThenByDescending(t => settlementLedger[t])
                .ThenBy(t => t.BaseMarketValue)
                .ThenBy(t => t.defName, StringComparer.Ordinal);
        }
    }
}

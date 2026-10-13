using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Stock;

namespace Settlement_Services.Domain
{
    internal static class DynamicStockEngine
    {
        public static DynamicStockEntryRecord FindEntry(SettlementRecord record, string thingDefName)
        {
            foreach (DynamicStockPoolRecord pool in record.dynamicStockPools)
                foreach (DynamicStockEntryRecord entry in pool.entries)
                    if (entry.thingDefName == thingDefName) return entry;
            return null;
        }

        public static DynamicStockEntryRecord FindOfferedEntry(SettlementRecord record, string thingDefName, out DynamicStockPoolRecord owner)
        {
            foreach (DynamicStockPoolRecord pool in record.dynamicStockPools)
            {
                if (!pool.wasEligible) continue;
                foreach (DynamicStockEntryRecord entry in pool.entries)
                {
                    if (entry.retired || entry.thingDefName != thingDefName) continue;
                    owner = pool;
                    return entry;
                }
            }
            owner = null;
            return null;
        }

        public static bool HasDynamicReservation(SettlementRecord record, string thingDefName) =>
            record.reservations.Any(r => r.usesDynamicStock && r.stockThingDefName == thingDefName);

        public static void Sync(SettlementRecord record, Settlement settlement, int now)
        {
            TechLevel tech = settlement.Faction.def.techLevel;
            foreach (SettlementDynamicStockPoolReference poolRef in SettlementDynamicStockCatalog.AllPools())
                SyncPool(record, settlement, poolRef, tech, now);
        }

        public static void MarkRefreshDue(SettlementRecord record, TechLevel tech, int now)
        {
            foreach (DynamicStockPoolRecord rec in record.dynamicStockPools)
            {
                SettlementDynamicStockPoolReference poolRef = SettlementDynamicStockCatalog.PoolFor(rec.poolKey);
                if (poolRef == null || !rec.wasEligible || !SettlementDynamicStockEffectiveSettings.IsEligible(poolRef.pool, tech)) continue;

                int interval = SettlementDynamicStockEffectiveSettings.For(poolRef.pool, tech).refreshIntervalTicks;
                rec.lastRefreshTick = Mathf.Min(rec.lastRefreshTick, now - interval);
            }
        }

        public static List<int> PruneStale(SettlementRecord record)
        {
            var affectedJobIds = new List<int>();

            foreach (DynamicStockPoolRecord rec in record.dynamicStockPools.ToList())
            {
                bool poolMissing = SettlementDynamicStockCatalog.PoolFor(rec.poolKey) == null;

                foreach (DynamicStockEntryRecord entry in rec.entries.ToList())
                {
                    if (!poolMissing && DefDatabase<ThingDef>.GetNamedSilentFail(entry.thingDefName) != null) continue;

                    affectedJobIds.AddRange(record.reservations
                        .Where(r => r.usesDynamicStock && r.stockThingDefName == entry.thingDefName)
                        .Select(r => r.jobId));
                    rec.entries.Remove(entry);
                }

                if (poolMissing) record.dynamicStockPools.Remove(rec);
            }

            return affectedJobIds.Distinct().ToList();
        }

        private static void SyncPool(SettlementRecord record, Settlement settlement, SettlementDynamicStockPoolReference poolRef, TechLevel tech, int now)
        {
            DynamicStockPoolRecord rec = record.dynamicStockPools.Find(p => p.poolKey == poolRef.key);
            bool eligible = SettlementDynamicStockEffectiveSettings.IsEligible(poolRef.pool, tech);

            if (rec == null)
            {
                if (!eligible) return;

                SettlementDynamicStockEffectiveSettings initial = SettlementDynamicStockEffectiveSettings.For(poolRef.pool, tech);
                rec = new DynamicStockPoolRecord
                {
                    poolKey = poolRef.key,
                    selectedThreshold = initial.selectedThreshold,
                    wasEligible = true,
                    lastRefreshTick = now,
                };
                record.dynamicStockPools.Add(rec);
                AddEntries(record, settlement, poolRef, rec, tech, initial, Rand.RangeInclusive(initial.minDifferentGoods, initial.maxDifferentGoods), null);
                return;
            }

            if (!eligible)
            {
                if (rec.wasEligible)
                {
                    rec.wasEligible = false;
                    rec.lastRefreshTick = now;
                }
                foreach (DynamicStockEntryRecord entry in rec.entries.ToList())
                    RetireOrRemove(record, rec, entry);
                return;
            }

            ReconcileEntries(record, rec, poolRef, tech);

            SettlementDynamicStockEffectiveSettings settings = SettlementDynamicStockEffectiveSettings.For(poolRef.pool, tech);
            if (!rec.wasEligible || rec.selectedThreshold != settings.selectedThreshold)
            {
                rec.wasEligible = true;
                rec.selectedThreshold = settings.selectedThreshold;
                rec.lastRefreshTick = now;
                ApplyTierTransition(record, settlement, poolRef, rec, tech, settings);
                return;
            }

            float capacityMultiplier = SettlementStockService.CapacityMultiplier(settlement, poolRef.category);
            foreach (DynamicStockEntryRecord entry in rec.entries)
                entry.currentAmount = Mathf.Min(entry.currentAmount, SettlementStockService.EffectiveDynamicCapacity(entry.baseCapacity, capacityMultiplier));

            if (now - rec.lastRefreshTick >= settings.refreshIntervalTicks)
                Refresh(record, settlement, poolRef, rec, tech, settings, capacityMultiplier, now);
        }

        private static void ReconcileEntries(SettlementRecord record, DynamicStockPoolRecord rec, SettlementDynamicStockPoolReference poolRef, TechLevel tech)
        {
            foreach (DynamicStockEntryRecord entry in rec.entries.ToList())
            {
                ThingDef thing = DefDatabase<ThingDef>.GetNamedSilentFail(entry.thingDefName);
                bool valid = !entry.retired
                    && thing != null
                    && poolRef.candidateSet.Contains(thing)
                    && SettlementDynamicStockCatalog.IsSelectableAtTech(thing, tech);
                if (!valid) RetireOrRemove(record, rec, entry);
            }
        }

        private static void RetireOrRemove(SettlementRecord record, DynamicStockPoolRecord rec, DynamicStockEntryRecord entry)
        {
            if (HasDynamicReservation(record, entry.thingDefName)) entry.retired = true;
            else rec.entries.Remove(entry);
        }

        private static void ApplyTierTransition(SettlementRecord record, Settlement settlement, SettlementDynamicStockPoolReference poolRef, DynamicStockPoolRecord rec, TechLevel tech, SettlementDynamicStockEffectiveSettings settings)
        {
            float capacityMultiplier = SettlementStockService.CapacityMultiplier(settlement, poolRef.category);

            List<DynamicStockEntryRecord> active = rec.entries.Where(e => !e.retired).ToList();
            foreach (DynamicStockEntryRecord entry in active)
            {
                entry.baseCapacity = Rand.RangeInclusive(settings.minCapacity, settings.maxCapacity);
                entry.currentAmount = Mathf.Min(entry.currentAmount, SettlementStockService.EffectiveDynamicCapacity(entry.baseCapacity, capacityMultiplier));
            }

            int excess = active.Count - settings.maxDifferentGoods;
            if (excess > 0)
            {
                List<DynamicStockEntryRecord> unreserved = active.Where(e => !HasDynamicReservation(record, e.thingDefName)).InRandomOrder().ToList();
                List<DynamicStockEntryRecord> reserved = active.Where(e => HasDynamicReservation(record, e.thingDefName)).InRandomOrder().ToList();
                foreach (DynamicStockEntryRecord entry in unreserved.Concat(reserved))
                {
                    if (excess <= 0) break;
                    RetireOrRemove(record, rec, entry);
                    excess--;
                }
            }

            int activeCount = rec.entries.Count(e => !e.retired);
            if (activeCount < settings.minDifferentGoods)
                AddEntries(record, settlement, poolRef, rec, tech, settings, Rand.RangeInclusive(settings.minDifferentGoods, settings.maxDifferentGoods) - activeCount, null);
        }

        private static void Refresh(SettlementRecord record, Settlement settlement, SettlementDynamicStockPoolReference poolRef, DynamicStockPoolRecord rec, TechLevel tech, SettlementDynamicStockEffectiveSettings settings, float capacityMultiplier, int now)
        {
            int intervals = (now - rec.lastRefreshTick) / settings.refreshIntervalTicks;
            rec.lastRefreshTick += intervals * settings.refreshIntervalTicks;

            HashSet<string> occupied = OccupiedNames(record);
            int outsiders = poolRef.candidates.Count(t => !occupied.Contains(t.defName) && SettlementDynamicStockCatalog.IsSelectableAtTech(t, tech));

            List<DynamicStockEntryRecord> removable = rec.entries
                .Where(e => !e.retired && !HasDynamicReservation(record, e.thingDefName))
                .InRandomOrder()
                .ToList();
            int removeCount = Mathf.Min(settings.replaceCount, Mathf.Min(removable.Count, outsiders));

            var removedNames = new HashSet<string>();
            for (int i = 0; i < removeCount; i++)
            {
                removedNames.Add(removable[i].thingDefName);
                rec.entries.Remove(removable[i]);
            }

            float refreshMultiplier = SettlementStockService.RefreshMultiplier(settlement, poolRef.category);
            foreach (DynamicStockEntryRecord entry in rec.entries)
            {
                if (entry.retired) continue;
                int amount = SettlementStockService.ToQuantity(Rand.RangeInclusive(settings.minRefreshAmount, settings.maxRefreshAmount) * refreshMultiplier);
                entry.currentAmount = Mathf.Min(SettlementStockService.EffectiveDynamicCapacity(entry.baseCapacity, capacityMultiplier), SettlementStockService.AddQuantities(entry.currentAmount, amount));
            }

            int survivors = rec.entries.Count(e => !e.retired);
            int target = Rand.RangeInclusive(settings.minDifferentGoods, settings.maxDifferentGoods);
            if (target > survivors)
                AddEntries(record, settlement, poolRef, rec, tech, settings, target - survivors, removedNames);
        }

        private static void AddEntries(SettlementRecord record, Settlement settlement, SettlementDynamicStockPoolReference poolRef, DynamicStockPoolRecord rec, TechLevel tech, SettlementDynamicStockEffectiveSettings settings, int count, HashSet<string> avoidNames)
        {
            if (count <= 0) return;

            HashSet<string> occupied = OccupiedNames(record);
            var preferred = new List<ThingDef>();
            var fallback = new List<ThingDef>();
            foreach (ThingDef thing in poolRef.candidates)
            {
                if (occupied.Contains(thing.defName) || !SettlementDynamicStockCatalog.IsSelectableAtTech(thing, tech)) continue;
                if (avoidNames != null && avoidNames.Contains(thing.defName)) fallback.Add(thing);
                else preferred.Add(thing);
            }

            float capacityMultiplier = SettlementStockService.CapacityMultiplier(settlement, poolRef.category);
            int added = 0;
            foreach (ThingDef thing in preferred.InRandomOrder().Concat(fallback.InRandomOrder()))
            {
                if (added >= count) break;

                int baseCapacity = Rand.RangeInclusive(settings.minCapacity, settings.maxCapacity);
                rec.entries.Add(new DynamicStockEntryRecord
                {
                    thingDefName = thing.defName,
                    baseCapacity = baseCapacity,
                    currentAmount = SettlementStockService.EffectiveDynamicCapacity(baseCapacity, capacityMultiplier),
                });
                added++;
            }

            int activeCount = rec.entries.Count(e => !e.retired);
            if (added < count && activeCount < settings.minDifferentGoods)
                Settlement_Services.SupportLog.Warning($"Dynamic stock pool {poolRef.key} has fewer eligible ThingDefs than minDifferentGoods {settings.minDifferentGoods} for faction tech {tech}; it stocks all {activeCount} available.");
        }

        private static HashSet<string> OccupiedNames(SettlementRecord record)
        {
            var names = new HashSet<string>();
            foreach (DynamicStockPoolRecord pool in record.dynamicStockPools)
                foreach (DynamicStockEntryRecord entry in pool.entries)
                    names.Add(entry.thingDefName);
            return names;
        }
    }
}

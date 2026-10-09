using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Framework.Stock
{
    public static class SettlementDynamicStockCatalog
    {
        private static List<SettlementDynamicStockPoolReference> allPools;
        private static Dictionary<string, SettlementDynamicStockPoolReference> byKey;
        private static Dictionary<SettlementStockCategoryDef, List<SettlementDynamicStockPoolReference>> byCategory;

        public static string KeyFor(SettlementStockCategoryDef category, SettlementDynamicStockPool pool) => category.defName + "/" + pool.id;

        public static IReadOnlyList<SettlementDynamicStockPoolReference> AllPools()
        {
            EnsureBuilt();
            return allPools;
        }

        public static SettlementDynamicStockPoolReference PoolFor(string key)
        {
            EnsureBuilt();
            return key != null && byKey.TryGetValue(key, out SettlementDynamicStockPoolReference reference) ? reference : null;
        }

        public static IReadOnlyList<SettlementDynamicStockPoolReference> PoolsFor(SettlementStockCategoryDef category)
        {
            EnsureBuilt();
            return category != null && byCategory.TryGetValue(category, out List<SettlementDynamicStockPoolReference> refs)
                ? refs
                : (IReadOnlyList<SettlementDynamicStockPoolReference>)Array.Empty<SettlementDynamicStockPoolReference>();
        }

        public static IEnumerable<ThingDef> CandidatesFor(SettlementStockCategoryDef category) =>
            PoolsFor(category).SelectMany(p => p.candidates).Distinct();

        public static bool IsSelectableAtTech(ThingDef thing, TechLevel factionTechLevel) =>
            thing.techLevel == TechLevel.Undefined || (int)thing.techLevel <= (int)factionTechLevel;

        private static void EnsureBuilt()
        {
            if (allPools != null) return;

            allPools = new List<SettlementDynamicStockPoolReference>();
            byKey = new Dictionary<string, SettlementDynamicStockPoolReference>();
            byCategory = new Dictionary<SettlementStockCategoryDef, List<SettlementDynamicStockPoolReference>>();

            foreach (SettlementStockCategoryDef category in DefDatabase<SettlementStockCategoryDef>.AllDefsListForReading.OrderBy(c => c.defName, StringComparer.Ordinal))
            {
                if (category.dynamicStockPools.NullOrEmpty()) continue;

                var refs = new List<SettlementDynamicStockPoolReference>();
                byCategory[category] = refs;

                foreach (SettlementDynamicStockPool pool in category.dynamicStockPools.Where(p => p != null).OrderBy(p => p.id, StringComparer.Ordinal))
                {
                    string owner = $"{category.defName} dynamic stock pool {pool.id}";
                    List<string> settingsErrors = pool.SettingsErrors(owner).ToList();
                    if (settingsErrors.Count > 0)
                    {
                        if (!Prefs.DevMode)
                            foreach (string error in settingsErrors) Settlement_Services.SupportLog.Error($"{error} The pool is skipped.");
                        continue;
                    }

                    string key = KeyFor(category, pool);
                    if (byKey.ContainsKey(key))
                    {
                        if (!Prefs.DevMode)
                            Settlement_Services.SupportLog.Error($"{category.defName} has a duplicate dynamic stock pool id {pool.id}; ignoring the later entry.");
                        continue;
                    }

                    if (!Prefs.DevMode)
                        foreach (string error in pool.SourceErrors(owner)) Settlement_Services.SupportLog.Error(error);

                    List<ThingDef> candidates = ResolveCandidates(pool);
                    if (candidates.Count == 0)
                        Settlement_Services.SupportLog.Warning($"{owner} resolves no eligible ThingDefs; it will stock nothing.");

                    var reference = new SettlementDynamicStockPoolReference(category, pool, key, candidates);
                    byKey[key] = reference;
                    allPools.Add(reference);
                    refs.Add(reference);
                }
            }
        }

        private static List<ThingDef> ResolveCandidates(SettlementDynamicStockPool pool)
        {
            var seen = new HashSet<ThingDef>();
            var candidates = new List<ThingDef>();

            if (!pool.thingCategoryDefName.NullOrEmpty())
            {
                ThingCategoryDef thingCategory = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(pool.thingCategoryDefName);
                if (thingCategory != null)
                    foreach (ThingDef thing in thingCategory.DescendantThingDefs)
                        if (seen.Add(thing) && IsCandidate(pool, thing)) candidates.Add(thing);
            }

            if (!pool.thingDefNames.NullOrEmpty())
                foreach (string name in pool.thingDefNames)
                {
                    ThingDef thing = name.NullOrEmpty() ? null : DefDatabase<ThingDef>.GetNamedSilentFail(name);
                    if (thing != null && seen.Add(thing) && IsCandidate(pool, thing)) candidates.Add(thing);
                }

            candidates.Sort((a, b) => string.CompareOrdinal(a.defName, b.defName));
            return candidates;
        }

        private static bool IsCandidate(SettlementDynamicStockPool pool, ThingDef thing)
        {
            if (thing.category != ThingCategory.Item) return false;
            if (!pool.excludeThingDefNames.NullOrEmpty() && pool.excludeThingDefNames.Contains(thing.defName)) return false;
            if (!thing.tradeability.TraderCanSell() && !(pool.allowPlayerSellOnlyItems && thing.tradeability == Tradeability.Sellable)) return false;
            if (thing.BaseMarketValue <= 0f) return false;
            if (SettlementStockCatalog.ItemFor(thing) != null) return false;

            if (pool.requireHumanEdible && (thing.ingestible == null || !thing.ingestible.HumanEdible)) return false;
            if (pool.requireNutritionGiving && !thing.IsNutritionGivingIngestible) return false;
            if (pool.excludeDrugs && thing.IsDrug) return false;
            return true;
        }
    }
}

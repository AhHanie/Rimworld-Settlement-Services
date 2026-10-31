using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Services.Construction;
using Settlement_Services.Services.Crafting;

namespace Settlement_Services.UI
{
    internal static class CommissionOrigin
    {
        public static bool IsOfficial(Def def) => def?.modContentPack?.IsOfficialMod == true;

        public static bool IsOfficial(CraftingCommissionRecipe recipe)
        {
            if (recipe == null || recipe.Kind != CraftingRecipeKind.Vanilla) return false;
            if (!IsOfficial(recipe.VanillaRecipeDef) || !IsOfficial(recipe.primaryOutput)) return false;
            return recipe.products.All(p => IsOfficial(p.thingDef));
        }
    }

    internal sealed class CommissionAvailability
    {
        private readonly ServiceRequestSession session;
        private readonly Settlement settlement;
        private readonly List<ThingDefCountClass> potentialInputs;
        private readonly Dictionary<CraftingCommissionRecipe, List<ThingDef>> craftingStuffs = new Dictionary<CraftingCommissionRecipe, List<ThingDef>>();
        private readonly Dictionary<ThingDef, List<ThingDef>> buildingStuffs = new Dictionary<ThingDef, List<ThingDef>>();
        private readonly Dictionary<(object, ThingDef, int), bool> cache = new Dictionary<(object, ThingDef, int), bool>();

        public CommissionAvailability(ServiceRequestSession session)
        {
            this.session = session;
            settlement = session.settlement;
            potentialInputs = new List<ThingDefCountClass>();
            if (session.caravan == null) return;

            var totals = new Dictionary<ThingDef, int>();
            foreach (Thing thing in CaravanInventoryUtility.AllInventoryItems(session.caravan))
            {
                totals.TryGetValue(thing.def, out int existing);
                totals[thing.def] = existing + thing.stackCount;
            }
            foreach (KeyValuePair<ThingDef, int> kv in totals) potentialInputs.Add(new ThingDefCountClass(kv.Key, kv.Value));
        }

        public List<ThingDef> CraftingStuffs(CraftingCommissionRecipe recipe)
        {
            if (!craftingStuffs.TryGetValue(recipe, out List<ThingDef> stuffs))
            {
                if (recipe.Kind == CraftingRecipeKind.Vanilla && recipe.madeFromStuff)
                {
                    TechLevel techCeiling = CraftingCommissionCatalog.EffectiveTechCeiling(recipe, settlement);
                    stuffs = CraftingMaterialPlanner.EligibleStuffs(recipe.primaryOutput, techCeiling, settlement).ToList();
                }
                else
                {
                    stuffs = new List<ThingDef> { null };
                }
                craftingStuffs[recipe] = stuffs;
            }
            return stuffs;
        }

        public List<ThingDef> BuildingStuffs(ThingDef building)
        {
            if (!buildingStuffs.TryGetValue(building, out List<ThingDef> stuffs))
            {
                stuffs = building.MadeFromStuff
                    ? BuildingCommissionCatalog.EligibleStuffs(building, settlement).ToList()
                    : new List<ThingDef> { null };
                buildingStuffs[building] = stuffs;
            }
            return stuffs;
        }

        public bool CraftingPossible(CraftingCommissionRecipe recipe, ThingDef stuff, int quantity)
        {
            var key = ((object)recipe, stuff, quantity);
            if (cache.TryGetValue(key, out bool cached)) return cached;
            bool result = ComputeCrafting(recipe, stuff, quantity);
            cache[key] = result;
            return result;
        }

        public bool CraftingPossibleWithAnyStuff(CraftingCommissionRecipe recipe, int quantity) =>
            CraftingStuffs(recipe).Any(s => CraftingPossible(recipe, s, quantity));

        public bool BuildingPossible(ThingDef building, ThingDef stuff, int quantity)
        {
            var key = ((object)building, stuff, quantity);
            if (cache.TryGetValue(key, out bool cached)) return cached;
            bool result = ComputeBuilding(building, stuff, quantity);
            cache[key] = result;
            return result;
        }

        public bool BuildingPossibleWithAnyStuff(ThingDef building, int quantity) =>
            BuildingStuffs(building).Any(s => BuildingPossible(building, s, quantity));

        private bool ComputeCrafting(CraftingCommissionRecipe recipe, ThingDef stuff, int quantity)
        {
            var lines = session.craftingCommissionLines.Select(l => l.Clone()).ToList();
            string stuffName = stuff?.defName;
            CraftingCommissionLine existing = lines.Find(l => l.recipeKind == recipe.identity.kind && l.recipeDefName == recipe.identity.defName && l.stuffDefName == stuffName);
            if (existing != null) existing.count += quantity;
            else lines.Add(new CraftingCommissionLine(recipe.identity.kind, recipe.identity.defName, quantity, stuffName));

            if (!CraftingProductionPlanner.TryPlan(settlement, lines, potentialInputs, session.craftingCrafterCount, out CraftingProductionPlan plan, out _))
                return false;
            return SettlementStockService.TryAllocate(settlement, CraftingStockRequirements.ToStockRequirements(plan), potentialInputs).Success;
        }

        private bool ComputeBuilding(ThingDef building, ThingDef stuff, int quantity)
        {
            var lines = session.buildingCommissionLines.Select(l => l.Clone()).ToList();
            string stuffName = stuff?.defName;
            BuildingCommissionLine existing = lines.Find(l => l.buildingDefName == building.defName && l.stuffDefName == stuffName);
            if (existing != null) existing.count += quantity;
            else lines.Add(new BuildingCommissionLine(building.defName, quantity, stuffName));

            if (!BuildingCommissionPlanner.TryPlan(settlement, lines, out BuildingCommissionPlan plan, out _))
                return false;
            return SettlementStockService.TryAllocate(settlement, BuildingCommissionPlanner.ToStockRequirements(plan), potentialInputs).Success;
        }
    }

    internal sealed class CommissionRowScan<T>
    {
        private readonly List<T> candidates;
        private readonly Func<T, bool> predicate;
        private readonly List<T> visible = new List<T>();
        private int next;

        public CommissionRowScan(IEnumerable<T> baseline, Func<T, bool> predicate)
        {
            candidates = baseline.ToList();
            this.predicate = predicate;
            if (predicate == null)
            {
                visible.AddRange(candidates);
                next = candidates.Count;
            }
        }

        public List<T> Visible => visible;

        public bool Complete => next >= candidates.Count;

        public void Step(double budgetMilliseconds)
        {
            if (Complete) return;

            var watch = Stopwatch.StartNew();
            while (next < candidates.Count)
            {
                T candidate = candidates[next++];
                if (predicate(candidate)) visible.Add(candidate);
                if (watch.Elapsed.TotalMilliseconds >= budgetMilliseconds) break;
            }
        }
    }
}

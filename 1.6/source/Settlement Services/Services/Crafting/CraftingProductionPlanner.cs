using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Stock;

namespace Settlement_Services.Services.Crafting
{
    internal static class CraftingProductionPlanner
    {
        private const int MaxProducerDepth = 6;

        private class BranchOutcome
        {
            public readonly List<CraftingProductionOperation> operations = new List<CraftingProductionOperation>();
            public readonly Dictionary<ThingDef, int> rawLeaves = new Dictionary<ThingDef, int>();
            public Dictionary<ThingDef, int> ledger;
            public float workTicks;

            public float RawValue => rawLeaves.Sum(kv => kv.Key.BaseMarketValue * kv.Value);
        }

        public static bool TryPlan(Settlement settlement, List<CraftingCommissionLine> lines, List<ThingDefCountClass> playerInputs, int crafterCount, out CraftingProductionPlan plan, out string errorKey) =>
            TryPlan(settlement, lines, playerInputs, crafterCount, out plan, out errorKey, out _);

        public static bool TryPlan(Settlement settlement, List<CraftingCommissionLine> lines, List<ThingDefCountClass> playerInputs, int crafterCount,
            out CraftingProductionPlan plan, out string errorKey, out CraftingMaterialRequirement blockingRequirement)
        {
            plan = null;
            errorKey = null;
            blockingRequirement = null;

            if (lines.NullOrEmpty())
            {
                errorKey = "SettlementServices.Error.NoCommissionItemsSelected";
                return false;
            }

            var ledger = new Dictionary<ThingDef, int>();
            var operations = new List<CraftingProductionOperation>();
            var rawLeaves = new Dictionary<ThingDef, int>();
            float workTicks = 0f;
            CraftingRecipeReachability reachability = CraftingCommissionCatalog.ReachabilityFor(settlement);

            foreach (CraftingCommissionLine line in lines)
            {
                if (line.count <= 0)
                {
                    errorKey = "SettlementServices.Error.InvalidQuantity";
                    return false;
                }

                CraftingRecipeIdentity identity = line.Identity;
                RecipeNode node = CraftingRecipeDependencyIndex.NodeFor(identity);
                if (node == null || !reachability.reachableRecipes.Contains(identity) || !CraftingCommissionCatalog.IsAllowedAtSettlement(node.recipe, settlement))
                {
                    errorKey = "SettlementServices.Error.CommissionedItemNoLongerAvailable";
                    return false;
                }

                CraftingCommissionRecipe recipe = node.recipe;

                ThingDef stuff = null;
                if (recipe.Kind == CraftingRecipeKind.Vanilla)
                {
                    if (recipe.madeFromStuff)
                    {
                        TechLevel techCeiling = CraftingCommissionCatalog.EffectiveTechCeiling(recipe, settlement);
                        stuff = line.stuffDefName != null ? DefDatabase<ThingDef>.GetNamedSilentFail(line.stuffDefName) : null;
                        if (stuff == null || !CraftingMaterialPlanner.EligibleStuffs(recipe.primaryOutput, techCeiling, settlement).Contains(stuff))
                        {
                            errorKey = "SettlementServices.Error.CommissionedItemNoLongerAvailable";
                            return false;
                        }
                    }
                }
                else if (line.stuffDefName != null)
                {
                    errorKey = "SettlementServices.Error.CommissionedItemNoLongerAvailable";
                    return false;
                }

                bool stuffAssigned = false;
                foreach (RecipeIngredientSlot slot in node.ingredientSlots)
                {
                    var activeProducers = new HashSet<CraftingRecipeIdentity> { identity };

                    if (!stuffAssigned && stuff != null && slot.IsStuffCandidate(stuff))
                    {
                        int need = slot.RequiredCountFor(stuff) * line.count;
                        if (!TryResolveDemand(stuff, need, settlement, playerInputs, ledger, activeProducers, 0, operations, rawLeaves, ref workTicks, out errorKey, out blockingRequirement))
                            return false;
                        stuffAssigned = true;
                        continue;
                    }

                    if (!TryResolveIngredientSlot(slot, line.count, settlement, playerInputs, ledger, activeProducers, 0, operations, rawLeaves, ref workTicks, out errorKey, out blockingRequirement))
                        return false;
                }

                operations.Add(new CraftingProductionOperation(identity.kind, identity.defName, stuff?.defName, line.count, true));
                workTicks += recipe.WorkAmountFor(stuff) * line.count;
            }

            if (!CraftingCommissionWorkloadPlanner.TryPlan(operations, crafterCount, out CraftingCommissionWorkloadPlanner.CraftingWorkloadResult staffing, out errorKey))
            {
                plan = null;
                return false;
            }

            plan = new CraftingProductionPlan
            {
                operations = operations,
                rawMaterials = rawLeaves
                    .Select(kv => new CraftingMaterialRequirement(kv.Key.defName, kv.Value, true))
                    .OrderBy(m => m.thingDefName, StringComparer.Ordinal)
                    .ToList(),
                totalRecipeExecutions = Mathf.Max(1, operations.Sum(o => o.executions)),
                totalWorkTicks = staffing.totalWorkTicks,
                crafterCount = staffing.crafterCount,
                assignedCrafterWorkTicks = staffing.assignedWorkTicks,
                paidCrafterDays = staffing.paidCrafterDays,
                billedCrafterDays = staffing.billedCrafterDays,
                longestCrafterDurationTicks = staffing.longestCrafterDurationTicks,
            };
            return true;
        }

        private static bool TryResolveDemand(ThingDef thing, int amount, Settlement settlement, List<ThingDefCountClass> playerInputs,
            Dictionary<ThingDef, int> ledger, HashSet<CraftingRecipeIdentity> activeProducers, int depth,
            List<CraftingProductionOperation> operations, Dictionary<ThingDef, int> rawLeaves, ref float workTicks, out string errorKey, out CraftingMaterialRequirement blockingRequirement)
        {
            errorKey = null;
            blockingRequirement = null;
            if (amount <= 0) return true;

            int available = GetAvailable(ledger, settlement, playerInputs, thing);
            int fromStock = Mathf.Min(available, amount);
            if (fromStock > 0)
            {
                ledger[thing] = available - fromStock;
                AddRaw(rawLeaves, thing, fromStock);
                amount -= fromStock;
            }
            if (amount <= 0) return true;

            if (depth >= MaxProducerDepth)
            {
                errorKey = "SettlementServices.Error.NoNestedCraftingPath";
                blockingRequirement = new CraftingMaterialRequirement(thing.defName, amount, true);
                return false;
            }

            List<RecipeNode> producers = CraftingRecipeDependencyIndex.ProducersOf(thing)
                .Where(n => !activeProducers.Contains(n.recipe.identity) && CraftingCommissionCatalog.CanBeNestedProducer(n.recipe, settlement))
                .OrderBy(n => n.recipe.identity.kind)
                .ThenBy(n => n.recipe.identity.defName, StringComparer.Ordinal)
                .ToList();

            if (producers.Count == 0)
            {
                errorKey = "SettlementServices.Error.NoNestedCraftingPath";
                blockingRequirement = new CraftingMaterialRequirement(thing.defName, amount, true);
                return false;
            }

            var branches = new List<(RecipeNode node, BranchOutcome outcome)>();
            string lastError = "SettlementServices.Error.NoNestedCraftingPath";
            CraftingMaterialRequirement lastBlocking = new CraftingMaterialRequirement(thing.defName, amount, true);

            foreach (RecipeNode node in producers)
            {
                var branchLedger = new Dictionary<ThingDef, int>(ledger);
                var branchOperations = new List<CraftingProductionOperation>();
                var branchRawLeaves = new Dictionary<ThingDef, int>();
                var branchActive = new HashSet<CraftingRecipeIdentity>(activeProducers) { node.recipe.identity };
                float branchWork = 0f;

                int executions = amount;
                bool ok = true;
                foreach (RecipeIngredientSlot slot in node.ingredientSlots)
                {
                    if (!TryResolveIngredientSlot(slot, executions, settlement, playerInputs, branchLedger, branchActive, depth + 1, branchOperations, branchRawLeaves, ref branchWork, out string ingredientError, out CraftingMaterialRequirement ingredientBlocking))
                    {
                        ok = false;
                        lastError = ingredientError;
                        lastBlocking = ingredientBlocking;
                        break;
                    }
                }
                if (!ok) continue;

                branchWork += node.recipe.WorkAmountFor(null) * executions;
                branchOperations.Add(new CraftingProductionOperation(node.recipe.identity.kind, node.recipe.identity.defName, null, executions, false));

                var outcome = new BranchOutcome { ledger = branchLedger, workTicks = branchWork };
                outcome.operations.AddRange(branchOperations);
                foreach (KeyValuePair<ThingDef, int> kv in branchRawLeaves) outcome.rawLeaves[kv.Key] = kv.Value;

                branches.Add((node, outcome));
            }

            if (branches.Count == 0)
            {
                errorKey = lastError;
                blockingRequirement = lastBlocking;
                return false;
            }

            (RecipeNode node, BranchOutcome outcome) best = branches
                .OrderBy(b => b.outcome.RawValue)
                .ThenBy(b => b.outcome.workTicks)
                .ThenBy(b => b.node.recipe.identity.kind)
                .ThenBy(b => b.node.recipe.identity.defName, StringComparer.Ordinal)
                .First();

            operations.AddRange(best.outcome.operations);
            foreach (KeyValuePair<ThingDef, int> kv in best.outcome.rawLeaves) AddRaw(rawLeaves, kv.Key, kv.Value);
            workTicks += best.outcome.workTicks;

            ledger.Clear();
            foreach (KeyValuePair<ThingDef, int> kv in best.outcome.ledger) ledger[kv.Key] = kv.Value;

            return true;
        }

        private static bool TryResolveIngredientSlot(RecipeIngredientSlot slot, int count,
            Settlement settlement, List<ThingDefCountClass> playerInputs, Dictionary<ThingDef, int> ledger, HashSet<CraftingRecipeIdentity> activeProducers, int depth,
            List<CraftingProductionOperation> operations, Dictionary<ThingDef, int> rawLeaves, ref float workTicks, out string errorKey, out CraftingMaterialRequirement blockingRequirement)
        {
            errorKey = "SettlementServices.Error.NoNestedCraftingPath";
            blockingRequirement = null;

            List<ThingDef> candidates = slot.candidates
                .OrderByDescending(t => GetAvailable(ledger, settlement, playerInputs, t))
                .ThenBy(t => t.BaseMarketValue)
                .ThenBy(t => t.defName, StringComparer.Ordinal)
                .ToList();

            foreach (ThingDef candidate in candidates)
            {
                var trialLedger = new Dictionary<ThingDef, int>(ledger);
                var trialOperations = new List<CraftingProductionOperation>();
                var trialRawLeaves = new Dictionary<ThingDef, int>();
                float trialWork = 0f;

                int need = slot.RequiredCountFor(candidate) * count;
                if (!TryResolveDemand(candidate, need, settlement, playerInputs, trialLedger, activeProducers, depth, trialOperations, trialRawLeaves, ref trialWork, out errorKey, out blockingRequirement))
                    continue;

                ledger.Clear();
                foreach (KeyValuePair<ThingDef, int> kv in trialLedger) ledger[kv.Key] = kv.Value;
                operations.AddRange(trialOperations);
                foreach (KeyValuePair<ThingDef, int> kv in trialRawLeaves) AddRaw(rawLeaves, kv.Key, kv.Value);
                workTicks += trialWork;
                errorKey = null;
                blockingRequirement = null;
                return true;
            }
            return false;
        }

        private static int GetAvailable(Dictionary<ThingDef, int> ledger, Settlement settlement, List<ThingDefCountClass> playerInputs, ThingDef thing)
        {
            if (!ledger.TryGetValue(thing, out int amount))
            {
                int playerAmount = playerInputs?.Where(i => i.thingDef == thing).Sum(i => i.count) ?? 0;
                amount = playerAmount + SettlementStockService.GetAvailableStock(settlement, thing);
                ledger[thing] = amount;
            }
            return amount;
        }

        private static void AddRaw(Dictionary<ThingDef, int> rawLeaves, ThingDef thing, int amount)
        {
            rawLeaves.TryGetValue(thing, out int existing);
            rawLeaves[thing] = existing + amount;
        }
    }
}

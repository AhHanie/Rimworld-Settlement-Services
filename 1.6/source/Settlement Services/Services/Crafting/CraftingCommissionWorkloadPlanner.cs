using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.Services.Crafting
{
    internal static class CraftingCommissionWorkloadPlanner
    {
        private const int WorkTicksPerPaidDay = 48000;
        private const int RestTicksPerPaidDay = 12000;

        public class CraftingWorkloadResult
        {
            public int crafterCount;
            public List<int> assignedWorkTicks;
            public int paidCrafterDays;
            public int billedCrafterDays;
            public int longestCrafterDurationTicks;
            public int totalWorkTicks;
        }

        private class WorkUnit
        {
            public CraftingRecipeKind recipeKind;
            public string recipeDefName;
            public string stuffDefName;
            public int workTicks;
        }

        public static bool TryPlan(List<CraftingProductionOperation> operations, int crafterCount, out CraftingWorkloadResult result, out string errorKey)
        {
            result = null;
            errorKey = null;

            if (crafterCount <= 0)
            {
                errorKey = "SettlementServices.Error.InvalidCrafterCount";
                return false;
            }

            if (!TryExpandUnits(operations, out List<WorkUnit> units, out errorKey)) return false;

            if (units.Count > 0 && crafterCount > units.Count)
            {
                errorKey = "SettlementServices.Error.TooManyCrafters";
                return false;
            }

            var loads = new int[crafterCount];
            foreach (WorkUnit unit in units
                         .OrderByDescending(u => u.workTicks)
                         .ThenBy(u => u.recipeKind)
                         .ThenBy(u => u.recipeDefName, StringComparer.Ordinal)
                         .ThenBy(u => u.stuffDefName, StringComparer.Ordinal))
            {
                int index = IndexOfLeastLoaded(loads);
                loads[index] += unit.workTicks;
            }

            int paidDays = 0;
            int longestDuration = 0;
            foreach (int load in loads)
            {
                if (load <= 0) continue;
                paidDays += PaidDaysFor(load);
                int duration = load + (load / WorkTicksPerPaidDay) * RestTicksPerPaidDay;
                if (duration > longestDuration) longestDuration = duration;
            }
            paidDays = Mathf.Max(1, paidDays);

            int totalWorkTicks = units.Sum(u => u.workTicks);
            int singleCrafterPaidDays = totalWorkTicks > 0 ? PaidDaysFor(totalWorkTicks) : 1;
            int minimumParallelStaffingDays = singleCrafterPaidDays + (crafterCount - 1);

            result = new CraftingWorkloadResult
            {
                crafterCount = crafterCount,
                assignedWorkTicks = loads.ToList(),
                paidCrafterDays = paidDays,
                billedCrafterDays = Mathf.Max(paidDays, minimumParallelStaffingDays),
                longestCrafterDurationTicks = longestDuration,
                totalWorkTicks = totalWorkTicks,
            };
            return true;
        }

        private static int PaidDaysFor(int workTicks) => Mathf.CeilToInt(workTicks / (float)WorkTicksPerPaidDay);

        private static int IndexOfLeastLoaded(int[] loads)
        {
            int best = 0;
            for (int i = 1; i < loads.Length; i++)
                if (loads[i] < loads[best]) best = i;
            return best;
        }

        private static bool TryExpandUnits(List<CraftingProductionOperation> operations, out List<WorkUnit> units, out string errorKey)
        {
            units = new List<WorkUnit>();
            errorKey = null;

            foreach (CraftingProductionOperation op in operations)
            {
                RecipeNode node = CraftingRecipeDependencyIndex.NodeFor(op.Identity);
                if (node == null)
                {
                    units = null;
                    errorKey = "SettlementServices.Error.CommissionedItemNoLongerAvailable";
                    return false;
                }

                ThingDef stuff = op.stuffDefName != null ? DefDatabase<ThingDef>.GetNamedSilentFail(op.stuffDefName) : null;
                int perExecutionTicks = Mathf.CeilToInt(node.recipe.WorkAmountFor(stuff));
                string stuffKey = op.stuffDefName ?? string.Empty;

                for (int i = 0; i < op.executions; i++)
                    units.Add(new WorkUnit { recipeKind = op.recipeKind, recipeDefName = op.recipeDefName, stuffDefName = stuffKey, workTicks = perExecutionTicks });
            }
            return true;
        }
    }
}

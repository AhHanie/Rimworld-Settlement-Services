using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Settlement_Services.Framework.Dto
{
    public class CraftingProductionPlan : IExposable
    {
        private const int WorkTicksPerPaidDay = 48000;
        private const int RestTicksPerPaidDay = 12000;

        public List<CraftingProductionOperation> operations = new List<CraftingProductionOperation>();
        public List<CraftingMaterialRequirement> rawMaterials = new List<CraftingMaterialRequirement>();
        public int totalRecipeExecutions;
        public int totalWorkTicks;

        public int crafterCount = 1;
        public List<int> assignedCrafterWorkTicks = new List<int>();
        public int paidCrafterDays = 1;
        public int billedCrafterDays = 1;
        public int longestCrafterDurationTicks;

        public CraftingProductionPlan Clone() => new CraftingProductionPlan
        {
            operations = operations.Select(o => o.Clone()).ToList(),
            rawMaterials = rawMaterials.Select(m => m.Clone()).ToList(),
            totalRecipeExecutions = totalRecipeExecutions,
            totalWorkTicks = totalWorkTicks,
            crafterCount = crafterCount,
            assignedCrafterWorkTicks = new List<int>(assignedCrafterWorkTicks),
            paidCrafterDays = paidCrafterDays,
            billedCrafterDays = billedCrafterDays,
            longestCrafterDurationTicks = longestCrafterDurationTicks,
        };

        public void ExposeData()
        {
            Scribe_Collections.Look(ref operations, "operations", LookMode.Deep);
            Scribe_Collections.Look(ref rawMaterials, "rawMaterials", LookMode.Deep);
            Scribe_Values.Look(ref totalRecipeExecutions, "totalRecipeExecutions");
            Scribe_Values.Look(ref totalWorkTicks, "totalWorkTicks");
            Scribe_Values.Look(ref crafterCount, "crafterCount");
            Scribe_Collections.Look(ref assignedCrafterWorkTicks, "assignedCrafterWorkTicks", LookMode.Value);
            Scribe_Values.Look(ref paidCrafterDays, "paidCrafterDays");
            Scribe_Values.Look(ref billedCrafterDays, "billedCrafterDays");
            Scribe_Values.Look(ref longestCrafterDurationTicks, "longestCrafterDurationTicks");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (operations == null) operations = new List<CraftingProductionOperation>();
                if (rawMaterials == null) rawMaterials = new List<CraftingMaterialRequirement>();

                if (crafterCount <= 0)
                {
                    crafterCount = 1;
                    paidCrafterDays = Mathf.Max(1, Mathf.CeilToInt(totalWorkTicks / (float)WorkTicksPerPaidDay));
                    longestCrafterDurationTicks = totalWorkTicks + (totalWorkTicks / WorkTicksPerPaidDay) * RestTicksPerPaidDay;
                    assignedCrafterWorkTicks = new List<int> { totalWorkTicks };
                }
                else if (assignedCrafterWorkTicks == null)
                {
                    assignedCrafterWorkTicks = new List<int> { totalWorkTicks };
                }

                if (billedCrafterDays <= 0) billedCrafterDays = Mathf.Max(1, paidCrafterDays);
            }
        }
    }
}

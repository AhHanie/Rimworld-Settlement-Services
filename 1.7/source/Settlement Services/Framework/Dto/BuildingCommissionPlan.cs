using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Settlement_Services.Framework.Dto
{
    public class BuildingCommissionPlan : IExposable
    {
        public List<BuildingCommissionLine> lines = new List<BuildingCommissionLine>();
        public List<CraftingMaterialRequirement> rawMaterials = new List<CraftingMaterialRequirement>();
        public int workTicks;

        public BuildingCommissionPlan Clone() => new BuildingCommissionPlan
        {
            lines = lines.Select(l => l.Clone()).ToList(),
            rawMaterials = rawMaterials.Select(m => m.Clone()).ToList(),
            workTicks = workTicks,
        };

        public void ExposeData()
        {
            Scribe_Collections.Look(ref lines, "lines", LookMode.Deep);
            Scribe_Collections.Look(ref rawMaterials, "rawMaterials", LookMode.Deep);
            Scribe_Values.Look(ref workTicks, "workTicks");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (lines == null) lines = new List<BuildingCommissionLine>();
                if (rawMaterials == null) rawMaterials = new List<CraftingMaterialRequirement>();
            }
        }
    }
}

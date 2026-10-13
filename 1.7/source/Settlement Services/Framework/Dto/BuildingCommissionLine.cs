using UnityEngine;
using Verse;

namespace Settlement_Services.Framework.Dto
{
    public class BuildingCommissionLine : IExposable
    {
        public string buildingDefName;
        public int count = 1;
        public string stuffDefName;

        public BuildingCommissionLine()
        {
        }

        public BuildingCommissionLine(string buildingDefName, int count, string stuffDefName)
        {
            this.buildingDefName = buildingDefName;
            this.count = count;
            this.stuffDefName = stuffDefName;
        }

        public BuildingCommissionLine Clone() => new BuildingCommissionLine(buildingDefName, count, stuffDefName);

        public void ExposeData()
        {
            Scribe_Values.Look(ref buildingDefName, "buildingDefName");
            Scribe_Values.Look(ref count, "count", 1);
            Scribe_Values.Look(ref stuffDefName, "stuffDefName");

            if (Scribe.mode == LoadSaveMode.PostLoadInit) count = Mathf.Max(1, count);
        }
    }
}

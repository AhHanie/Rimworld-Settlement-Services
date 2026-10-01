using Verse;

namespace Settlement_Services.Domain.Records
{
    public class DynamicStockEntryRecord : IExposable
    {
        public string thingDefName;
        public int baseCapacity;
        public int currentAmount;
        public bool retired;

        public void ExposeData()
        {
            Scribe_Values.Look(ref thingDefName, "thingDefName");
            Scribe_Values.Look(ref baseCapacity, "baseCapacity");
            Scribe_Values.Look(ref currentAmount, "currentAmount");
            Scribe_Values.Look(ref retired, "retired");
        }
    }
}

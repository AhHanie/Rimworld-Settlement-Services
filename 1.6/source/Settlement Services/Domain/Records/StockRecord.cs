using Verse;

namespace Settlement_Services.Domain.Records
{
    public class StockRecord : IExposable
    {
        public string stockThingDefName;
        public int currentAmount;
        public int lastRefreshTick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref stockThingDefName, "stockThingDefName");
            Scribe_Values.Look(ref currentAmount, "currentAmount");
            Scribe_Values.Look(ref lastRefreshTick, "lastRefreshTick");
        }
    }
}

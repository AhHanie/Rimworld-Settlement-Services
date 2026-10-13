using UnityEngine;
using Verse;

namespace Settlement_Services.Framework.Dto
{
    public class MarketCartLine : IExposable
    {
        public string thingDefName;
        public int count = 1;

        public MarketCartLine()
        {
        }

        public MarketCartLine(string thingDefName, int count)
        {
            this.thingDefName = thingDefName;
            this.count = count;
        }

        public MarketCartLine Clone() => new MarketCartLine(thingDefName, count);

        public void ExposeData()
        {
            Scribe_Values.Look(ref thingDefName, "thingDefName");
            Scribe_Values.Look(ref count, "count", 1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit) count = Mathf.Max(1, count);
        }
    }
}

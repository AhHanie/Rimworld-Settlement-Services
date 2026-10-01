using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Settlement_Services.Domain.Records
{
    public class DynamicStockPoolRecord : IExposable
    {
        public string poolKey;
        public TechLevel selectedThreshold = TechLevel.Undefined;
        public bool wasEligible;
        public int lastRefreshTick;
        public List<DynamicStockEntryRecord> entries = new List<DynamicStockEntryRecord>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref poolKey, "poolKey");
            Scribe_Values.Look(ref selectedThreshold, "selectedThreshold", TechLevel.Undefined);
            Scribe_Values.Look(ref wasEligible, "wasEligible");
            Scribe_Values.Look(ref lastRefreshTick, "lastRefreshTick");
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (entries == null) entries = new List<DynamicStockEntryRecord>();
                entries.RemoveAll(e => e == null || e.thingDefName.NullOrEmpty());
            }
        }
    }
}

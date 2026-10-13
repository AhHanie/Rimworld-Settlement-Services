using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Domain.Records
{
    public class BoardRewardRecord : IExposable
    {
        public List<Thing> items = new List<Thing>();
        public float marketValue;
        public string label;
        public bool settled;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref items, "items", LookMode.Reference);
            Scribe_Values.Look(ref marketValue, "marketValue");
            Scribe_Values.Look(ref label, "label");
            Scribe_Values.Look(ref settled, "settled");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (items == null) items = new List<Thing>();
                int removed = items.RemoveAll(t => t == null || t.Destroyed);
                if (removed > 0) SupportLog.Warning($"Job board reward '{label}': {removed} item reference(s) were missing or destroyed on load.");
            }
        }
    }
}

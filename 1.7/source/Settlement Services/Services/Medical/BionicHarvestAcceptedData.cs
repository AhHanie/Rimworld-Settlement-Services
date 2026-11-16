using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Services.Medical
{
    public sealed class BionicHarvestAcceptedData : IExposable
    {
        public List<string> keys = new List<string>();
        public List<int> refundShares = new List<int>();

        public void ExposeData()
        {
            Scribe_Collections.Look(ref keys, "keys", LookMode.Value);
            Scribe_Collections.Look(ref refundShares, "refundShares", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (keys == null) keys = new List<string>();
                if (refundShares == null) refundShares = new List<int>();
            }
        }
    }
}

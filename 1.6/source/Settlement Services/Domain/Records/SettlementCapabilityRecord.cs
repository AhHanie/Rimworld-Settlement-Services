using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Domain.Records
{
    public class SettlementCapabilityRecord : IExposable
    {
        public List<string> specialtyDefNames = new List<string>();

        public void ExposeData()
        {
            Scribe_Collections.Look(ref specialtyDefNames, "specialtyDefNames", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && specialtyDefNames == null)
                specialtyDefNames = new List<string>();
        }
    }
}

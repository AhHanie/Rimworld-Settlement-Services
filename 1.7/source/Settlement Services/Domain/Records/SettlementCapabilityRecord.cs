using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Domain.Records
{
    public class SettlementCapabilityRecord : IExposable
    {
        public List<string> specialtyDefNames = new List<string>();

        public string generatedForFactionLoadId;
        public string generatedForFactionDefName;
        public bool ownerFingerprintInitialized;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref specialtyDefNames, "specialtyDefNames", LookMode.Value);
            Scribe_Values.Look(ref generatedForFactionLoadId, "generatedForFactionLoadId");
            Scribe_Values.Look(ref generatedForFactionDefName, "generatedForFactionDefName");
            Scribe_Values.Look(ref ownerFingerprintInitialized, "ownerFingerprintInitialized");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && specialtyDefNames == null)
                specialtyDefNames = new List<string>();
        }
    }
}

using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Domain.Records
{
    public class StashContractRecord : IExposable
    {
        public int stashId;
        public int settlementWorldObjectId;
        public int createdTick;
        public int expiryTick;
        public List<Thing> items = new List<Thing>();
        public int originalFee;
        public int extensionFees;
        public int extensionDays;
        public bool warningSent;
        public string settlementLabel;

        public void ExposeData()
        {
            Scribe_Values.Look(ref stashId, "stashId");
            Scribe_Values.Look(ref settlementWorldObjectId, "settlementWorldObjectId");
            Scribe_Values.Look(ref createdTick, "createdTick");
            Scribe_Values.Look(ref expiryTick, "expiryTick");
            Scribe_Collections.Look(ref items, "items", LookMode.Reference);
            Scribe_Values.Look(ref originalFee, "originalFee");
            Scribe_Values.Look(ref extensionFees, "extensionFees");
            Scribe_Values.Look(ref extensionDays, "extensionDays");
            Scribe_Values.Look(ref warningSent, "warningSent");
            Scribe_Values.Look(ref settlementLabel, "settlementLabel");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && items == null) items = new List<Thing>();
        }
    }
}

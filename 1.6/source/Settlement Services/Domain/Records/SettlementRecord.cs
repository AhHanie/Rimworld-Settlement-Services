using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Domain.Records
{
    public class SettlementRecord : IExposable
    {
        public int settlementWorldObjectId = -1;
        public List<DiscoveryRecord> discoveries = new List<DiscoveryRecord>();
        public List<ReservationRecord> reservations = new List<ReservationRecord>();

        public SettlementCapabilityRecord capability;
        public List<StockRecord> stock = new List<StockRecord>();

        public InvestmentRecord investment;

        public Dictionary<string, int> recentServiceEventTicks = new Dictionary<string, int>();

        public List<HiringCandidateRecord> hiringCandidates = new List<HiringCandidateRecord>();
        public int hiringPoolLastRefreshTick = -1;
        public int nextHiringCandidateId = 1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref settlementWorldObjectId, "settlementWorldObjectId", -1);
            Scribe_Collections.Look(ref discoveries, "discoveries", LookMode.Deep);
            Scribe_Collections.Look(ref reservations, "reservations", LookMode.Deep);
            Scribe_Deep.Look(ref capability, "capability");
            Scribe_Collections.Look(ref stock, "stock", LookMode.Deep);
            Scribe_Deep.Look(ref investment, "investment");
            Scribe_Collections.Look(ref recentServiceEventTicks, "recentServiceEventTicks", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref hiringCandidates, "hiringCandidates", LookMode.Deep);
            Scribe_Values.Look(ref hiringPoolLastRefreshTick, "hiringPoolLastRefreshTick", -1);
            Scribe_Values.Look(ref nextHiringCandidateId, "nextHiringCandidateId", 1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (discoveries == null) discoveries = new List<DiscoveryRecord>();
                if (reservations == null) reservations = new List<ReservationRecord>();
                if (stock == null) stock = new List<StockRecord>();
                if (recentServiceEventTicks == null) recentServiceEventTicks = new Dictionary<string, int>();
                if (hiringCandidates == null) hiringCandidates = new List<HiringCandidateRecord>();
            }
        }

        public Settlement ResolveSettlement() => WorldObjectLookup.ResolveSettlement(settlementWorldObjectId);
    }
}

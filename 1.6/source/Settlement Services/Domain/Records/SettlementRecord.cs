using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using UnityEngine;
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
        public List<DynamicStockPoolRecord> dynamicStockPools = new List<DynamicStockPoolRecord>();

        public InvestmentRecord investment;

        public JobBoardRecord jobBoard;

        public Dictionary<string, int> recentServiceEventTicks = new Dictionary<string, int>();

        public int nextRumorAvailableTick = -1;
        public int lastRumorQuestJobId = -1;

        public List<HiringCandidateRecord> hiringCandidates = new List<HiringCandidateRecord>();
        public int hiringPoolExpiryTick = -1;
        public int nextHiringCandidateId = 1;

        public List<AnimalVendorOfferRecord> animalVendorOffers = new List<AnimalVendorOfferRecord>();
        public int nextAnimalVendorOfferId = 1;
        public int animalVendorNextRefreshTick = -1;
        public string animalVendorGeneratedForFactionLoadId;

        public bool practicedIdeosInitialized;
        public int practicedIdeoCount;
        public List<string> practicedIdeoLoadIds = new List<string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref settlementWorldObjectId, "settlementWorldObjectId", -1);
            Scribe_Collections.Look(ref discoveries, "discoveries", LookMode.Deep);
            Scribe_Collections.Look(ref reservations, "reservations", LookMode.Deep);
            Scribe_Deep.Look(ref capability, "capability");
            Scribe_Collections.Look(ref stock, "stock", LookMode.Deep);
            Scribe_Collections.Look(ref dynamicStockPools, "dynamicStockPools", LookMode.Deep);
            Scribe_Deep.Look(ref investment, "investment");
            Scribe_Deep.Look(ref jobBoard, "jobBoard");
            Scribe_Collections.Look(ref recentServiceEventTicks, "recentServiceEventTicks", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref nextRumorAvailableTick, "nextRumorAvailableTick", -1);
            Scribe_Values.Look(ref lastRumorQuestJobId, "lastRumorQuestJobId", -1);
            Scribe_Collections.Look(ref hiringCandidates, "hiringCandidates", LookMode.Deep);
            Scribe_Values.Look(ref hiringPoolExpiryTick, "hiringPoolExpiryTick", -1);
            Scribe_Values.Look(ref nextHiringCandidateId, "nextHiringCandidateId", 1);
            Scribe_Collections.Look(ref animalVendorOffers, "animalVendorOffers", LookMode.Deep);
            Scribe_Values.Look(ref nextAnimalVendorOfferId, "nextAnimalVendorOfferId", 1);
            Scribe_Values.Look(ref animalVendorNextRefreshTick, "animalVendorNextRefreshTick", -1);
            Scribe_Values.Look(ref animalVendorGeneratedForFactionLoadId, "animalVendorGeneratedForFactionLoadId");
            Scribe_Values.Look(ref practicedIdeosInitialized, "practicedIdeosInitialized");
            Scribe_Values.Look(ref practicedIdeoCount, "practicedIdeoCount");
            Scribe_Collections.Look(ref practicedIdeoLoadIds, "practicedIdeoLoadIds", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (discoveries == null) discoveries = new List<DiscoveryRecord>();
                if (reservations == null) reservations = new List<ReservationRecord>();
                if (stock == null) stock = new List<StockRecord>();
                if (dynamicStockPools == null) dynamicStockPools = new List<DynamicStockPoolRecord>();
                dynamicStockPools.RemoveAll(p => p == null || p.poolKey.NullOrEmpty());
                if (recentServiceEventTicks == null) recentServiceEventTicks = new Dictionary<string, int>();
                if (hiringCandidates == null) hiringCandidates = new List<HiringCandidateRecord>();
                hiringCandidates.RemoveAll(c => c == null || c.pawn == null || c.pawn.Destroyed || c.pawn.Dead);
                if (animalVendorOffers == null) animalVendorOffers = new List<AnimalVendorOfferRecord>();
                animalVendorOffers.RemoveAll(o => o == null || o.pawn == null || o.pawn.Destroyed || o.pawn.Dead);

                if (practicedIdeoLoadIds == null) practicedIdeoLoadIds = new List<string>();
                else
                {
                    var seen = new HashSet<string>();
                    practicedIdeoLoadIds = practicedIdeoLoadIds.Where(id => !string.IsNullOrEmpty(id) && seen.Add(id)).ToList();
                }
                if (practicedIdeosInitialized) practicedIdeoCount = Mathf.Clamp(practicedIdeoCount, 1, 3);
            }
        }

        public Settlement ResolveSettlement() => WorldObjectLookup.ResolveSettlement(settlementWorldObjectId);
    }
}

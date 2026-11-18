using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Custody;

namespace Settlement_Services.Domain
{
    public partial class SettlementServicesWorldComponent
    {
        private int earliestStashExpiry = int.MaxValue;

        public IReadOnlyList<StashContractRecord> ActiveStashes => stashes;

        public StashContractRecord GetStash(int id) => stashes.FirstOrDefault(s => s.stashId == id);

        public List<StashContractRecord> StashesForSettlement(int worldObjectId) =>
            stashes.Where(s => s.settlementWorldObjectId == worldObjectId).OrderBy(s => s.expiryTick).ToList();

        internal ThingOwner<Thing> StashCustody => itemCustody;

        internal StashContractRecord AddStash(Settlement settlement, int expiry, int fee, List<Thing> items)
        {
            if (nextStashId <= 0 || nextStashId == int.MaxValue) throw new OverflowException("Stash ID exhausted");
            var record = new StashContractRecord
            {
                stashId = nextStashId++, settlementWorldObjectId = settlement.ID,
                settlementLabel = settlement.LabelCap, createdTick = Find.TickManager.TicksGame,
                expiryTick = expiry, originalFee = fee, items = items
            };
            stashes.Add(record);
            if (expiry < earliestStashExpiry) earliestStashExpiry = expiry;
            return record;
        }

        internal void RemoveStash(StashContractRecord record)
        {
            stashes.Remove(record);
            earliestStashExpiry = stashes.Count == 0 ? int.MaxValue : stashes.Min(s => s.expiryTick);
        }

        internal void UpdateStashExpiry(StashContractRecord record)
        {
            earliestStashExpiry = stashes.Count == 0 ? int.MaxValue : stashes.Min(s => s.expiryTick);
        }

        private void TickStashes()
        {
            int now = Find.TickManager.TicksGame;
            if (now >= earliestStashExpiry) ExpireDueStashes();
            if (now % TickInterval != 0 || stashes.Count == 0) return;
            RecoverStashesAtMissingSettlements();
            foreach (StashContractRecord stash in stashes)
            {
                if (stash.warningSent || now >= stash.expiryTick || stash.expiryTick - (long)now > GenDate.TicksPerDay) continue;
                stash.warningSent = true;
                Find.LetterStack.ReceiveLetter("SettlementServices.Stash.WarningTitle".Translate(),
                    "SettlementServices.Stash.WarningBody".Translate(stash.stashId, stash.settlementLabel), LetterDefOf.NeutralEvent);
            }
        }

        public void ExpireDueStashes()
        {
            int now = Find.TickManager.TicksGame;
            if (now < earliestStashExpiry) return;
            // A settlement removed just before the deadline must release its paid stash.
            RecoverStashesAtMissingSettlements();
            foreach (StashContractRecord stash in stashes.Where(s => now >= s.expiryTick).ToList())
            {
                foreach (Thing item in stash.items)
                {
                    if (item != null && !item.Destroyed && itemCustody.Contains(item)) item.Destroy(DestroyMode.Vanish);
                }
                RemoveStash(stash);
                Find.LetterStack.ReceiveLetter("SettlementServices.Stash.LostTitle".Translate(),
                    "SettlementServices.Stash.LostBody".Translate(stash.stashId, stash.settlementLabel), LetterDefOf.NegativeEvent);
            }
        }

        private void RecoverStashesAtMissingSettlements()
        {
            foreach (StashContractRecord stash in stashes.Where(s => WorldObjectLookup.ResolveSettlement(s.settlementWorldObjectId) == null).ToList())
            {
                foreach (Thing item in stash.items)
                {
                    if (item == null || item.Destroyed || !itemCustody.Contains(item)) continue;
                    QueueHomeDelivery(new TargetSnapshot { kind = TargetKind.Item, liveThing = item,
                        snapshotLabel = item.LabelCap, snapshotDefName = item.def?.defName });
                }
                RemoveStash(stash);
                Find.LetterStack.ReceiveLetter("SettlementServices.Stash.RecoveryTitle".Translate(),
                    "SettlementServices.Stash.RecoveryBody".Translate(stash.stashId, stash.settlementLabel), LetterDefOf.NeutralEvent);
            }
        }

        private void ReconcileStashes()
        {
            var seen = new HashSet<Thing>();
            var ids = new HashSet<int>();
            foreach (StashContractRecord stash in stashes.ToList())
            {
                if (stash.items == null) stash.items = new List<Thing>();
                if (stash.stashId <= 0 || !ids.Add(stash.stashId))
                {
                    SupportLog.Error($"Invalid or duplicate stash ID {stash.stashId}; retaining the first record only.");
                    stashes.Remove(stash);
                    continue;
                }
                int removed = stash.items.RemoveAll(item => item == null || item.Destroyed || !itemCustody.Contains(item)
                    || IsTargetReserved(item)
                    || boardJobs.Any(job => job.reward?.items?.Contains(item) == true)
                    || pendingHomeDeliveries.Any(snapshot => snapshot?.liveThing == item)
                    || !seen.Add(item));
                if (removed > 0) SupportLog.Warning($"Stash {stash.stashId}: removed {removed} missing or duplicate item reference(s) on load.");
                if (stash.items.Count == 0) stashes.Remove(stash);
            }
            int highest = stashes.Count == 0 ? 0 : stashes.Max(s => s.stashId);
            if (nextStashId <= highest) nextStashId = highest == int.MaxValue ? int.MaxValue : highest + 1;
            earliestStashExpiry = stashes.Count == 0 ? int.MaxValue : stashes.Min(s => s.expiryTick);
            RecoverStashesAtMissingSettlements();
            ExpireDueStashes();
        }
    }
}

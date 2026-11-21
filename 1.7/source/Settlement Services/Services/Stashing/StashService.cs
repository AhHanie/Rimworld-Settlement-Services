using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Compatibility;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Registry;
using Settlement_Services.Framework.Validation;
using Settlement_Services.UI;

namespace Settlement_Services.Services.Stashing
{
    public static class StashService
    {
        private const string InvalidKey = "SettlementServices.Stash.Error.Invalid";
        private const string TransferKey = "SettlementServices.Stash.Error.Transfer";
        private const string ExpiredKey = "SettlementServices.Stash.Error.Expired";
        private const string SilverKey = "SettlementServices.Error.InsufficientCaravanSilver";

        private sealed class Transfer
        {
            public Thing item;
            public ThingOwner origin;
            public ThingOwner destination;
            public int count;
        }

        public static List<Thing> EligibleInventory(Caravan caravan)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (caravan == null || domain == null) return new List<Thing>();
            List<Thing> snapshot = CaravanInventoryUtility.AllInventoryItems(caravan).ToList();
            return snapshot.Where(t => IsEligibleItem(t, domain) && InventoryOwner(caravan, t) != null).ToList();
        }

        private static ThingOwner InventoryOwner(Caravan caravan, Thing thing)
        {
            if (caravan == null || thing == null) return null;
            foreach (Pawn pawn in caravan.PawnsListForReading)
            {
                ThingOwner owner = pawn.inventory?.innerContainer;
                if (owner != null && owner.Contains(thing)) return owner;
            }
            return null;
        }

        private static bool IsEligibleItem(Thing item, SettlementServicesWorldComponent domain)
        {
            if (item == null || item.Destroyed || item is Pawn || item.def == null || !item.def.destroyable
                || !item.questTags.NullOrEmpty() || domain.IsTargetReserved(item)) return false;
            return !ContainsProtectedNestedThing(item, domain, new HashSet<IThingHolder>());
        }

        private static bool ContainsProtectedNestedThing(Thing thing, SettlementServicesWorldComponent domain,
            HashSet<IThingHolder> seen)
        {
            if (!(thing is IThingHolder holder)) return false;
            return ContainsProtectedHolder(holder, domain, seen);
        }

        private static bool ContainsProtectedHolder(IThingHolder holder, SettlementServicesWorldComponent domain,
            HashSet<IThingHolder> seen)
        {
            if (!seen.Add(holder)) return false;
            ThingOwner contents = holder.GetDirectlyHeldThings();
            if (contents != null)
            {
                foreach (Thing child in contents)
                    if (child == null || child is Pawn || !child.def.destroyable
                        || !child.questTags.NullOrEmpty() || domain.IsTargetReserved(child)) return true;
            }
            var children = new List<IThingHolder>();
            holder.GetChildHolders(children);
            foreach (IThingHolder child in children)
                if (child != null && ContainsProtectedHolder(child, domain, seen)) return true;
            return false;
        }

        public static string VisitBlock(Settlement settlement, Caravan caravan)
        {
            if (settlement == null || settlement.Destroyed || WorldObjectLookup.ResolveSettlement(settlement.ID) != settlement)
                return "SettlementServices.Error.SettlementNoLongerExists";
            if (caravan == null || caravan.Destroyed || !caravan.Spawned || !caravan.IsPlayerControlled)
                return "SettlementServices.Error.CollectCaravanIneligible";
            if (caravan.pather != null && caravan.pather.Moving) return "SettlementServices.Error.CollectCaravanMoving";
            if (CaravanVisitUtility.SettlementVisitedNow(caravan) != settlement)
                return "SettlementServices.Error.CollectCaravanWrongTile";
            if (!Settlement_Services.UI.Interaction.SettlementServicesInteractionCommands.HasVisitingColonist(caravan))
                return "SettlementServices.Command.NoVisitingColonist";
            if (settlement.Faction != null && settlement.Faction.HostileTo(Faction.OfPlayer))
                return "SettlementServices.Error.FactionHostile";
            string blocked = SettlementServicesCompatibilityRegistry.GetRequestBlockReason(settlement)
                ?? EmpireTradePermitAccess.GetBlockReason(settlement, caravan);
            if (blocked != null) return blocked;
            return null;
        }

        public static string NewActionBlock(Settlement settlement, Caravan caravan)
        {
            string visitBlock = VisitBlock(settlement, caravan);
            if (visitBlock != null) return visitBlock;
            SettlementServiceDef def = DefDatabase<SettlementServiceDef>.GetNamedSilentFail("SettlementService_Stash");
            if (!SettlementServiceRegistry.IsEnabled(def)) return "SettlementServices.Error.ServiceDisabled";
            if (!SettlementServiceValidator.StructuralEligibility(def, settlement, out string blocked)
                || !SettlementServiceValidator.GoodwillMet(def, settlement, out blocked)) return blocked ?? InvalidKey;
            return null;
        }

        public static bool TryDeposit(Settlement settlement, Caravan caravan, IReadOnlyList<StashSelection> selections,
            int days, out StashContractRecord created, out string errorKey)
        {
            created = null;
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null) { errorKey = InvalidKey; return false; }
            domain.ExpireDueStashes();
            errorKey = NewActionBlock(settlement, caravan);
            if (errorKey != null) return false;
            if (!ValidateSelections(domain, caravan, selections, out errorKey)) return false;
            if (!StashPricing.TryQuote(selections, days, true, out StashQuote quote)
                || !TryAdvanceTick(Find.TickManager.TicksGame, days, out int expiry))
            { errorKey = "SettlementServices.Stash.Error.Price"; return false; }

            var selectedSilver = selections.Where(s => s.thing.def == ThingDefOf.Silver)
                .ToDictionary(s => s.thing, s => s.count);
            ThingOwner<Thing> escrow = new ThingOwner<Thing>(domain);
            var payment = new List<Transfer>();
            var moved = new List<Transfer>();
            try
            {
                if (!HoldSilver(domain, caravan, quote.total, selectedSilver, escrow, payment))
                { errorKey = SilverKey; return false; }
                foreach (StashSelection selection in selections)
                {
                    ThingOwner source = InventoryOwner(caravan, selection.thing);
                    if (source == null || selection.thing.stackCount < selection.count
                        || !Move(source, domain.StashCustody, selection.thing, selection.count, moved))
                        throw new InvalidOperationException("Stash item transfer failed");
                }
                created = domain.AddStash(settlement, expiry, quote.total, moved.Select(m => m.item).ToList());
                foreach (Transfer transfer in payment) transfer.item.Destroy(DestroyMode.Vanish);
                caravan.RecacheInventory();
                errorKey = null;
                return true;
            }
            catch (Exception ex)
            {
                SupportLog.Error($"Stash deposit failed: {ex}");
                if (created != null) domain.RemoveStash(created);
                created = null;
                Reverse(moved, caravan, domain);
                errorKey = TransferKey;
                return false;
            }
            finally
            {
                if (created == null) Reverse(payment, caravan, domain);
                caravan.RecacheInventory();
            }
        }

        public static bool TryExtend(Settlement settlement, Caravan caravan, int stashId, int days, out int charged, out string errorKey)
        {
            charged = 0;
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null) { errorKey = InvalidKey; return false; }
            domain.ExpireDueStashes();
            errorKey = NewActionBlock(settlement, caravan);
            if (errorKey != null) return false;
            StashContractRecord stash = domain.GetStash(stashId);
            if (stash == null || stash.settlementWorldObjectId != settlement.ID || Find.TickManager.TicksGame >= stash.expiryTick)
            { errorKey = ExpiredKey; return false; }
            if (stash.items == null || stash.items.Count == 0 || stash.items.Any(t => t == null || t.Destroyed)
                || !TryAdvanceTick(stash.expiryTick, days, out int expiry)
                || !StashPricing.TryQuote(stash.items.Select(t => new StashSelection(t, t.stackCount)), days, false, out StashQuote quote)
                || stash.items.Any(t => !domain.StashCustody.Contains(t))
                || (long)stash.extensionFees + quote.total > int.MaxValue
                || (long)stash.extensionDays + days > int.MaxValue)
            { errorKey = "SettlementServices.Stash.Error.Price"; return false; }

            ThingOwner<Thing> escrow = new ThingOwner<Thing>(domain);
            var payment = new List<Transfer>();
            bool committed = false;
            try
            {
                if (!HoldSilver(domain, caravan, quote.total, null, escrow, payment))
                { errorKey = SilverKey; return false; }
                foreach (Transfer transfer in payment) transfer.item.Destroy(DestroyMode.Vanish);
                stash.expiryTick = expiry;
                stash.extensionFees += quote.total;
                stash.extensionDays += days;
                stash.warningSent = false;
                domain.UpdateStashExpiry(stash);
                charged = quote.total;
                committed = true;
                errorKey = null;
                return true;
            }
            catch (Exception ex)
            {
                SupportLog.Error($"Stash extension failed: {ex}");
                errorKey = TransferKey;
                return false;
            }
            finally
            {
                if (!committed) Reverse(payment, caravan, domain);
                caravan.RecacheInventory();
            }
        }

        public static bool TryCollect(Settlement settlement, Caravan caravan, int stashId, out string errorKey)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null) { errorKey = InvalidKey; return false; }
            domain.ExpireDueStashes();
            errorKey = VisitBlock(settlement, caravan);
            if (errorKey != null) return false;
            StashContractRecord stash = domain.GetStash(stashId);
            if (stash == null || stash.settlementWorldObjectId != settlement.ID || Find.TickManager.TicksGame >= stash.expiryTick)
            { errorKey = ExpiredKey; return false; }
            var destinations = new List<ThingOwner>();
            foreach (Thing item in stash.items)
            {
                if (item == null || item.Destroyed || !domain.StashCustody.Contains(item))
                { errorKey = TransferKey; return false; }
                Pawn recipient = CaravanInventoryUtility.FindPawnToMoveInventoryTo(item, caravan.PawnsListForReading, null);
                ThingOwner owner = recipient?.inventory?.innerContainer;
                if (owner == null || !owner.CanAcceptAnyOf(item, false))
                { errorKey = "SettlementServices.Stash.Error.NoRecipient"; return false; }
                destinations.Add(owner);
            }
            var moved = new List<Transfer>();
            try
            {
                for (int i = 0; i < stash.items.Count; i++)
                    if (!Move(domain.StashCustody, destinations[i], stash.items[i], stash.items[i].stackCount, moved))
                        throw new InvalidOperationException("Stash collection transfer failed");
                domain.RemoveStash(stash);
                caravan.RecacheInventory();
                errorKey = null;
                return true;
            }
            catch (Exception ex)
            {
                SupportLog.Error($"Stash collection failed: {ex}");
                Reverse(moved, caravan, domain);
                errorKey = TransferKey;
                return false;
            }
        }

        private static bool ValidateSelections(SettlementServicesWorldComponent domain, Caravan caravan,
            IReadOnlyList<StashSelection> selections, out string errorKey)
        {
            errorKey = InvalidKey;
            if (selections == null || selections.Count == 0) return false;
            var seen = new HashSet<Thing>();
            List<Thing> snapshot = CaravanInventoryUtility.AllInventoryItems(caravan).ToList();
            foreach (StashSelection selection in selections)
                if (selection.thing == null || !seen.Add(selection.thing) || !snapshot.Contains(selection.thing)
                    || InventoryOwner(caravan, selection.thing) == null || !IsEligibleItem(selection.thing, domain)
                    || selection.count < 1 || selection.count > selection.thing.stackCount) return false;
            errorKey = null;
            return true;
        }

        private static bool TryAdvanceTick(int baseTick, int days, out int result)
        {
            long value = baseTick + (long)days * GenDate.TicksPerDay;
            result = value > int.MaxValue || days < 1 || days > StashPricing.MaximumPurchaseDays ? 0 : (int)value;
            return result > baseTick;
        }

        private static bool HoldSilver(SettlementServicesWorldComponent domain, Caravan caravan, int amount,
            Dictionary<Thing, int> selected, ThingOwner<Thing> escrow, List<Transfer> payment)
        {
            List<Thing> silver = CaravanInventoryUtility.AllInventoryItems(caravan)
                .Where(t => t.def == ThingDefOf.Silver && !domain.IsTargetReserved(t)).ToList();
            long available = silver.Sum(t => (long)t.stackCount - (selected != null && selected.TryGetValue(t, out int count) ? count : 0));
            if (available < amount) return false;
            int remaining = amount;
            foreach (Thing stack in silver)
            {
                if (remaining == 0) break;
                int reserved = selected != null && selected.TryGetValue(stack, out int count) ? count : 0;
                int take = Math.Min(remaining, stack.stackCount - reserved);
                if (take <= 0) continue;
                ThingOwner origin = InventoryOwner(caravan, stack);
                if (origin == null || !Move(origin, escrow, stack, take, payment)) return false;
                remaining -= take;
            }
            return remaining == 0;
        }

        private static bool Move(ThingOwner origin, ThingOwner destination, Thing item, int count, List<Transfer> ledger)
        {
            int transferred = origin.TryTransferToContainer(item, destination, count, out Thing actual, false);
            if (actual != null && destination.Contains(actual))
                ledger.Add(new Transfer { item = actual, origin = origin, destination = destination, count = transferred });
            return transferred == count && actual != null && destination.Contains(actual) && actual.stackCount == count;
        }

        private static void Reverse(List<Transfer> ledger, Caravan caravan, SettlementServicesWorldComponent domain)
        {
            for (int i = ledger.Count - 1; i >= 0; i--)
            {
                Transfer transfer = ledger[i];
                Thing thing = transfer.item;
                if (thing == null || thing.Destroyed || !transfer.destination.Contains(thing)) continue;
                if (transfer.destination.TryTransferToContainer(thing, transfer.origin, thing.stackCount, out Thing restored, false) == transfer.count
                    && restored != null && transfer.origin.Contains(restored)) continue;
                Pawn fallback = CaravanInventoryUtility.FindPawnToMoveInventoryTo(thing, caravan.PawnsListForReading, null);
                ThingOwner owner = fallback?.inventory?.innerContainer;
                if (owner != null && transfer.destination.TryTransferToContainer(thing, owner, thing.stackCount, out Thing recovered, false) == transfer.count
                    && recovered != null && owner.Contains(recovered)) continue;
                if (!domain.StashCustody.Contains(thing))
                    transfer.destination.TryTransferToContainer(thing, domain.StashCustody, thing.stackCount, out _, false);
                if (domain.StashCustody.Contains(thing))
                    domain.QueueHomeDelivery(new TargetSnapshot { kind = TargetKind.Item, liveThing = thing,
                        snapshotLabel = thing.LabelCap, snapshotDefName = thing.def?.defName });
                SupportLog.Error($"Could not return {thing} to its original caravan inventory during stash rollback; queued home delivery if recoverable.");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain;
using Verse;

namespace Settlement_Services.Services.Land
{
    internal sealed class LandPlotSilverHold : IDisposable
    {
        private readonly SettlementServicesWorldComponent domain;
        private readonly Caravan caravan;
        private readonly ThingOwner<Thing> escrow;
        private readonly List<(Pawn pawn, Thing stack, int count)> staged = new();
        private bool committed;

        internal LandPlotSilverHold(SettlementServicesWorldComponent domain, Caravan caravan)
        {
            this.domain = domain;
            this.caravan = caravan;
            escrow = new ThingOwner<Thing>(domain);
        }

        private static IEnumerable<(Pawn pawn, Thing stack)> SpendableSilver(SettlementServicesWorldComponent domain, Caravan caravan)
        {
            foreach (Pawn pawn in caravan.PawnsListForReading)
            {
                if (pawn.inventory == null) continue;
                foreach (Thing stack in pawn.inventory.innerContainer)
                    if (stack.def == ThingDefOf.Silver && !stack.Destroyed && !domain.IsTargetReserved(stack))
                        yield return (pawn, stack);
            }
        }

        internal static long Available(SettlementServicesWorldComponent domain, Caravan caravan) =>
            SpendableSilver(domain, caravan).Sum(entry => (long)entry.stack.stackCount);

        internal bool Stage(int amount)
        {
            foreach (var (pawn, stack) in SpendableSilver(domain, caravan).ToList())
            {
                int take = Math.Min(amount, stack.stackCount);
                ThingOwner origin = pawn.inventory.innerContainer;
                int moved;
                Thing actual;
                int oldCount = escrow.Count;
                try
                {
                    moved = origin.TryTransferToContainer(stack, escrow, take, out actual, false);
                }
                finally
                {
                    // Container callbacks can throw after the stack has already moved.
                    for (int index = oldCount; index < escrow.Count; index++)
                        staged.Add((pawn, escrow[index], escrow[index].stackCount));
                }
                if (moved != take || actual == null || !escrow.Contains(actual) || actual.stackCount != take) return false;
                amount -= moved;
                if (amount == 0) return true;
            }
            return false;
        }

        internal void Commit()
        {
            foreach (var entry in staged) entry.stack.Destroy(DestroyMode.Vanish);
            committed = true;
        }

        public void Dispose()
        {
            try
            {
                if (committed) return;
                foreach (var (pawn, stack, count) in staged)
                {
                    Thing silver = stack;
                    if (silver.Destroyed)
                    {
                        // A failure while consuming the hold must refund stacks already consumed.
                        silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                        silver.stackCount = count;
                        escrow.TryAdd(silver, false);
                    }
                    if (!escrow.Contains(silver)) continue;
                    if (caravan.Spawned && !caravan.Destroyed)
                    {
                        if (caravan.PawnsListForReading.Contains(pawn) && Move(silver, pawn.inventory?.innerContainer)) continue;
                        if (caravan.PawnsListForReading.Any(p => Move(silver, p.inventory?.innerContainer))) continue;
                    }
                    if (!Move(silver, domain.StashCustody))
                        throw new InvalidOperationException($"Could not preserve land payment refund {silver}.");
                    domain.QueueHomeDelivery(new TargetSnapshot
                    {
                        kind = TargetKind.Item, liveThing = silver, snapshotLabel = silver.LabelCap,
                        snapshotDefName = silver.def.defName
                    });
                }
            }
            finally
            {
                try
                {
                    if (!caravan.Destroyed) caravan.RecacheInventory();
                }
                catch (Exception ex)
                {
                    SupportLog.Error($"Could not refresh caravan {caravan.ID} inventory after a land transaction: {ex}");
                }
            }
        }

        private bool Move(Thing stack, ThingOwner destination)
        {
            if (destination == null) return false;
            int count = stack.stackCount;
            try
            {
                escrow.TryTransferToContainer(stack, destination, count, out _, false);
            }
            catch (Exception ex)
            {
                SupportLog.Error($"Land payment rollback encountered an inventory callback failure for {stack}: {ex}");
            }
            if (destination.Contains(stack) && stack.stackCount == count) return true;
            if (stack.holdingOwner == null && !stack.Destroyed) escrow.TryAdd(stack, false);
            return false;
        }
    }
}

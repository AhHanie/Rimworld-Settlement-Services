using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Verse;

namespace Settlement_Services.Services.Land
{
    internal static partial class LandPlotService
    {
        internal const string SaleBlockedOccupants = Key + "SaleBlockedOccupants";
        private const string SaleBlockedBusy = Key + "SaleBlockedBusy";
        private const string SaleFailed = Key + "SaleFailed";
        private static readonly HashSet<LandPlotRecord> salesInProgress = new();

        internal static string SaleBlock(Settlement settlement, Caravan caravan, LandPlotRecord plot) =>
            SaleContractBlock(settlement, caravan, plot, out _) ?? (salesInProgress.Contains(plot)
                ? SaleBlockedBusy : SalePlotBlock(plot, plot.Map));

        private static string SaleContractBlock(Settlement settlement, Caravan caravan, LandPlotRecord plot,
            out SettlementServicesWorldComponent domain)
        {
            string blocked = VisitTransactionBlock(settlement, caravan, out domain);
            if (blocked != null) return blocked;
            if (plot == null || plot.hostId != settlement.ID || domain.LandPlotAtHost(settlement.ID) != plot)
                return Key + "Changed";
            return null;
        }

        private static string SalePlotBlock(LandPlotRecord plot, Map map)
        {
            if (plot.recoveryError) return Key + "RecordError";
            if (plot.rented || plot.hostLost || plot.relinquished) return Key + "Closed";
            var parent = plot.parent;
            bool ClaimsPlot(SettlementLandMapParent other) => other.plotId == plot.id || other.hostId == plot.hostId;
            if (Find.World.pocketMaps.OfType<SettlementLandMapParent>().Any(other => other != parent && ClaimsPlot(other))
                || Find.Maps.Any(otherMap => otherMap != map && SettlementLandMapParent.For(otherMap) is { } other
                    && (other == parent || ClaimsPlot(other))))
                return Key + "RecordError";

            if (!plot.generated) return parent == null && map == null ? null : Key + "RecordError";
            if (!SaleMapMatches(plot, parent, map)) return Key + "RecordError";
            if (parent.transfers != 0 || LandPlotPatches.IsPacking(map)) return SaleBlockedBusy;
            if (SettlementLandMapParent.LivingPawns(map).Any()) return SaleBlockedOccupants;
            // Match Game.DeinitAndRemoveMap's check for pocket maps removed with their source map.
            if (Find.World.pocketMaps.Any(other => other.sourceMap == map
                && other.Map?.generatorDef?.pocketMapProperties?.destroyOnParentMapAbandoned != false))
                return Key + "SaleBlockedDependentMap";
            return null;
        }

        private static bool SaleMapMatches(LandPlotRecord plot, SettlementLandMapParent parent, Map map) =>
            parent is { Destroyed: false } && parent.MatchesPlotMap(plot, map) && parent.Map == map
            && Find.World.pocketMaps.Count(other => other == parent) == 1;

        internal static string ConfirmSale(Settlement settlement, Caravan caravan, LandPlotRecord plot,
            int side, bool bunkerInterior, SettlementLandMapParent parent, Map map, int expectedPrice)
        {
            if (plot == null) return Key + "Changed";
            if (!salesInProgress.Add(plot)) return SaleBlockedBusy;

            SettlementServicesWorldComponent domain = null;
            var staged = new List<(Thing stack, int count)>();
            bool committing = false;
            bool committed = false;

            string Revalidate()
            {
                string blocked = SaleContractBlock(settlement, caravan, plot, out domain);
                if (blocked != null) return blocked;
                if (plot.side != side || plot.bunkerInterior != bunkerInterior || plot.parent != parent
                    || plot.Map != map || plot.generated != (map != null)) return Key + "Changed";
                return SalePlotBlock(plot, map);
            }

            void LogFailure(string action, Exception ex) => SupportLog.Error(
                $"Land sale for plot {plot.id}, host {plot.hostId}, caravan {caravan?.ID}, map {map?.uniqueID} {action}: {ex}");

            try
            {
                string blocked = Revalidate();
                if (blocked != null) return blocked;
                if (!LandPlotPolicy.TrySaleQuote(plot, out int amount)) return Key + "PriceUnavailable";
                if (amount != expectedPrice) return Key + "PriceChanged";
                if (!StageSaleSilver(caravan, amount, staged)) return SaleFailed;

                // Inventory callbacks can change transaction state while the payout is being added.
                blocked = Revalidate();
                if (blocked != null) return blocked;
                if (staged.Any(entry => !IsSaleStackInCaravan(caravan, entry.stack, entry.count))) return SaleFailed;

                if (map != null)
                {
                    int spawnedCount = map.spawnedThings.Count;
                    int listedCount = map.listerThings.AllThings.Count;
                    Region arrivalRegion = map.regionGrid.GetRegionAt_NoRebuild_InvalidAllowed(map.Center);
                    try
                    {
                        LandPlotTravel.TryRemoveMap(parent, ex => LogFailure("could not tear down the map", ex));
                    }
                    catch (Exception ex)
                    {
                        LogFailure("could not finish map teardown", ex);
                    }
                    // Once the game detaches the map, keep the payout even if a removal hook failed.
                    if (Find.Maps.Contains(map))
                    {
                        // The game can clear map listers and region references before detaching the map.
                        if (!SaleMapMatches(plot, parent, map) || map.spawnedThings.Count != spawnedCount
                            || map.listerThings.AllThings.Count != listedCount || (arrivalRegion != null && arrivalRegion.Map != map))
                        {
                            plot.recoveryError = true;
                            if (!parent.Destroyed && map.Parent == parent && !Find.World.pocketMaps.Contains(parent))
                                Find.World.pocketMaps.Add(parent);
                        }
                        return SaleFailed;
                    }
                    Find.World.pocketMaps.Remove(parent);
                }

                committing = true;
                domain.RemoveLandPlot(plot);
                committed = true;
                if (map is { Disposed: false }) map.Dispose();
                return null;
            }
            catch (Exception ex)
            {
                // RemoveLandPlot updates the indexes before it reschedules contract transitions.
                committed |= committing && (map != null || domain.LandPlotAtHost(plot.hostId) != plot);
                LogFailure("failed", ex);
                return committed ? null : SaleFailed;
            }
            finally
            {
                try
                {
                    if (!committed)
                        foreach (var (stack, _) in staged)
                            RollBackSaleStack(stack, LogFailure);
                    if (staged.Count != 0 && !caravan.Destroyed) caravan.RecacheInventory();
                }
                catch (Exception ex)
                {
                    LogFailure("could not refresh caravan inventory", ex);
                }
                finally
                {
                    salesInProgress.Remove(plot);
                }
            }
        }

        private static bool StageSaleSilver(Caravan caravan, int amount, List<(Thing stack, int count)> staged)
        {
            int stackLimit = ThingDefOf.Silver.stackLimit;
            if (stackLimit < 1) return false;
            while (amount > 0)
            {
                int count = Math.Min(amount, stackLimit);
                Thing stack = ThingMaker.MakeThing(ThingDefOf.Silver);
                staged.Add((stack, count));
                stack.stackCount = count;
                Pawn carrier = CaravanInventoryUtility.FindPawnToMoveInventoryTo(stack, caravan.PawnsListForReading, null);
                ThingOwner inventory = carrier?.inventory?.innerContainer;
                if (inventory == null || !inventory.TryAdd(stack, canMergeWithExistingStacks: false)
                    || stack.holdingOwner != inventory || !inventory.Contains(stack) || stack.Destroyed || stack.stackCount != count)
                    return false;
                amount -= count;
            }
            return true;
        }

        private static bool IsSaleStackInCaravan(Caravan caravan, Thing stack, int count) =>
            !stack.Destroyed && stack.stackCount == count && stack.holdingOwner is ThingOwner inventory
            && inventory.Contains(stack) && caravan.PawnsListForReading.Any(pawn => pawn.inventory?.innerContainer == inventory);

        private static void RollBackSaleStack(Thing stack, Action<string, Exception> logFailure)
        {
            try
            {
                stack.holdingOwner?.Remove(stack);
            }
            catch (Exception ex)
            {
                // ThingOwner clears ownership before notifying the inventory.
                logFailure("encountered an error removing a payout stack", ex);
            }
            try
            {
                if (!stack.Destroyed) stack.Destroy(DestroyMode.Vanish);
            }
            catch (Exception ex)
            {
                logFailure("encountered an error destroying a payout stack", ex);
            }
            if (stack.holdingOwner != null || !stack.Destroyed)
                logFailure("could not verify payout rollback", new InvalidOperationException($"Payout stack {stack} remained after rollback."));
        }
    }
}

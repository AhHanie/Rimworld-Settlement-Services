using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Custody;
using Verse;

namespace Settlement_Services.Services.Land
{
    internal static class LandPlotTravel
    {
        internal static bool TileExists(PlanetTile tile)
        {
            if (!tile.Valid) return false;
            try
            {
                return tile.Layer is PlanetLayer layer && tile.tileId < layer.TilesCount;
            }
            catch (KeyNotFoundException)
            {
                return false;
            }
        }

        internal static bool CanUseTile(PlanetTile tile) => TileExists(tile) && tile.LayerDef.canFormCaravans
            && !tile.LayerDef.isSpace && !Find.World.Impassable(tile);

        internal static PlanetTile RecoveryTile(PlanetTile hostTile)
        {
            if (CanUseTile(hostTile)) return hostTile;
            if (TileExists(hostTile) && hostTile.LayerDef.canFormCaravans && !hostTile.LayerDef.isSpace
                && GenWorldClosest.TryFindClosestPassableTile(hostTile, out PlanetTile alternate) && CanUseTile(alternate))
                return alternate;
            return Find.Maps.Where(map => map.IsPlayerHome).Select(map => map.Tile)
                .FirstOrDefault(CanUseTile, PlanetTile.Invalid);
        }

        internal static string EntryBlock(LandPlotRecord plot, Caravan caravan)
        {
            if (plot == null || SettlementServicesWorldComponent.Current?.LandPlotAtHost(plot.hostId) != plot)
                return LandPlotService.Invalid;
            if (plot.recoveryError && (plot.Map == null || plot.parent.Contract != plot))
                return LandPlotService.Key + "RecordError";
            if (!plot.recoveryError && plot.Due(LandPlotPolicy.Now)) return LandPlotService.Key + "Closed";
            if (!LandPlotPatches.Ready) return LandPlotService.PatchFailure;
            string blocked = LandPlotService.CaravanBlock(caravan);
            if (blocked != null) return blocked;
            if (caravan.Tile != RecoveryTile(plot.hostTile)) return "SettlementServices.Error.CollectCaravanWrongTile";
            Settlement host = WorldObjectLookup.ResolveSettlement(plot.hostId);
            if (plot.Map == null)
            {
                if (plot.generated || plot.parent != null) return LandPlotService.Key + "RecordError";
                if (!LandPlotService.CanEnterNormally(plot, host, caravan))
                    return LandPlotService.Key + "EmptyRecovery";
                if (Find.Maps.Count >= LandPlotPolicy.LoadedMapLimit) return LandPlotService.MapLimit;
                if (LandPlotService.Generator == null || LandPlotService.ParentDef == null) return LandPlotService.PatchFailure;
            }
            return null;
        }

        internal static string Enter(LandPlotRecord plot, Caravan caravan)
        {
            string blocked = EntryBlock(plot, caravan);
            if (blocked != null) return blocked;

            Map map = plot.Map ?? Generate(plot);
            if (map == null) return LandPlotService.Key + "GenerationFailed";
            return TransferIntoPlot(plot.parent, caravan);
        }

        internal static string EnterOrphan(SettlementLandMapParent parent, Caravan caravan)
        {
            if (!LandPlotPatches.Ready) return LandPlotService.PatchFailure;
            string blocked = LandPlotService.CaravanBlock(caravan);
            if (blocked != null) return blocked;
            if (!Find.World.pocketMaps.Contains(parent) || parent.Map == null || parent.Contract != null)
                return LandPlotService.Invalid;
            if (caravan.Tile != RecoveryTile(parent.departureTile))
                return "SettlementServices.Error.CollectCaravanWrongTile";
            return TransferIntoPlot(parent, caravan);
        }

        private static string TransferIntoPlot(SettlementLandMapParent parent, Caravan caravan)
        {
            string blocked = LandPlotService.CaravanBlock(caravan);
            if (blocked != null) return blocked;
            Map map = parent.Map;
            List<Pawn> party = caravan.PawnsListForReading.ToList();
            List<IntVec3> cells = map.AllCells.Where(cell => cell.Standable(map) && !cell.Fogged(map)
                    && cell.GetFirstPawn(map) == null && cell.GetFirstThing<Fire>(map) == null)
                .OrderBy(cell => cell.DistanceToSquared(map.Center)).ToList();
            if (cells.Count < party.Count) return LandPlotService.Key + "ArrivalBlocked";
            var arrival = new Dictionary<Pawn, IntVec3>(party.Count);
            foreach (Pawn pawn in party)
            {
                int index = cells.FindIndex(cell => cell.WalkableBy(map, pawn));
                if (index < 0) return LandPlotService.Key + "ArrivalBlocked";
                arrival.Add(pawn, cells[index]);
                cells.RemoveAt(index);
            }
            var caravanTile = caravan.Tile;
            parent.transfers++;
            try
            {
                CaravanEnterMapUtility.Enter(caravan, map, pawn => arrival[pawn]);
                if (party.Any(pawn => pawn.MapHeld != map) || !caravan.Destroyed)
                    throw new InvalidOperationException("Caravan entry did not transfer the whole party.");
            }
            catch (Exception ex)
            {
                SupportLog.Error($"Land plot {parent.plotId} entry failed: {ex}");
                RestoreEntryParty(caravan, party, caravanTile, map, parent.plotId);
                return LandPlotService.Key + "EntryFailed";
            }
            finally
            {
                parent.transfers--;
                parent.InvalidateOccupancy();
            }

            CameraJumper.TryJump(map.Center, map);
            return null;
        }

        private static void RestoreEntryParty(Caravan original, List<Pawn> party, PlanetTile tile, Map map, int plotId)
        {
            Caravan receiver = original.Spawned && !original.Destroyed ? original
                : CaravanMaker.MakeCaravan(Array.Empty<Pawn>(), Faction.OfPlayer, tile, true);
            foreach (Pawn pawn in party)
            {
                if (receiver.ContainsPawn(pawn)) continue;
                try
                {
                    if (!MovePawn(pawn, map, receiver))
                        SupportLog.Error($"Land plot {plotId} could not verify caravan rollback for pawn {pawn.thingIDNumber}.");
                }
                catch (Exception ex)
                {
                    SupportLog.Error($"Land plot {plotId} could not restore pawn {pawn.thingIDNumber}: {ex}");
                }
            }
            if (receiver.PawnsListForReading.Count == 0) receiver.Destroy();
            else
            {
                receiver.Name = CaravanNameGenerator.GenerateCaravanName(receiver);
                receiver.RecacheInventory();
            }
        }

        private static Map Generate(LandPlotRecord plot)
        {
            SettlementLandMapParent parent = null;
            try
            {
                parent = (SettlementLandMapParent)WorldObjectMaker.MakeWorldObject(LandPlotService.ParentDef);
                parent.plotId = plot.id;
                parent.hostId = plot.hostId;
                parent.departureTile = plot.hostTile;
                parent.Tile = PlanetTile.Invalid;
                parent.SetFaction(Faction.OfPlayer);
                parent.mapGenerator = LandPlotService.Generator;
                parent.transfers++;
                Map map = MapGenerator.GenerateMap(new IntVec3(plot.side, 1, plot.side), parent,
                    parent.mapGenerator, isPocketMap: true);
                if (map == null || parent.Map != map || !parent.terrainGenerated)
                    throw new InvalidOperationException("Land map generation did not complete.");
                Find.World.pocketMaps.Add(parent);
                plot.parent = parent;
                plot.generated = true;
                return map;
            }
            catch (Exception ex)
            {
                SupportLog.Error($"Land plot {plot.id} generation failed at host {plot.hostId}: {ex}");
                if (parent != null && !TryRemoveMap(parent))
                {
                    plot.parent = parent;
                    plot.generated = parent.Map != null;
                    plot.recoveryError = true;
                    if (!Find.World.pocketMaps.Contains(parent)) Find.World.pocketMaps.Add(parent);
                }
                return null;
            }
            finally
            {
                if (parent != null)
                {
                    parent.transfers--;
                    parent.InvalidateOccupancy();
                }
            }
        }

        internal static bool TryRemoveMap(SettlementLandMapParent parent)
        {
            Map map = parent.Map;
            try
            {
                if (map != null)
                {
                    if (SettlementLandMapParent.LivingPawns(map).Any()) return false;
                    Current.Game.DeinitAndRemoveMap(map, notifyPlayer: false);
                }
            }
            catch (Exception ex)
            {
                SupportLog.Error($"Land plot {parent.plotId} teardown failed for parent {parent.ID} and map {map?.uniqueID}: {ex}");
            }
            if (parent.Map != null) return false;
            Find.World.pocketMaps.Remove(parent);
            return true;
        }

        internal static bool Evacuate(SettlementLandMapParent parent)
        {
            Map map = parent.Map;
            if (map == null) return true;
            List<Pawn> occupants = SettlementLandMapParent.LivingPawns(map).Distinct().ToList();
            if (occupants.Count == 0) return true;

            PlanetTile tile = RecoveryTile(parent.departureTile);
            Caravan receiver = null;
            if (tile.Valid)
            {
                receiver = Find.WorldObjects.Caravans.FirstOrDefault(c => c.Tile == tile
                    && LandPlotService.CaravanBlock(c) == null && CaravanOwnershipRules.CaravanHasCapableOwner(c));
                if (receiver == null && CaravanOwnershipRules.HasCapableOwner(occupants, Faction.OfPlayer))
                    receiver = CaravanMaker.MakeCaravan(Array.Empty<Pawn>(), Faction.OfPlayer, tile, true);
            }

            parent.transfers++;
            bool secured = true;
            try
            {
                foreach (Pawn pawn in occupants.OrderByDescending(p => CaravanOwnershipRules.IsCapableOwner(p, Faction.OfPlayer)))
                    secured &= MovePawn(pawn, map, receiver);
            }
            finally
            {
                parent.transfers--;
                parent.InvalidateOccupancy();
                if (receiver != null)
                {
                    if (receiver.PawnsListForReading.Count == 0) receiver.Destroy();
                    else
                    {
                        receiver.Name = CaravanNameGenerator.GenerateCaravanName(receiver);
                        receiver.RecacheInventory();
                    }
                }
            }
            return secured && !SettlementLandMapParent.LivingPawns(map).Any();
        }

        private static bool MovePawn(Pawn pawn, Map map, Caravan receiver)
        {
            if (receiver?.ContainsPawn(pawn) == true) return true;
            if (pawn.MapHeld != map && pawn.holdingOwner != null) return false;
            ThingOwner origin = pawn.holdingOwner;
            IntVec3 cell = pawn.PositionHeld;
            bool spawned = pawn.Spawned;
            bool secured = false;
            try
            {
                Thing carried = pawn.carryTracker?.CarriedThing;
                if (carried != null && carried is not Pawn
                    && pawn.inventory?.innerContainer.TryAddOrTransfer(carried, false) != true) return false;
                if (spawned) pawn.DeSpawnOrDeselect();
                if (pawn.holdingOwner != null && !pawn.holdingOwner.Remove(pawn)) return false;
                if (receiver != null)
                {
                    if (!receiver.pawns.TryAdd(pawn, false) || !receiver.ContainsPawn(pawn)) return false;
                    if (!Find.WorldPawns.Contains(pawn)) Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
                    secured = Find.WorldPawns.Contains(pawn);
                }
                else
                {
                    TargetCustodyService.ParkPawnInWorld(pawn);
                    secured = Find.WorldPawns.Contains(pawn);
                    if (secured)
                    {
                        SettlementServicesWorldComponent.Current.QueueHomeDelivery(new TargetSnapshot
                        {
                            kind = TargetKind.Pawn, liveThing = pawn, snapshotLabel = pawn.LabelShortCap,
                            snapshotDefName = pawn.def.defName
                        });
                    }
                }
                return secured;
            }
            finally
            {
                if (!secured && pawn.MapHeld != map)
                {
                    if (receiver?.ContainsPawn(pawn) == true) receiver.RemovePawn(pawn);
                    if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
                    if (pawn.holdingOwner == null && (origin == null || origin.Owner is Caravan || !origin.TryAdd(pawn, false)))
                        GenSpawn.Spawn(pawn, cell.InBounds(map) ? cell : map.Center, map);
                }
            }
        }
    }
}

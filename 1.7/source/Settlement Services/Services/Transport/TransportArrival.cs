using System;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Custody;
using Verse;

namespace Settlement_Services.Services.Transport
{
    internal static class TransportArrival
    {
        internal const int ApproachTicks = 60;
        internal const int DepartureTicks = 60;
        private const int WorldUnloadTicks = 30;
        private const int ShuttleUnloadInterval = 8;
        private const int RecoveryInterval = 60;
        private const string Key = TransportBooking.Key;

        private static T FindWorldObject<T>(int id) where T : WorldObject => id < 0 ? null
            : Find.WorldObjects.AllWorldObjects.FirstOrDefault(o => o.ID == id) as T;

        internal static void Start(TransportContractRecord contract)
        {
            if (contract.Destination.IsLocal && TryStartLocal(contract)) return;
            StartWorldFallback(contract);
        }

        private static bool TryStartLocal(TransportContractRecord contract)
        {
            if (contract.deliveredAny || contract.landingAttempts >= 2) return false;
            MapParent parent = TransportRoute.FindLocalDestination(contract.Destination);
            if (parent == null) return false;

            string error = TransportRoute.ResolveSpecs(contract.mode, TechLevel.Archotech, out TransportSpecs specs);
            if (error != null) return false;
            Map map = parent.Map;
            IntVec3 cell = IntVec3.Invalid;
            int spots = GenRadial.NumCellsInRadius(16f);
            for (int i = 0; i < spots; i++)
            {
                IntVec3 candidate = contract.bookedCell + GenRadial.RadialPattern[i];
                if (contract.landingAttempts > 0 && candidate == contract.landingCell) continue;
                if (!TransportRoute.IsSafeCell(contract.mode, specs.Hull, candidate, map)) continue;
                cell = candidate;
                break;
            }
            if (!cell.IsValid) return false;

            contract.landingAttempts++;
            contract.landingCell = cell;
            contract.ChangePhase(TransportPhase.ArrivalAnimation);
            if (contract.mode == TransportMode.Pod)
            {
                var pod = (SettlementTransportPod)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("SettlementTransport_ActivePod"));
                pod.Contents = new ActiveTransporterInfo();
                contract.localVehicle = pod;
                GenSpawn.Spawn(SkyfallerMaker.MakeSkyfaller(specs.Incoming, pod), cell, map);
            }
            else
            {
                // Keep the real hull Def and comps. The subclass hides controls only on the hired hull.
                var hull = new SettlementTransportShuttle { def = specs.Hull, Rotation = TransportRoute.LandingRotation };
                hull.PostMake();
                hull.PostPostMake();
                if (contract.providerFaction != null) hull.SetFaction(contract.providerFaction);
                contract.localVehicle = hull;
                TransportShip ship = TransportShipMaker.MakeTransportShip(specs.Ship, null, hull);
                contract.ship = ship;
                var unload = (ShipJob_SettlementTransportUnload)ShipJobMaker.MakeShipJob(specs.Unload);
                unload.contractId = contract.id;
                ship.AddJob(unload);
                ship.ArriveAt(cell, parent);
            }
            if (cell != contract.bookedCell)
                Messages.Message((Key + "DivertedCell").Translate(cell.x, cell.z), new TargetInfo(cell, map), MessageTypeDefOf.CautionInput);
            return true;
        }

        private static void StartWorldFallback(TransportContractRecord contract)
        {
            if (contract.deliveredAny || !TransportRoute.IsWorldDestination(contract.tile)
                || !CaravanOwnershipRules.HasCapableOwner(contract.Payload.OfType<Pawn>(), Faction.OfPlayer))
            {
                contract.ChangePhase(TransportPhase.Recovery);
                return;
            }

            contract.worldArrival = true;
            contract.ChangePhase(TransportPhase.ArrivalAnimation);
            EnsureMarker(contract);
            if (contract.Destination.IsLocal)
                Messages.Message((Key + "DivertedWorld").Translate(), new GlobalTargetInfo(contract.tile), MessageTypeDefOf.CautionInput);
        }

        internal static bool Tick(SettlementServicesWorldComponent domain, TransportContractRecord contract)
        {
            int elapsed = Find.TickManager.TicksGame - contract.phaseTick;
            switch (contract.phase)
            {
                case TransportPhase.InFlight:
                    if (elapsed % RecoveryInterval == 0)
                    {
                        var flight = FindWorldObject<SettlementTransportFlight>(contract.flightId);
                        if (flight?.contractId != contract.id) contract.ChangePhase(TransportPhase.Recovery);
                        else if (!TransportRoute.TryGetLayer(contract.tile, out _)) flight.Destroy();
                    }
                    break;

                case TransportPhase.ArrivalAnimation:
                    if (contract.worldArrival)
                    {
                        EnsureMarker(contract);
                        if (elapsed >= ApproachTicks) contract.ChangePhase(TransportPhase.Unloading);
                    }
                    else TickLocalApproach(contract);
                    break;

                case TransportPhase.Unloading:
                    if (contract.worldArrival)
                    {
                        if (elapsed >= WorldUnloadTicks) UnloadWorld(contract);
                    }
                    else if (contract.mode == TransportMode.Pod || elapsed % ShuttleUnloadInterval == 0)
                        UnloadLocal(contract);
                    break;

                case TransportPhase.DepartureAnimation:
                    if (contract.worldArrival ? elapsed >= DepartureTicks : contract.departureVisual?.Spawned != true)
                        FinishDeparture(contract);
                    break;

                case TransportPhase.Preparing:
                    contract.ChangePhase(TransportPhase.Recovery);
                    break;

                case TransportPhase.Recovery:
                    if (elapsed % RecoveryInterval == 0 && Recover(domain, contract)) return true;
                    break;

                case TransportPhase.Delivered:
                    return true;
            }
            return false;
        }

        private static void TickLocalApproach(TransportContractRecord contract)
        {
            Thing vehicle = contract.localVehicle;
            Map safeMap = TransportRoute.FindLocalDestination(contract.Destination)?.Map;
            if (vehicle == null || vehicle.Destroyed || safeMap == null || vehicle.MapHeld != safeMap)
            {
                DiscardLocalVisual(contract);
                if (!TryStartLocal(contract)) StartWorldFallback(contract);
                return;
            }
            if (vehicle is SettlementTransportPod pod && pod.Spawned && pod.ReadyToOpen)
            {
                if (!TransportRoute.IsSafeCell(TransportMode.Pod, null, pod.Position, safeMap, pod))
                {
                    DiscardLocalVisual(contract);
                    if (!TryStartLocal(contract)) StartWorldFallback(contract);
                    return;
                }
                contract.ChangePhase(TransportPhase.Unloading);
                pod.Open();
                contract.localVehicle = null;
            }
            else if (vehicle is SettlementTransportShuttle && vehicle.Spawned
                && (contract.ship?.curJob is ShipJob_SettlementTransportUnload || contract.ship?.Disposed != false))
            {
                // Reconcile a saved hull that landed before its unload job was observed by the world component.
                contract.ChangePhase(TransportPhase.Unloading);
            }
        }

        private static void UnloadLocal(TransportContractRecord contract)
        {
            Map map = TransportRoute.FindLocalDestination(contract.Destination)?.Map;
            IntVec3 spot = contract.landingCell;
            if (contract.mode == TransportMode.Shuttle)
            {
                Thing hull = contract.localVehicle;
                if (map == null || hull?.Spawned != true || hull.Map != map || hull.Position != contract.landingCell)
                {
                    DiscardLocalVisual(contract);
                    if (!TryStartLocal(contract)) StartWorldFallback(contract);
                    return;
                }
                spot = hull.InteractionCell;
            }
            if (map == null || !spot.IsValid || !spot.InBounds(map) || spot.Fogged(map) || !spot.Walkable(map))
            {
                if (contract.mode == TransportMode.Pod && !contract.deliveredAny)
                {
                    if (!TryStartLocal(contract)) StartWorldFallback(contract);
                }
                else EndLocalUnload(contract, map);
                return;
            }

            foreach (Thing thing in contract.Payload.OrderByDescending(t => t is Pawn).ToList())
            {
                bool moved = Place(thing, spot, map, _ => contract.deliveredAny = true);
                if (moved && contract.mode == TransportMode.Shuttle && contract.Payload.Any()) return;
            }
            EndLocalUnload(contract, map);
        }

        private static void EndLocalUnload(TransportContractRecord contract, Map map)
        {
            if (contract.Payload.Any() && !contract.deliveredAny && TransportRoute.IsWorldDestination(contract.tile)
                && CaravanOwnershipRules.HasCapableOwner(contract.Payload.OfType<Pawn>(), Faction.OfPlayer))
            {
                // The booked map could not accept the payload. Let any landed shuttle leave before the world handoff.
                DepartLocalShuttle(contract);
                StartWorldFallback(contract);
                return;
            }

            if (contract.deliveredAny && map != null)
                ReportArrival(contract, new TargetInfo(contract.landingCell, map));
            bool payloadLeft = contract.Payload.Any();
            contract.ChangePhase(payloadLeft || !contract.deliveredAny ? TransportPhase.Recovery : TransportPhase.Delivered);
            if (DepartLocalShuttle(contract) && !payloadLeft && contract.deliveredAny)
                contract.ChangePhase(TransportPhase.DepartureAnimation);
        }

        private static bool DepartLocalShuttle(TransportContractRecord contract)
        {
            if (contract.localVehicle is not SettlementTransportShuttle hull || !hull.Spawned)
            {
                DiscardLocalVisual(contract);
                return false;
            }
            TransportShip ship = contract.ship ?? hull.ShuttleComp.shipParent;
            ThingDef leaving = ship?.def?.leavingSkyfaller;
            if (leaving?.thingClass != typeof(PassengerShuttleLeaving) || hull.TransporterComp.innerContainer.Count != 0)
            {
                DiscardLocalVisual(contract);
                return false;
            }

            Map map = hull.Map;
            IntVec3 position = hull.Position;
            Rot4 rotation = hull.Rotation;
            var carrier = (ActiveTransporter)ThingMaker.MakeThing(ThingDefOf.ActiveDropPod);
            carrier.Contents = new ActiveTransporterInfo();
            carrier.Rotation = rotation;
            var departure = (PassengerShuttleLeaving)SkyfallerMaker.MakeSkyfaller(leaving, carrier);
            departure.createWorldObject = false;
            contract.departureVisual = departure;
            carrier.Contents.SetShuttle(hull);
            if (hull.holdingOwner != carrier.Contents.innerContainer)
                throw new InvalidOperationException("The empty chartered hull could not enter its departure skyfaller.");
            GenSpawn.Spawn(departure, position, map, rotation);
            contract.localVehicle = null;
            ReleaseShip(contract);
            return departure.Spawned;
        }

        private static void UnloadWorld(TransportContractRecord contract)
        {
            if (!TransportRoute.IsWorldDestination(contract.tile)) { contract.ChangePhase(TransportPhase.Recovery); return; }
            var passengers = contract.Payload.OfType<Pawn>().Where(p => !p.Dead && !p.Destroyed)
                .OrderByDescending(p => CaravanOwnershipRules.IsCapableOwner(p, Faction.OfPlayer)).ToList();
            Caravan receiver = FindWorldObject<Caravan>(contract.receiverId);
            if (contract.receiverId < 0 && CaravanOwnershipRules.HasCapableOwner(passengers, Faction.OfPlayer))
            {
                receiver = CaravanMaker.MakeCaravan(Array.Empty<Pawn>(), Faction.OfPlayer, contract.tile, addToWorldPawnsIfNotAlready: false);
                contract.receiverId = receiver.ID;
            }
            if (receiver?.Spawned != true || receiver.Tile != contract.tile || receiver.Faction != Faction.OfPlayer)
            {
                contract.ChangePhase(TransportPhase.Recovery);
                return;
            }

            foreach (Pawn pawn in passengers)
            {
                if (!CaravanOwnershipRules.CaravanHasCapableOwner(receiver) && !CaravanOwnershipRules.IsCapableOwner(pawn, Faction.OfPlayer)) break;
                if (MoveToCaravan(pawn, receiver)) contract.deliveredAny = true;
            }
            if (CaravanOwnershipRules.CaravanHasCapableOwner(receiver))
            {
                receiver.Name = CaravanNameGenerator.GenerateCaravanName(receiver);
                foreach (Thing item in contract.Payload.Where(t => t is not Pawn).ToList())
                    if (MoveToCaravan(item, receiver)) contract.deliveredAny = true;
                receiver.RecacheInventory();
            }
            else if (receiver.pawns.Count == 0) receiver.Destroy();

            if (contract.deliveredAny) ReportArrival(contract, receiver);
            if (contract.mode == TransportMode.Shuttle)
                contract.ChangePhase(TransportPhase.DepartureAnimation);
            else FinishDeparture(contract);
        }

        private static bool MoveToCaravan(Thing thing, Caravan caravan)
        {
            if (thing is not Pawn pawn) return TransportDispatch.Move(thing, TransportBooking.FindCarrier(thing, TransportBooking.DirectPassengers(caravan)));
            if (pawn.Dead || !TransportDispatch.Move(pawn, caravan.pawns)) return false;
            if (!pawn.IsWorldPawn()) Find.WorldPawns.PassToWorld(pawn);
            return caravan.ContainsPawn(pawn) && pawn.IsWorldPawn();
        }

        private static void EnsureMarker(TransportContractRecord contract)
        {
            if (FindWorldObject<SettlementTransportMarker>(contract.markerId) != null) return;
            string name = contract.mode == TransportMode.Pod ? "SettlementTransport_PodMarker" : "SettlementTransport_ShuttleMarker";
            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail(name);
            if (def == null) return;
            var marker = (SettlementTransportMarker)WorldObjectMaker.MakeWorldObject(def);
            marker.Tile = contract.tile;
            marker.contractId = contract.id;
            marker.SetFaction(Faction.OfPlayer);
            contract.markerId = marker.ID;
            Find.WorldObjects.Add(marker);
        }

        private static void FinishDeparture(TransportContractRecord contract)
        {
            contract.departureVisual = null;
            FindWorldObject<SettlementTransportMarker>(contract.markerId)?.Destroy();
            contract.markerId = -1;
            contract.ChangePhase(contract.Payload.Any() || !contract.deliveredAny ? TransportPhase.Recovery : TransportPhase.Delivered);
        }

        private static void ReportArrival(TransportContractRecord contract, LookTargets target)
        {
            if (contract.arrivalReported) return;
            contract.arrivalReported = true;
            string key = contract.Payload.Any() ? Key + "PartialArrival" : Key + "Arrived";
            Messages.Message(key.Translate(), target, MessageTypeDefOf.TaskCompletion);
        }

        private static void DiscardLocalVisual(TransportContractRecord contract)
        {
            Thing visual = contract.localVehicle;
            contract.localVehicle = null;
            ReleaseShip(contract);
            if (visual == null || visual.Destroyed) return;
            if (visual.ParentHolder is Skyfaller incoming && !incoming.Destroyed)
                incoming.Destroy(DestroyMode.QuestLogic);
            else visual.Destroy(DestroyMode.QuestLogic);
        }

        private static void ReleaseShip(TransportContractRecord contract)
        {
            TransportShip ship = contract.ship;
            contract.ship = null;
            if (ship == null || !Find.TransportShipManager.AllTransportShips.Contains(ship)) return;
            if (ship.shipThing != null) ship.Dispose();
            else Find.TransportShipManager.DeregisterShipObject(ship);
        }

        private static bool Recover(SettlementServicesWorldComponent domain, TransportContractRecord contract)
        {
            DiscardLocalVisual(contract);
            if (contract.departureVisual?.Destroyed == true) contract.departureVisual = null;
            FindWorldObject<SettlementTransportMarker>(contract.markerId)?.Destroy();
            contract.markerId = -1;

            contract.recoveryManifest ??= contract.Payload.Select(t => t.LabelCap.ToString()).ToList().ToCommaList();
            Caravan origin = FindWorldObject<Caravan>(contract.sourceId);
            bool canReturn = TransportBooking.IsStationaryReceiver(origin) && CaravanOwnershipRules.CaravanHasCapableOwner(origin);
            if (canReturn)
            {
                foreach (Thing item in contract.Payload.OrderByDescending(t => t is Pawn).ToList()) MoveToCaravan(item, origin);
                origin.RecacheInventory();
            }

            if (!contract.deliveredAny && contract.payment == TransportPaymentState.Paid)
                Refund(contract, canReturn ? origin : null);

            foreach (Thing item in contract.Payload.ToList()) domain.QueueTransportHomeDelivery(contract, item);
            bool held = contract.Payload.Any();
            bool queued = domain.HasQueuedTransportDelivery(contract.id);
            if (!contract.recoveryReported && (held || queued || !contract.deliveredAny || !contract.recoveryManifest.NullOrEmpty()))
            {
                contract.recoveryReported = true;
                string destination = held ? "Held" : queued ? "Home" : "Origin";
                if (contract.recoveryManifest.NullOrEmpty()) destination = "Missing";
                string refund = contract.payment == TransportPaymentState.Refunded ? "Refunded"
                    : contract.payment == TransportPaymentState.RefundQueued ? "RefundQueued"
                    : contract.payment == TransportPaymentState.Unpaid ? "Unpaid" : "FareRetained";
                string outcome = (Key + "Recovery." + destination).Translate(contract.recoveryManifest);
                Messages.Message(outcome + " " + (Key + "Recovery." + refund).Translate(contract.fare),
                    canReturn ? new LookTargets(origin) : LookTargets.Invalid, MessageTypeDefOf.CautionInput);
            }
            return !held && !queued && contract.refundRemaining <= 0 && contract.departureVisual?.Spawned != true;
        }

        private static void Refund(TransportContractRecord contract, Caravan origin)
        {
            if (contract.refundRemaining < 0) contract.refundRemaining = contract.fare;
            while (origin != null && contract.refundRemaining > 0)
            {
                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = Math.Min(contract.refundRemaining, silver.def.stackLimit);
                ThingOwner inventory = TransportBooking.FindCarrier(silver, TransportBooking.DirectPassengers(origin));
                if (inventory != null && inventory.TryAdd(silver, canMergeWithExistingStacks: false))
                    contract.refundRemaining -= silver.stackCount;
                else
                {
                    silver.Destroy();
                    break;
                }
            }
            if (contract.refundRemaining > 0)
                contract.payment = TransportPaymentState.RefundQueued;
            else contract.payment = TransportPaymentState.Refunded;
        }

        internal static void TryDeliverQueuedRefund(TransportContractRecord contract)
        {
            Map home = TradeUtility.PlayerHomeMapWithMostLaunchableSilver() ?? Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
            if (home == null) return;
            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = Math.Min(contract.refundRemaining, silver.def.stackLimit);
            try
            {
                Place(silver, DropCellFinder.TradeDropSpot(home), home, count => contract.refundRemaining -= count);
            }
            finally
            {
                if (!silver.Spawned && !silver.Destroyed) silver.Destroy();
            }
        }

        internal static bool TryDeliverQueuedHome(Thing thing, ThingOwner custody)
        {
            if (thing == null || thing.Destroyed || !custody.Contains(thing)) return true;
            Map home = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
            if (home == null) return false;
            Place(thing, DropCellFinder.TradeDropSpot(home), home, null);
            return !custody.Contains(thing);
        }

        private static bool Place(Thing thing, IntVec3 spot, Map map, Action<int> delivered)
        {
            if (thing.Destroyed || !spot.IsValid || !spot.InBounds(map)) return false;
            ThingOwner custody = thing.holdingOwner;
            bool moved = false;
            void RecordPlacement(Thing placed, int count)
            {
                if (count <= 0 || !placed.Spawned || placed.Map != map) return;
                moved = true;
                delivered?.Invoke(count);
            }
            try
            {
                GenPlace.TryPlaceThing(thing, spot, map, ThingPlaceMode.Near, out _, RecordPlacement,
                    cell => cell.InBounds(map) && cell.Walkable(map) && !cell.Fogged(map)
                        && !cell.GetThingList(map).Any(t => t is Skyfaller || t is IActiveTransporter
                            || GenSpawn.SpawningWipes(thing.def, t.def, ignoreDestroyable: true)));
            }
            catch (Exception ex)
            {
                SupportLog.Warning($"Placement of transport payload {thing} was interrupted: {ex}");
                if (thing.Spawned) RecordPlacement(thing, thing.stackCount);
                else if (!thing.Destroyed && thing.holdingOwner == null) custody?.TryAdd(thing, canMergeWithExistingStacks: false);
            }
            if (thing is Pawn pawn && pawn.Spawned && pawn.Map == map && pawn.guest?.IsPrisoner == true)
                pawn.guest.WaitInsteadOfEscapingForDefaultTicks();
            return moved;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Compat.VehicleFramework;
using Settlement_Services.Framework.Custody;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Delivery;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Validation;
using Settlement_Services.Services.Stashing;
using Verse;

namespace Settlement_Services.Services.Transport
{
    internal readonly record struct TransportStack(Thing Item, ThingOwner Owner, int Count);
    internal readonly record struct TransportRemainder(TransportStack Stack, ThingOwner Receiver)
    {
        internal bool IsAbandoned => Receiver == null;
    }
    internal readonly record struct TransportPassenger(Pawn Pawn, ThingOwner Owner, float Mass, ThingDef Race, Faction Faction, Faction Host);

    internal sealed class TransportRequest
    {
        internal readonly Settlement Provider;
        internal readonly Caravan Caravan;
        internal readonly HashSet<Pawn> Passengers = new HashSet<Pawn>();
        internal readonly Dictionary<Thing, int> Cargo = new Dictionary<Thing, int>();
        internal TransportMode Mode;
        internal TransportDestination Destination = TransportDestination.None;

        internal TransportRequest(Settlement provider, Caravan caravan)
        {
            Provider = provider;
            Caravan = caravan;
        }
    }

    internal sealed record TransportQuote(TransportMode Mode, TransportDestination Destination, TransportSpecs Specs,
        TransportFare Fare, float Mass, List<TransportPassenger> Passengers, List<TransportStack> Cargo,
        List<TransportStack> Payment, List<TransportRemainder> Remainders, List<TransportStack> PersonalGear)
    {
        internal bool Matches(TransportQuote other) => other != null && Mode == other.Mode && Destination == other.Destination
            && Specs == other.Specs && Fare == other.Fare && Mass == other.Mass
            && Passengers.SequenceEqual(other.Passengers) && Cargo.SequenceEqual(other.Cargo)
            && Payment.SequenceEqual(other.Payment) && Remainders.SequenceEqual(other.Remainders) && PersonalGear.SequenceEqual(other.PersonalGear);
    }

    internal static class TransportBooking
    {
        internal const string Key = "SettlementServices.Transport.";
        private const string Error = Key + "Error.";
        internal const string TransferError = Error + "Transfer";
        internal const string Changed = Error + "Changed";
        internal const string InsufficientSilver = "SettlementServices.Error.InsufficientCaravanSilver";
        internal const string ProviderRequired = Error + "Provider";
        internal static SettlementServiceDef ServiceDef => DefDatabase<SettlementServiceDef>.GetNamedSilentFail("SettlementService_Transport");

        internal static string ProviderBlock(Settlement provider, Caravan caravan, SettlementServiceDef def = null)
        {
            if (provider?.Faction == null || provider.Faction == Faction.OfPlayer || provider.Destroyed || !provider.Spawned)
                return ProviderRequired;
            if (provider.Faction.def.techLevel < TechLevel.Industrial) return "SettlementServices.Error.TechLevelTooLow";
            if (caravan == null) return null;
            if (VehicleFrameworkAdapter.IsVehicleCaravan(caravan) && !VehicleFrameworkAdapter.IsStationaryVehicleCaravan(caravan))
                return "SettlementServices.Error.CollectCaravanMoving";
            string visitBlock = StashService.VisitBlock(provider, caravan);
            if (visitBlock != null) return visitBlock;
            def ??= ServiceDef;
            if (def == null) return ProviderRequired;
            var request = new SettlementServiceRequest
            {
                channel = RequestChannel.InPerson,
                settlement = provider,
                negotiator = BestCaravanPawnUtility.FindBestNegotiator(caravan),
            };
            return SettlementServiceValidator.Validate(def, request, out string error) ? null : error;
        }

        internal static bool IsStationaryReceiver(Caravan caravan) => CaravanOwnershipRules.IsStationaryPlayerCaravan(caravan)
            && (!VehicleFrameworkAdapter.IsVehicleCaravan(caravan) || VehicleFrameworkAdapter.IsStationaryVehicleCaravan(caravan));

        internal static IEnumerable<Pawn> DirectPassengers(Caravan caravan) => caravan?.pawns == null
            ? Enumerable.Empty<Pawn>() : caravan.pawns.InnerListForReading.Where(p => p != null && p.holdingOwner == caravan.pawns
                && !VehicleFrameworkAdapter.IsVehiclePawn(p));

        internal static bool CanBookPassenger(Pawn pawn, SettlementServicesWorldComponent domain)
        {
            if (pawn.Destroyed || pawn.Dead || pawn.IsQuestLodger() || !pawn.questTags.NullOrEmpty() || domain.IsTargetReserved(pawn)
                || pawn.carryTracker?.CarriedThing != null) return false;
            if (!(pawn.IsColonist && pawn.HostFaction == null) && !pawn.IsSlaveOfColony && !pawn.IsPrisonerOfColony
                && !(pawn.RaceProps.Animal && pawn.Faction == Faction.OfPlayer)) return false;

            var holders = new List<IThingHolder>();
            pawn.GetChildHolders(holders);
            holders.AddRange(pawn.AllComps.OfType<IThingHolder>());
            return !ContainsProtected(holders, domain) && (pawn.inventory == null || pawn.inventory.innerContainer.All(item =>
                item.def.category == ThingCategory.Item || CustodyCargoPolicy.ShouldRetainWithPawn(item, pawn)));
        }

        private static bool IsProtected(Thing item, SettlementServicesWorldComponent domain) => item == null || item.Destroyed
            || !item.def.destroyable || !item.questTags.NullOrEmpty() || domain.IsTargetReserved(item);

        private static bool ContainsProtected(List<IThingHolder> holders, SettlementServicesWorldComponent domain)
        {
            var seen = new HashSet<IThingHolder>();
            for (int i = 0; i < holders.Count; i++)
            {
                IThingHolder holder = holders[i];
                if (holder == null || !seen.Add(holder)) continue;
                ThingOwner contents = holder.GetDirectlyHeldThings();
                if (contents != null && contents.Any(t => t is Pawn || IsProtected(t, domain))) return true;
                holder.GetChildHolders(holders);
            }
            return false;
        }

        private static bool CanBookCargo(Thing item, Pawn pawn, SettlementServicesWorldComponent domain)
        {
            if (IsProtected(item, domain) || item.def.category != ThingCategory.Item || CustodyCargoPolicy.ShouldRetainWithPawn(item, pawn)) return false;
            var holders = new List<IThingHolder>();
            if (item is IThingHolder holder) holders.Add(holder);
            if (item is ThingWithComps compThing) holders.AddRange(compThing.AllComps.OfType<IThingHolder>());
            return !ContainsProtected(holders, domain);
        }

        internal static IEnumerable<TransportStack> LooseInventory(IEnumerable<Pawn> pawns, SettlementServicesWorldComponent domain)
        {
            foreach (Pawn pawn in pawns)
            {
                ThingOwner inventory = pawn.inventory?.innerContainer;
                if (inventory == null) continue;
                foreach (Thing item in inventory)
                    if (item.holdingOwner == inventory && CanBookCargo(item, pawn, domain))
                        yield return new TransportStack(item, inventory, item.stackCount);
            }
        }

        internal static ThingOwner FindCarrier(Thing item, IEnumerable<Pawn> pawns)
        {
            // Use the vanilla carrying priorities with a stable order so redistributing a load gives a repeatable quote.
            return pawns.Where(p => !p.Dead && !p.Destroyed && !VehicleFrameworkAdapter.IsVehiclePawn(p) && p.inventory != null)
                .OrderByDescending(p => MassUtility.CanEverCarryAnything(p) && !MassUtility.IsOverEncumbered(p))
                .ThenByDescending(MassUtility.CanEverCarryAnything)
                .Select(p => p.inventory.innerContainer).FirstOrDefault(o => o.CanAcceptAnyOf(item, false));
        }

        internal static float PersonalMass(Pawn pawn)
        {
            float mass = ServicePodDeliveryPlanner.MassOf(pawn, 1);
            if (pawn.inventory != null)
                foreach (Thing item in pawn.inventory.innerContainer)
                    if (!CustodyCargoPolicy.ShouldRetainWithPawn(item, pawn))
                        mass -= ServicePodDeliveryPlanner.MassOf(item, item.stackCount);
            return mass;
        }

        internal static IEnumerable<Thing> PersonalGear(Pawn pawn)
        {
            if (pawn.equipment != null)
                foreach (Thing item in pawn.equipment.AllEquipmentListForReading) yield return item;
            if (pawn.apparel != null)
                foreach (Thing item in pawn.apparel.WornApparel) yield return item;
            if (pawn.inventory != null)
                foreach (Thing item in pawn.inventory.innerContainer)
                    if (CustodyCargoPolicy.ShouldRetainWithPawn(item, pawn)) yield return item;
        }

        internal static string BuildQuote(TransportRequest request, out TransportQuote quote, out TransportFare? routeFare)
        {
            quote = null;
            routeFare = null;
            var domain = SettlementServicesWorldComponent.Current;
            if (domain == null || request.Caravan == null) return ProviderRequired;
            string error = ProviderBlock(request.Provider, request.Caravan);
            if (error != null) return error;
            error = TransportRoute.ResolveSpecs(request.Mode, request.Provider.Faction.def.techLevel, out TransportSpecs specs);
            if (error != null) return error;
            error = TransportRoute.RouteFare(request.Provider.Tile, request.Destination.Tile, specs, out TransportFare fare);
            if (error != null) return error;
            routeFare = fare;

            List<Pawn> direct = DirectPassengers(request.Caravan).ToList();
            List<Pawn> selected = direct.Where(request.Passengers.Contains).ToList();
            if (selected.Count != request.Passengers.Count || selected.Any(p => !CanBookPassenger(p, domain))) return Error + "Manifest";
            List<TransportStack> available = LooseInventory(direct, domain).ToList();
            var cargo = new List<TransportStack>();
            foreach (TransportStack stack in available)
            {
                if (!request.Cargo.TryGetValue(stack.Item, out int count)) continue;
                if (count < 1 || count > stack.Count) return Error + "Manifest";
                cargo.Add(stack with { Count = count });
            }
            if (cargo.Count != request.Cargo.Count) return Error + "Manifest";
            if (selected.Count + cargo.Count == 0) return Error + "Empty";

            var passengers = selected.Select(p => new TransportPassenger(p, p.holdingOwner, PersonalMass(p), p.def, p.Faction, p.HostFaction)).ToList();
            float mass = passengers.Sum(p => p.Mass) + cargo.Sum(s => ServicePodDeliveryPlanner.MassOf(s.Item, s.Count));
            if (!float.IsFinite(mass) || mass < 0 || mass > specs.Capacity + TransportRoute.MassTolerance) return Error + "Capacity";

            if (request.Destination.IsLocal)
            {
                var parent = TransportRoute.FindLocalDestination(request.Destination);
                if (parent == null) return TransportRoute.InvalidDestination;
                if (!TransportRoute.IsSafeCell(request.Mode, specs.Hull, request.Destination.Cell, parent.Map)) return TransportRoute.LandingBlocked;
            }
            else
            {
                if (!TransportRoute.CanBookWorldDestination(request.Destination.Tile)) return TransportRoute.InvalidDestination;
                if (!CaravanOwnershipRules.HasCapableOwner(selected, Faction.OfPlayer)) return Error + "DestinationOwner";
                if (cargo.Any(s => FindCarrier(s.Item, selected) == null)) return Error + "NoCarrier";
            }

            error = CaravanOwnershipRules.GetDepartureBlockKey(request.Caravan, selected, request.Provider);
            if (error != null) return error;
            if (request.Caravan.PawnsListForReading.Any(p => !request.Passengers.Contains(p))
                && !CaravanOwnershipRules.HasCapableOwner(request.Caravan.PawnsListForReading.Where(p => !request.Passengers.Contains(p)), Faction.OfPlayer))
                return CaravanOwnershipRules.LastOwnerDepartureKey;

            // Spend loose silver carried by departing passengers first so it can satisfy their cargo disposition.
            var payment = new List<TransportStack>();
            int due = fare.Total;
            foreach (TransportStack stack in available.Where(s => s.Item.def == ThingDefOf.Silver)
                .OrderByDescending(s => s.Owner.Owner is Pawn_InventoryTracker inventory && request.Passengers.Contains(inventory.pawn)))
            {
                request.Cargo.TryGetValue(stack.Item, out int reserved);
                int count = System.Math.Min(due, stack.Count - reserved);
                if (count > 0) payment.Add(stack with { Count = count });
                due -= count;
                if (due == 0) break;
            }
            if (due > 0) return InsufficientSilver;

            Dictionary<Thing, int> paid = payment.ToDictionary(s => s.Item, s => s.Count);
            List<Pawn> staying = direct.Where(p => !request.Passengers.Contains(p)).ToList();
            bool emptiesCaravan = request.Caravan.pawns.Count == selected.Count;
            var remainders = new List<TransportRemainder>();
            foreach (TransportStack stack in LooseInventory(selected, domain))
            {
                request.Cargo.TryGetValue(stack.Item, out int shipped);
                paid.TryGetValue(stack.Item, out int spent);
                int count = stack.Count - shipped - spent;
                if (count == 0) continue;
                ThingOwner receiver = emptiesCaravan ? null : FindCarrier(stack.Item, staying);
                if (receiver == null && !emptiesCaravan) return Error + "Remainder";
                remainders.Add(new TransportRemainder(stack with { Count = count }, receiver));
            }

            var gear = selected.SelectMany(PersonalGear).Select(t => new TransportStack(t, t.holdingOwner, t.stackCount)).ToList();
            quote = new TransportQuote(request.Mode, request.Destination, specs, fare, mass, passengers, cargo, payment, remainders, gear);
            return null;
        }
    }
}

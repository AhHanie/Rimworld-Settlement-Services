using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Compat.VehicleFramework;

namespace Settlement_Services.Framework.Custody
{
    internal static class CaravanOwnershipRules
    {
        internal const string LastOwnerDepartureKey = "SettlementServices.Error.LastOwnerDeparture";
        internal const string NoCapableOwnerKey = "SettlementServices.Error.NoCapableCaravanOwner";

        internal static bool IsCapableOwner(Pawn pawn, Faction caravanFaction)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.Downed || pawn.InMentalState) return false;
            if (caravanFaction == null) return false;
            return CaravanUtility.IsOwner(pawn, caravanFaction);
        }

        internal static bool HasCapableOwner(IEnumerable<Pawn> pawns, Faction caravanFaction) =>
            pawns != null && pawns.Any(p => IsCapableOwner(p, caravanFaction));

        internal static bool CaravanHasCapableOwner(Caravan caravan) =>
            caravan != null && !caravan.Destroyed && HasCapableOwner(caravan.PawnsListForReading, caravan.Faction);

        internal static string GetDepartureBlockKey(Caravan caravan, IReadOnlyCollection<Pawn> departing,
            Settlement intendedSettlement = null, bool allowVehicleParking = false)
        {
            if (caravan == null || caravan.Destroyed || departing == null || departing.Count == 0) return null;

            List<Pawn> remaining = caravan.PawnsListForReading.Where(p => !departing.Contains(p)).ToList();
            if (remaining.Count == 0) return null;

            if (HasCapableOwner(remaining, caravan.Faction)) return null;
            return allowVehicleParking && CanParkVehicleCaravanForService(caravan, departing, intendedSettlement)
                ? null : LastOwnerDepartureKey;
        }

        internal static bool CanParkVehicleCaravanForService(Caravan caravan, IReadOnlyCollection<Pawn> departing, Settlement intendedSettlement)
        {
            if (intendedSettlement == null || !IsStationaryPlayerCaravan(caravan)
                || CaravanVisitUtility.SettlementVisitedNow(caravan) != intendedSettlement
                || !VehicleFrameworkAdapter.IsStationaryVehicleCaravan(caravan)
                || departing == null || departing.Count == 0) return false;

            List<Pawn> members = caravan.PawnsListForReading;
            if (!departing.Any(p => IsCapableOwner(p, caravan.Faction))
                || members.Where(p => !departing.Contains(p)).Any(p => IsCapableOwner(p, caravan.Faction))) return false;

            foreach (Pawn pawn in departing)
            {
                if (pawn == null || !members.Contains(pawn)) return false;
                if (caravan.pawns.Contains(pawn)) continue;

                Thing vehicle = VehicleFrameworkAdapter.FindVehicleContainingOccupant(caravan, pawn);
                if (vehicle == null || !caravan.pawns.Contains(vehicle as Pawn)) return false;
            }

            List<Pawn> remaining = members.Where(p => !departing.Contains(p)).ToList();
            return remaining.Count > 0 && remaining.All(p => VehicleFrameworkAdapter.IsVehicle(p) && caravan.pawns.Contains(p));
        }

        internal static bool IsStationaryPlayerCaravan(Caravan caravan) =>
            caravan != null && !caravan.Destroyed && caravan.Spawned && caravan.IsPlayerControlled && !caravan.pather.Moving;

        internal static bool IsEligibleReceiver(Caravan caravan, IEnumerable<Pawn> incoming) =>
            IsStationaryPlayerCaravan(caravan) && GroupWouldHaveCapableOwner(caravan, incoming);

        internal static bool GroupWouldHaveCapableOwner(Caravan caravan, IEnumerable<Pawn> incoming) =>
            HasCapableOwner(caravan.PawnsListForReading.Concat(incoming ?? Enumerable.Empty<Pawn>()), caravan.Faction);
    }
}

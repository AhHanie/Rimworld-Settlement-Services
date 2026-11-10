using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

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

        internal static string GetDepartureBlockKey(Caravan caravan, IReadOnlyCollection<Pawn> departing)
        {
            if (caravan == null || caravan.Destroyed || departing == null || departing.Count == 0) return null;

            List<Pawn> remaining = caravan.PawnsListForReading.Where(p => !departing.Contains(p)).ToList();
            if (remaining.Count == 0) return null;

            return HasCapableOwner(remaining, caravan.Faction) ? null : LastOwnerDepartureKey;
        }

        internal static bool IsStationaryPlayerCaravan(Caravan caravan) =>
            caravan != null && !caravan.Destroyed && caravan.Spawned && caravan.IsPlayerControlled && !caravan.pather.Moving;

        internal static bool IsEligibleReceiver(Caravan caravan, IEnumerable<Pawn> incoming) =>
            IsStationaryPlayerCaravan(caravan) && GroupWouldHaveCapableOwner(caravan, incoming);

        internal static bool GroupWouldHaveCapableOwner(Caravan caravan, IEnumerable<Pawn> incoming) =>
            HasCapableOwner(caravan.PawnsListForReading.Concat(incoming ?? Enumerable.Empty<Pawn>()), caravan.Faction);
    }
}

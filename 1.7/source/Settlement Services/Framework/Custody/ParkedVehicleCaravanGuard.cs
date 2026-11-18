using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Compat.VehicleFramework;

namespace Settlement_Services.Framework.Custody
{
    internal sealed class ParkedVehicleCaravanGuard
    {
        private static readonly ParkedVehicleCaravanGuard Inactive = new ParkedVehicleCaravanGuard();

        private readonly Caravan original;
        private readonly int originalId;
        private readonly PlanetTile originalTile;
        private readonly List<Pawn> vehicles;

        private ParkedVehicleCaravanGuard() { }

        private ParkedVehicleCaravanGuard(Caravan caravan)
        {
            original = caravan;
            originalId = caravan.ID;
            originalTile = caravan.Tile;
            vehicles = caravan.pawns.InnerListForReading.Where(VehicleFrameworkAdapter.IsVehicle).ToList();
        }

        internal static ParkedVehicleCaravanGuard Capture(Caravan caravan, IReadOnlyCollection<Pawn> departing, Settlement settlement)
        {
            if (departing == null || departing.Count == 0
                || CaravanOwnershipRules.GetDepartureBlockKey(caravan, departing) == null
                || !CaravanOwnershipRules.CanParkVehicleCaravanForDeparture(caravan, departing, settlement))
                return Inactive;

            return new ParkedVehicleCaravanGuard(caravan);
        }

        internal bool IsIntact(Caravan current)
        {
            if (original == null) return true;
            if (current != original || original.Destroyed || !original.Spawned
                || original.ID != originalId || original.Tile != originalTile || vehicles.Count == 0)
                return false;

            List<Pawn> members = original.PawnsListForReading;
            if (vehicles.Any(v => v.Destroyed || !original.pawns.Contains(v) || !members.Contains(v))) return false;
            return members.All(p => VehicleFrameworkAdapter.IsVehicle(p) && original.pawns.Contains(p));
        }
    }
}

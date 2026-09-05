using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Compatibility;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Compat.VehicleFramework
{
    internal sealed class VehicleFrameworkCustodyLifecycle : ICompatibilityCustodyLifecycle
    {
        public bool Handles(ServiceJobContext context, Thing thing) => VehicleFrameworkAdapter.IsVehicle(thing);

        public bool TryPrepareForTargetCustody(ServiceJobContext context, Caravan origin, Thing thing, out Caravan resultCaravan, out string errorKey)
        {
            resultCaravan = origin;
            errorKey = null;

            if (origin == null || !(thing is Pawn vehicle)) return true;

            if (!VehicleFrameworkAdapter.TryDisembarkAllOccupants(vehicle))
            {
                errorKey = "SettlementServices.Error.VehicleOccupantsCouldNotDisembark";
                return false;
            }

            Faction faction = origin.Faction;
            PlanetTile tile = origin.Tile;
            List<Pawn> remainingMembers = origin.PawnsListForReading.Where(p => p != vehicle).ToList();

            origin.RemovePawn(vehicle);

            resultCaravan = ResolveLiveCaravan(origin, faction, tile, remainingMembers);
            return true;
        }

        public Caravan ReturnPawnToCaravan(ServiceJobContext context, Caravan receiver, Pawn pawn)
        {
            Faction faction = receiver.Faction;
            PlanetTile tile = receiver.Tile;

            receiver.AddPawn(pawn, true);

            return ResolveLiveCaravan(receiver, faction, tile, new List<Pawn> { pawn });
        }

        private static Caravan ResolveLiveCaravan(Caravan caravan, Faction faction, PlanetTile tile, List<Pawn> members)
        {
            if (!caravan.Destroyed) return caravan;
            if (members.Count == 0) return null;

            return Find.WorldObjects.Caravans.FirstOrDefault(c =>
                !c.Destroyed && c.Faction == faction && c.Tile == tile && members.Any(c.PawnsListForReading.Contains));
        }
    }
}

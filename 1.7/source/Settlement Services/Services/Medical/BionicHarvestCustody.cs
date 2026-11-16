using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Custody;

namespace Settlement_Services.Services.Medical
{
    internal static class BionicHarvestCustody
    {
        internal static void Detach(Pawn pawn)
        {
            if (pawn.holdingOwner != null)
            {
                var caravan = pawn.holdingOwner.Owner as Caravan;
                pawn.holdingOwner.Remove(pawn);
                if (caravan != null && !caravan.Destroyed && caravan.PawnsListForReading.Count == 0) caravan.Destroy();
            }
            if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
        }

        internal static void Restore(Pawn pawn)
        {
            if (!pawn.Dead && !Find.WorldPawns.Contains(pawn)) TargetCustodyService.ParkPawnInWorld(pawn);
        }
    }
}

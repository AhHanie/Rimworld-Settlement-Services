using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Compatibility
{
    internal interface ICompatibilityCustodyLifecycle
    {
        bool Handles(ServiceJobContext context, Thing thing);

        bool TryPrepareForTargetCustody(ServiceJobContext context, Caravan origin, Thing thing, out Caravan resultCaravan, out string errorKey);

        Caravan ReturnPawnToCaravan(ServiceJobContext context, Caravan receiver, Pawn pawn);
    }
}

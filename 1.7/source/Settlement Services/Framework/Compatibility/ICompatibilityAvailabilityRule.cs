using RimWorld.Planet;

namespace Settlement_Services.Framework.Compatibility
{
    internal interface ICompatibilityAvailabilityRule
    {
        string GetBlockReason(Settlement settlement);
    }
}

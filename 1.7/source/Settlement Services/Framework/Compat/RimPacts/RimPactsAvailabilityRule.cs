using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Framework.Compatibility;

namespace Settlement_Services.Framework.Compat.RimPacts
{
    internal sealed class RimPactsAvailabilityRule : ICompatibilityAvailabilityRule
    {
        public string GetBlockReason(Settlement settlement)
        {
            Faction faction = settlement?.Faction;
            switch (RimPactsAdapter.GetServiceBlock(faction))
            {
                case RimPactsServiceBlock.Embargoed: return "SettlementServices.Error.RimPactsEmbargoed";
                case RimPactsServiceBlock.CivilWar: return "SettlementServices.Error.RimPactsCivilWar";
                default: return null;
            }
        }
    }
}

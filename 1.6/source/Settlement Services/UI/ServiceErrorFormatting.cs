using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.UI
{
    internal static class ServiceErrorFormatting
    {
        private const string InsufficientGoodwillKey = "SettlementServices.Error.InsufficientGoodwill";

        public static TaggedString Format(string errorKey, SettlementServiceDef def, Settlement settlement)
        {
            if (errorKey == InsufficientGoodwillKey && def != null && settlement?.Faction != null)
                return errorKey.Translate(def.minimumGoodwill.ToStringWithSign(), settlement.Faction.PlayerGoodwill.ToStringWithSign());

            return errorKey.Translate();
        }
    }
}

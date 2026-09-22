using RimWorld.Planet;
using Settlement_Services.Framework.Compatibility;

namespace Settlement_Services.Framework.Compat.Empire
{
    internal sealed class EmpireAvailabilityRule : ICompatibilityAvailabilityRule
    {
        internal const string UnderAttackErrorKey = "SettlementServices.Error.EmpireSettlementUnderAttack";

        public string GetBlockReason(Settlement settlement) =>
            EmpireAdapter.IsUnderAttack(settlement) ? UnderAttackErrorKey : null;
    }
}

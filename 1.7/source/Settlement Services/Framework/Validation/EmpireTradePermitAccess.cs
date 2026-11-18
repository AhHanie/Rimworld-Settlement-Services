using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Framework.Validation
{
    internal static class EmpireTradePermitAccess
    {
        private const string PermitRequiredKey = "SettlementServices.Error.EmpireTradePermitRequired";

        internal static bool IsVanillaEmpireSettlement(Settlement settlement) =>
            ModsConfig.RoyaltyActive && settlement?.Faction?.def == FactionDefOf.Empire;

        internal static string GetBlockReason(Settlement settlement, Caravan caravan)
        {
            if (!IsVanillaEmpireSettlement(settlement)) return null;

            if (caravan == null || caravan.Destroyed || !caravan.Spawned || !caravan.IsPlayerControlled
                || CaravanVisitUtility.SettlementVisitedNow(caravan) != settlement)
                return PermitRequiredKey;

            RoyalTitlePermitDef permit = DefDatabase<RoyalTitlePermitDef>.GetNamedSilentFail("TradeSettlement");
            if (permit == null)
            {
                SupportLog.Error("Royalty's TradeSettlement permit definition is missing; Empire service purchases are blocked.");
                return PermitRequiredKey;
            }

            foreach (Pawn pawn in caravan.PawnsListForReading)
            {
                if (pawn != null && !pawn.Destroyed && !pawn.Dead
                    && pawn.royalty?.HasPermit(permit, settlement.Faction) == true)
                    return null;
            }

            return PermitRequiredKey;
        }
    }
}

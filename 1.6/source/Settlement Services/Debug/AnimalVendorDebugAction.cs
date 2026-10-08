using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Services.Animals;

namespace Settlement_Services.Debug
{
    public static class AnimalVendorDebugAction
    {
        [DebugAction("Settlement Services", "Make animal vendor offer due", actionType = DebugActionType.ToolWorld, allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        private static void MakeAnimalVendorOfferDue()
        {
            Settlement settlement = Find.WorldObjects.SettlementAt(GenWorld.MouseTile());
            if (settlement == null)
            {
                Logger.Message("No settlement under mouse.");
                return;
            }

            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (!domain.TryGetAnimalVendorRefreshTick(settlement.ID, out _))
            {
                Logger.Message($"{settlement.Name}: the animal vendor has not been queried yet, so it has no scheduled refresh.");
                return;
            }

            domain.MakeAnimalVendorOfferDue(settlement.ID);
            Logger.Message($"{settlement.Name}: animal vendor offer is now due; it is replaced the next time the vendor is queried.");
        }

        [DebugAction("Settlement Services", "Log animal vendor offer", actionType = DebugActionType.ToolWorld, allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        private static void LogAnimalVendorOffer()
        {
            Settlement settlement = Find.WorldObjects.SettlementAt(GenWorld.MouseTile());
            if (settlement == null)
            {
                Logger.Message("No settlement under mouse.");
                return;
            }

            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (!domain.TryGetAnimalVendorRefreshTick(settlement.ID, out int nextRefreshTick))
            {
                Logger.Message($"{settlement.Name}: the animal vendor has not been queried yet.");
                return;
            }

            SettlementRecord record = domain.SettlementRecordsRaw.FirstOrDefault(r => r.settlementWorldObjectId == settlement.ID);
            Logger.Message($"{settlement.Name}: next refresh at tick {nextRefreshTick} (now {Find.TickManager.TicksGame}), {record?.animalVendorOffers.Count ?? 0} animal(s) on offer.");
            if (record == null) return;

            foreach (AnimalVendorOfferRecord offer in record.animalVendorOffers)
                Logger.Message($"  #{offer.offerId}: {offer.pawn.LabelShortCap} ({offer.pawn.def.defName}, {offer.pawn.gender}, market value {offer.pawn.MarketValue:F0})");
        }
    }
}

using System;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Compat.VehicleFramework;
using Settlement_Services.Framework.Compatibility;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Registry;
using Settlement_Services.Framework.Validation;
using Settlement_Services.UI.Interaction;
using Verse;

namespace Settlement_Services.Services.Land
{
    internal static partial class LandPlotService
    {
        internal const string Key = "SettlementServices.Land.";
        internal const string Invalid = Key + "Invalid";
        internal const string MapLimit = Key + "MapLimit";
        internal const string PatchFailure = Key + "Unavailable";
        internal const string MissingHost = "SettlementServices.Error.SettlementNoLongerExists";
        internal const string SilverShort = "SettlementServices.Error.InsufficientCaravanSilver";
        internal const string BunkerGeneratorName = "SettlementServices_LandBunker";
        private const string MapDefName = "SettlementServices_LandMap";

        internal static SettlementServiceDef Def => DefDatabase<SettlementServiceDef>.GetNamedSilentFail("SettlementService_LandPlot");
        internal static WorldObjectDef ParentDef => DefDatabase<WorldObjectDef>.GetNamedSilentFail(MapDefName);
        internal static MapGeneratorDef GeneratorFor(bool bunkerInterior) =>
            DefDatabase<MapGeneratorDef>.GetNamedSilentFail(bunkerInterior ? BunkerGeneratorName : MapDefName);

        internal static string GenerationBlock(bool bunkerInterior)
        {
            if (ParentDef == null) return PatchFailure;
            if (GeneratorFor(bunkerInterior) != null) return null;
            return bunkerInterior ? Key + "BunkerUnavailable" : PatchFailure;
        }

        internal static bool EligibleHost(Settlement settlement) =>
            settlement?.Faction is Faction faction && faction != Faction.OfPlayer && faction.def.humanlikeFaction
            && LandPlotTravel.TileExists(settlement.Tile) && settlement.Tile.LayerDef.canFormCaravans
            && !settlement.Tile.LayerDef.isSpace;

        internal static string CaravanBlock(Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed || !caravan.Spawned || !caravan.IsPlayerControlled)
                return "SettlementServices.Error.CollectCaravanIneligible";
            if (caravan.pather.Moving) return "SettlementServices.Error.CollectCaravanMoving";
            if (caravan.GetType() != typeof(Caravan) || caravan.Shuttle != null || VehicleFrameworkAdapter.IsVehicleCaravan(caravan)
                || caravan.PawnsListForReading.Any(p => p == null || p.Dead || p.Destroyed || p.Spawned
                    || !caravan.pawns.Contains(p) || p.inventory == null || VehicleFrameworkAdapter.IsVehicle(p)))
                return Key + "StandardCaravan";
            if (!SettlementServicesInteractionCommands.HasVisitingColonist(caravan))
                return "SettlementServices.Command.NoVisitingColonist";
            return null;
        }

        private static string VisitTransactionBlock(Settlement settlement, Caravan caravan,
            out SettlementServicesWorldComponent domain)
        {
            domain = null;
            if (!LandPlotPatches.Ready) return PatchFailure;
            if (settlement == null || settlement.Destroyed || WorldObjectLookup.ResolveSettlement(settlement.ID) != settlement)
                return MissingHost;
            string blocked = CaravanBlock(caravan);
            if (blocked != null) return blocked;
            if (settlement.Faction?.HostileTo(Faction.OfPlayer) == true) return "SettlementServices.Error.FactionHostile";
            if (CaravanVisitUtility.SettlementVisitedNow(caravan) != settlement)
                return "SettlementServices.Error.CollectCaravanWrongTile";
            if (!EligibleHost(settlement)) return Key + "HostIneligible";
            if (!SettlementServiceRegistry.IsEnabled(Def)) return "SettlementServices.Error.ServiceDisabled";
            blocked = SettlementServicesCompatibilityRegistry.GetRequestBlockReason(settlement)
                ?? EmpireTradePermitAccess.GetBlockReason(settlement, caravan);
            if (blocked != null) return blocked;
            if (!SettlementServiceValidator.StructuralEligibility(Def, settlement, out blocked)
                || !SettlementServiceValidator.GoodwillMet(Def, settlement, out blocked)) return blocked ?? Invalid;

            domain = SettlementServicesWorldComponent.Current;
            if (domain == null || !domain.LandPlotsReady) return Invalid;
            return null;
        }

        internal static string TransactionBlock(Settlement settlement, Caravan caravan, LandPlotRecord expected, bool bunkerInterior)
        {
            string blocked = VisitTransactionBlock(settlement, caravan, out var domain);
            if (blocked != null) return blocked;
            if (domain.LandPlotAtHost(settlement.ID) != expected) return Key + "Changed";
            if (expected != null)
            {
                if (expected.recoveryError) return Key + "RecordError";
                if (!expected.rented || expected.hostLost || expected.Due(LandPlotPolicy.Now)) return Key + "Closed";
            }
            else
            {
                if (Find.World.pocketMaps.OfType<SettlementLandMapParent>().Any(parent => parent.hostId == settlement.ID))
                    return Key + "RecordError";
                if (Find.Maps.Count >= LandPlotPolicy.LoadedMapLimit) return MapLimit;
                return GenerationBlock(bunkerInterior);
            }
            return null;
        }

        internal static string Confirm(Settlement settlement, Caravan caravan, LandPlotRecord expected,
            long expectedDeadline, int side, bool bunkerInterior, bool rent, int days, int expectedPrice)
        {
            string blocked = TransactionBlock(settlement, caravan, expected, bunkerInterior);
            if (blocked != null) return blocked;
            if (expected != null && (expected.paidThrough != expectedDeadline || expected.side != side
                || expected.bunkerInterior != bunkerInterior || !rent))
                return Key + "Changed";
            if (!LandPlotPolicy.TryQuote(side, rent ? days : 1, out var quote)) return Key + "PriceUnavailable";

            var domain = SettlementServicesWorldComponent.Current;
            int price = rent ? quote.Rent : quote.Purchase;
            if (price != expectedPrice) return Key + "PriceChanged";
            if (LandPlotSilverHold.Available(domain, caravan) < price) return SilverShort;
            long paid = rent ? QuoteDeadline(expected, days) : 0;
            LandPlotRecord added = null;
            long oldGrace = expected?.graceThrough ?? 0;
            bool oldWarned = expected?.warned ?? false;
            bool oldNotice = expected?.expiredNotice ?? false;
            try
            {
                using var escrow = new LandPlotSilverHold(domain, caravan);
                if (!escrow.Stage(price)) return Key + "PaymentFailed";
                try
                {
                    if (expected == null) added = domain.AddLandPlot(settlement, side, bunkerInterior, rent, paid);
                    else domain.SetLandPlotTerm(expected, paid);
                    escrow.Commit();
                }
                catch
                {
                    if (added != null) domain.RemoveLandPlot(added);
                    else if (expected != null)
                    {
                        expected.paidThrough = expectedDeadline;
                        expected.graceThrough = oldGrace;
                        expected.warned = oldWarned;
                        expected.expiredNotice = oldNotice;
                        domain.RescheduleLandPlots();
                    }
                    throw;
                }
            }
            catch (Exception ex)
            {
                SupportLog.Error($"Land payment at settlement {settlement.ID} failed: {ex}");
                return Key + "PaymentFailed";
            }
            return null;
        }

        internal static long QuoteDeadline(LandPlotRecord plot, int days) =>
            LandPlotPolicy.PaidThrough(Math.Max(LandPlotPolicy.Now, plot?.paidThrough ?? 0), days);

        internal static bool HostOrdersBlocked(LandPlotRecord plot)
        {
            Settlement host = WorldObjectLookup.ResolveSettlement(plot.hostId);
            if (host == null || host.Destroyed || host.Faction?.HostileTo(Faction.OfPlayer) == true
                || SettlementServicesCompatibilityRegistry.GetRequestBlockReason(host) != null) return true;
            if (!EmpireTradePermitAccess.IsVanillaEmpireSettlement(host)) return false;
            var permit = DefDatabase<RoyalTitlePermitDef>.GetNamedSilentFail("TradeSettlement");
            Map map = plot.Map;
            return permit == null || map == null || !SettlementLandMapParent.LivingPawns(map)
                .Any(p => p.Faction == Faction.OfPlayer && p.royalty?.HasPermit(permit, host.Faction) == true);
        }

        internal static bool CanEnterNormally(LandPlotRecord plot, Settlement host, Caravan caravan) =>
            !plot.recoveryError && !plot.RecoveryOnly(LandPlotPolicy.Now) && host != null && !host.Destroyed && EligibleHost(host)
            && host.Faction?.HostileTo(Faction.OfPlayer) != true && CaravanVisitUtility.SettlementVisitedNow(caravan) == host
            && SettlementServicesCompatibilityRegistry.GetRequestBlockReason(host) == null
            && EmpireTradePermitAccess.GetBlockReason(host, caravan) == null;

        internal static void Reject(string error) =>
            Messages.Message(error.Translate(), MessageTypeDefOf.RejectInput, historical: false);
    }
}

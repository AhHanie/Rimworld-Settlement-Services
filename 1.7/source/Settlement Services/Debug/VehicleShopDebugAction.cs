using System.Linq;
using System.Text;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Compat.VehicleFramework;

namespace Settlement_Services.Debug
{
    public static class VehicleShopDebugAction
    {
        private const int SampleTrials = 20000;

        [DebugAction("Settlement Services", "Make vehicle shop refresh due", actionType = DebugActionType.ToolWorld, allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        private static void MakeVehicleShopDue()
        {
            Settlement settlement = Find.WorldObjects.SettlementAt(GenWorld.MouseTile());
            if (settlement == null)
            {
                Log.Message("[Settlement Services] No settlement under mouse.");
                return;
            }

            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (!domain.TryGetVehicleShopRefreshTick(settlement.ID, out _))
            {
                Log.Message($"[Settlement Services] {settlement.Name}: the vehicle shop has not been queried yet, so it has no scheduled refresh.");
                return;
            }

            domain.MakeVehicleShopDue(settlement.ID);
            Log.Message($"[Settlement Services] {settlement.Name}: vehicle shop refresh is now due; the roster is replaced the next time the shop is queried.");
        }

        [DebugAction("Settlement Services", "Log vehicle shop offers", actionType = DebugActionType.ToolWorld, allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        private static void LogVehicleShopOffers()
        {
            Settlement settlement = Find.WorldObjects.SettlementAt(GenWorld.MouseTile());
            if (settlement == null)
            {
                Log.Message("[Settlement Services] No settlement under mouse.");
                return;
            }

            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (!domain.TryGetVehicleShopRefreshTick(settlement.ID, out int nextRefreshTick))
            {
                Log.Message($"[Settlement Services] {settlement.Name}: the vehicle shop has not been queried yet.");
                return;
            }

            SettlementRecord record = domain.SettlementRecordsRaw.FirstOrDefault(r => r.settlementWorldObjectId == settlement.ID);
            var sb = new StringBuilder();
            sb.Append($"[Settlement Services] {settlement.Name}: next vehicle shop refresh at tick {nextRefreshTick} (now {Find.TickManager.TicksGame}), {record?.vehicleShopOffers.Count ?? 0} vehicle(s) on offer.");
            if (record != null)
            {
                foreach (VehicleShopOfferRecord offer in record.vehicleShopOffers)
                    sb.Append($"\n  #{offer.offerId}: {offer.vehicleDefName} tier {offer.tier}, {offer.listedPrice} silver");
            }
            Log.Message(sb.ToString());
        }

        [DebugAction("Settlement Services", "Sample vehicle shop tiers", allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        private static void SampleVehicleShopTiers()
        {
            if (!VehicleShopAdapter.Available)
            {
                Log.Message("[Settlement Services] Vehicle shop sampling skipped: Vehicle Framework is not available.");
                return;
            }

            var pool = VehicleShopCatalog.EligibleDefs()
                .Select(d => new VehicleShopCandidate { defName = d.defName, tier = VehicleShopCatalog.TierOf(d) })
                .ToList();

            var sb = new StringBuilder();
            sb.Append($"[Settlement Services] Vehicle shop catalog: {pool.Count} eligible vehicle(s) (tier 1/2/3 = {pool.Count(c => c.tier == 1)}/{pool.Count(c => c.tier == 2)}/{pool.Count(c => c.tier == 3)}).");
            foreach (VehicleShopCandidate candidate in pool.OrderBy(c => c.tier).ThenBy(c => c.defName))
                sb.Append($"\n  tier {candidate.tier}: {candidate.defName}");

            int[] drawn = new int[VehicleShopDraw.MaxTier];
            int[] rostered = new int[VehicleShopDraw.MaxTier];
            for (int seed = 0; seed < SampleTrials; seed++)
            {
                var rng = new System.Random(seed);
                foreach (VehicleShopCandidate chosen in VehicleShopDraw.Select(pool, () => (float)rng.NextDouble(), drawn))
                    rostered[chosen.tier - VehicleShopDraw.MinTier]++;
            }

            sb.Append($"\nSampled {SampleTrials} rosters. Tier draws before fallback: {Percentages(drawn)}. Tiers actually rostered: {Percentages(rostered)}.");
            Log.Message(sb.ToString());
        }

        private static string Percentages(int[] counts)
        {
            int total = counts.Sum();
            return string.Join(" / ", counts.Select((count, i) => $"tier {i + VehicleShopDraw.MinTier} {(total == 0 ? 0f : count / (float)total):P1}"));
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain.Records;

namespace Settlement_Services.Framework.Compat.VehicleFramework
{
    internal static class VehicleShopOfferGenerator
    {
        public static List<VehicleShopOfferRecord> Generate(Settlement settlement, int seed)
        {
            if (!VehicleShopAdapter.Available) return null;

            if (!TryBuildPool(settlement, out List<VehicleShopCandidate> pool, out Dictionary<string, ThingDef> defsByName)) return null;

            List<VehicleShopCandidate> chosen;
            Rand.PushState(seed);
            try
            {
                chosen = VehicleShopDraw.Select(pool, () => Rand.Value);
            }
            finally
            {
                Rand.PopState();
            }

            return chosen
                .Select(c => new VehicleShopOfferRecord
                {
                    vehicleDefName = c.defName,
                    tier = c.tier,
                    listedPrice = VehicleShopCatalog.PriceOf(defsByName[c.defName]),
                })
                .ToList();
        }

        private static bool TryBuildPool(Settlement settlement, out List<VehicleShopCandidate> pool, out Dictionary<string, ThingDef> defsByName)
        {
            pool = new List<VehicleShopCandidate>();
            defsByName = new Dictionary<string, ThingDef>();

            foreach (ThingDef def in VehicleShopCatalog.EligibleDefs())
            {
                VehicleShopNavigation navigation = VehicleShopAdapter.CheckNavigation(def, null, settlement.Tile);
                if (navigation == VehicleShopNavigation.Unknown) return false;
                if (navigation == VehicleShopNavigation.Blocked) continue;

                pool.Add(new VehicleShopCandidate { defName = def.defName, tier = VehicleShopCatalog.TierOf(def) });
                defsByName[def.defName] = def;
            }
            return true;
        }
    }
}

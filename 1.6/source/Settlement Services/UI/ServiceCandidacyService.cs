using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Validation;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.UI
{
    public static class ServiceCandidacyService
    {
        public static bool IsCandidate(SettlementServiceDef def, Settlement settlement) =>
            SettlementServiceValidator.StructuralEligibility(def, settlement, out _);

        public static bool TryGetUnavailableReason(SettlementServiceDef def, Settlement settlement, out string reasonKey)
        {
            if (!SettlementServiceValidator.GoodwillMet(def, settlement, out reasonKey)) return true;

            var ctx = new SettlementServiceContext(SettlementServicesWorldComponent.Current, settlement, null);
            ServiceAvailabilityReport report = def.Worker.CanOffer(ctx);
            if (!report.IsAvailable) { reasonKey = report.ErrorKey; return true; }

            if (settlement != null && !def.stockRequirements.NullOrEmpty())
            {
                StockAvailabilityReport stock = SettlementStockService.CheckAvailability(settlement, def.stockRequirements);
                if (!stock.IsAvailable) { reasonKey = stock.ErrorKey; return true; }
            }

            reasonKey = null;
            return false;
        }

        public static bool IsCandidateForChannel(SettlementServiceDef def, Settlement settlement, RequestChannel channel)
        {
            if (channel == RequestChannel.Remote && !def.allowRemoteDiscovery) return false;
            if (channel == RequestChannel.Remote && !SettlementServiceValidator.RemoteRequiresIndustrialTech(settlement)) return false;
            return IsCandidate(def, settlement);
        }
    }
}

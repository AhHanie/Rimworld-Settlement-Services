using RimWorld.Planet;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Registry;

namespace Settlement_Services.UI
{
    internal static class ServiceDiscoveryRecorder
    {
        public static void RecordCandidates(Settlement settlement, RequestChannel channel)
        {
            if (settlement == null) return;
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;

            foreach (SettlementServiceDef def in SettlementServiceRegistry.AllValid)
            {
                if (!ServiceCandidacyService.IsCandidateForChannel(def, settlement, channel)) continue;
                domain.RecordDiscovery(settlement.ID, def.defName, channel);
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;
using Verse;

namespace Settlement_Services.UI.Overview
{
    public enum ServiceOverviewGrouping { Settlement, Category, Status, None }

    public static class ServiceOverviewQueryService
    {
        public static List<ServiceOverviewEntry> BuildEntries()
        {
            var entries = new List<ServiceOverviewEntry>();
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null) return entries;

            foreach (ServiceJobRecord job in domain.AllJobs)
            {
                SettlementServiceDef def = DefDatabase<SettlementServiceDef>.GetNamedSilentFail(job.serviceDefName);
                Settlement settlement = WorldObjectLookup.ResolveSettlement(job.settlementWorldObjectId);
                entries.Add(new ServiceOverviewEntry(job, def, settlement));
            }
            return entries;
        }

        public static List<ServiceDiscoveryOverviewEntry> BuildDiscoveryEntries()
        {
            var entries = new List<ServiceDiscoveryOverviewEntry>();
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null) return entries;

            foreach (int id in domain.SettlementWorldObjectIdsWithAnyDiscovery)
            {
                Settlement settlement = WorldObjectLookup.ResolveSettlement(id);
                entries.Add(new ServiceDiscoveryOverviewEntry(settlement, domain.DiscoveriesForSettlement(id)));
            }
            return entries.OrderBy(e => e.settlementLabel).ToList();
        }

        public static List<IGrouping<string, ServiceOverviewEntry>> Grouped(
            IEnumerable<ServiceOverviewEntry> entries, ServiceOverviewGrouping grouping)
        {
            switch (grouping)
            {
                case ServiceOverviewGrouping.Settlement:
                    return entries.GroupBy(e => e.settlementLabel).OrderBy(g => g.Key).ToList();
                case ServiceOverviewGrouping.Category:
                    return entries.GroupBy(e => e.CategoryLabel).OrderBy(g => g.Key).ToList();
                case ServiceOverviewGrouping.Status:
                    return entries.GroupBy(e => ServiceOverviewFormatting.StatusLabel(e.job.status)).OrderBy(g => g.Key).ToList();
                default:
                    return entries.GroupBy(_ => (string)null).ToList();
            }
        }
    }
}

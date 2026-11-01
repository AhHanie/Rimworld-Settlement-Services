using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Specialty;
using Verse;

namespace Settlement_Services.UI.Overview
{
    public class ServiceDiscoveryOverviewEntry
    {
        public readonly Settlement settlement;
        public readonly string settlementLabel;
        public readonly string inPersonLabel;
        public readonly string remoteLabel;
        public readonly IReadOnlyList<SettlementSpecialtyDef> specialties;
        public readonly IReadOnlyList<SettlementServiceDef> inPersonServices;
        public readonly IReadOnlyList<SettlementServiceDef> remoteServices;

        public ServiceDiscoveryOverviewEntry(Settlement settlement, IReadOnlyList<DiscoveryRecord> discoveries)
        {
            this.settlement = settlement;
            settlementLabel = settlement?.LabelCap ?? "SettlementServices.Label.UnknownSettlement".Translate();

            inPersonServices = ResolveServices(discoveries, RequestChannel.InPerson);
            remoteServices = ResolveServices(discoveries, RequestChannel.Remote);
            inPersonLabel = JoinLabels(inPersonServices);
            remoteLabel = JoinLabels(remoteServices);

            specialties = settlement == null || (inPersonServices.Count == 0 && remoteServices.Count == 0)
                ? Array.Empty<SettlementSpecialtyDef>()
                : SettlementSpecialtyService.PreviewSpecialties(settlement)
                    .GroupBy(s => s.defName)
                    .Select(g => g.First())
                    .OrderBy(s => (string)s.LabelCap, StringComparer.CurrentCulture)
                    .ThenBy(s => s.defName, StringComparer.Ordinal)
                    .ToList();
        }

        public bool HasSpecialty(string defName) => specialties.Any(s => s.defName == defName);

        public bool HasDiscoveredService(string defName) =>
            inPersonServices.Any(s => s.defName == defName) || remoteServices.Any(s => s.defName == defName);

        private static IReadOnlyList<SettlementServiceDef> ResolveServices(IReadOnlyList<DiscoveryRecord> discoveries, RequestChannel channel)
        {
            return discoveries
                .Where(d => d != null && d.discoveredVia == channel)
                .Select(d => d.ResolveDef())
                .Where(def => def != null)
                .GroupBy(def => def.defName)
                .Select(g => g.First())
                .OrderBy(def => (string)def.LabelCap, StringComparer.CurrentCulture)
                .ThenBy(def => def.defName, StringComparer.Ordinal)
                .ToList();
        }

        private static string JoinLabels(IReadOnlyList<SettlementServiceDef> services) =>
            services.Count == 0 ? null : string.Join(", ", services.Select(s => (string)s.LabelCap));
    }
}

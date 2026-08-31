using System.Collections.Generic;
using RimWorld.Planet;
using Settlement_Services.Domain.Records;
using Verse;

namespace Settlement_Services.UI.Overview
{
    public class ServiceDiscoveryOverviewEntry
    {
        public readonly Settlement settlement;
        public readonly string settlementLabel;
        public readonly string inPersonLabel;
        public readonly string remoteLabel;

        public ServiceDiscoveryOverviewEntry(Settlement settlement, IReadOnlyList<DiscoveryRecord> discoveries)
        {
            this.settlement = settlement;
            settlementLabel = settlement?.LabelCap ?? "SettlementServices.Label.UnknownSettlement".Translate();
            (inPersonLabel, remoteLabel) = ServiceDiscoveryFormatting.SplitByChannel(discoveries);
        }
    }
}

using RimWorld.Planet;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;
using Verse;

namespace Settlement_Services.UI.Overview
{
    public class ServiceOverviewEntry
    {
        public readonly ServiceJobRecord job;
        public readonly SettlementServiceDef def;
        public readonly Settlement settlement;
        public readonly string settlementLabel;

        public ServiceOverviewEntry(ServiceJobRecord job, SettlementServiceDef def, Settlement settlement)
        {
            this.job = job;
            this.def = def;
            this.settlement = settlement;
            settlementLabel = settlement?.LabelCap ?? "SettlementServices.Label.UnknownSettlement".Translate();
        }

        public string ServiceLabel => def?.LabelCap ?? "SettlementServices.Label.UnknownService".Translate();
        public string CategoryLabel => def?.category?.LabelCap ?? "SettlementServices.Label.UnknownService".Translate();

        public string TargetLabel
        {
            get
            {
                if (job.targets.Count == 0) return job.quantity > 1
                    ? (string)"SettlementServices.Label.QuantityTarget".Translate(job.quantity)
                    : (string)"SettlementServices.Label.NoTarget".Translate();

                string primary = job.targets[0]?.snapshotLabel ?? "SettlementServices.Label.NoTarget".Translate();
                int others = job.targets.Count - 1;
                return others > 0 ? (string)"SettlementServices.Label.TargetPlusOthers".Translate(primary, others) : primary;
            }
        }
    }
}

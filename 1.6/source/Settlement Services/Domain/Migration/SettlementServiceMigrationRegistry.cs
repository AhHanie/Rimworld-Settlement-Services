using System.Collections.Generic;

namespace Settlement_Services.Domain.Migration
{
    public static class SettlementServiceMigrationRegistry
    {
        private static readonly List<ISettlementServiceMigration> All = new List<ISettlementServiceMigration>();

        public static void Run(SettlementServicesWorldComponent component, int from, int to)
        {
            for (int v = from; v < to; v++)
            {
                ISettlementServiceMigration migration = All.Find(m => m.FromVersion == v);
                if (migration == null)
                {
                    Settlement_Services.SupportLog.Error($"Missing migration from schema {v}.");
                    continue;
                }

                migration.Apply(component);
                Settlement_Services.SupportLog.Info($"Migrated Settlement Services save data {v} -> {v + 1}.");
            }
        }
    }
}

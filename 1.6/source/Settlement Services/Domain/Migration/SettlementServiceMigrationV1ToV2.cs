using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Domain.Migration
{
    public class SettlementServiceMigrationV1ToV2 : ISettlementServiceMigration
    {
        public int FromVersion => 1;

        public void Apply(SettlementServicesWorldComponent component)
        {
            if (!ModsConfig.IdeologyActive) return;

            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                string reservedLoadId = settlement.Faction?.ideos?.PrimaryIdeo?.GetUniqueLoadID();
                component.EnsureRosterInitialized(settlement, reservedLoadId);
            }
        }
    }
}

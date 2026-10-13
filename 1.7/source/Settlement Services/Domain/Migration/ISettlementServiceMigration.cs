using Settlement_Services.Domain;

namespace Settlement_Services.Domain.Migration
{
    public interface ISettlementServiceMigration
    {
        int FromVersion { get; }

        void Apply(SettlementServicesWorldComponent component);
    }
}

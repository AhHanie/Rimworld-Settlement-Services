using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;

namespace Settlement_Services.Framework.Compatibility
{
    internal sealed class CompatibilityCompletionContext
    {
        public readonly SettlementServicesWorldComponent domain;
        public readonly ServiceJobRecord job;

        public CompatibilityCompletionContext(SettlementServicesWorldComponent domain, ServiceJobRecord job)
        {
            this.domain = domain;
            this.job = job;
        }
    }
}

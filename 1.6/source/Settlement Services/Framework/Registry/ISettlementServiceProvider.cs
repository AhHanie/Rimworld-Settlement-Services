using System.Collections.Generic;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Framework.Registry
{
    public interface ISettlementServiceProvider
    {
        IEnumerable<SettlementServiceDef> ProvideDefs();
    }
}

using System.Collections.Generic;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.Framework.Compatibility
{
    internal sealed class CompatibilityQuoteContext
    {
        public readonly SettlementServiceDef def;
        public readonly SettlementServiceRequest request;
        public readonly List<ServiceLineItem> lineItems;
        public int totalCost;

        public CompatibilityQuoteContext(SettlementServiceDef def, SettlementServiceRequest request, List<ServiceLineItem> lineItems, int totalCost)
        {
            this.def = def;
            this.request = request;
            this.lineItems = lineItems;
            this.totalCost = totalCost;
        }
    }
}

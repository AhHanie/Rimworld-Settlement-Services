using System;
using System.Collections.Generic;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Stock;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Framework
{
    public class ServiceBatchAllocationContext
    {
        public readonly StockAllocationLedger StockLedger = new StockAllocationLedger();

        private readonly Dictionary<SettlementServiceRequest, ServiceInputPlan> cachedInputPlans = new Dictionary<SettlementServiceRequest, ServiceInputPlan>();

        public ServiceInputPlan GetOrCreateInputPlan(SettlementServiceRequest request, Func<ServiceInputPlan> factory)
        {
            if (!cachedInputPlans.TryGetValue(request, out ServiceInputPlan plan))
            {
                plan = factory();
                cachedInputPlans[request] = plan;
            }
            return plan;
        }
    }
}

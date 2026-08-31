using System.Collections.Generic;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.Framework.Workers.Results
{
    public class ServiceCancelResult
    {
        public bool Success { get; }
        public string ErrorKey { get; }

        public List<ServiceLineItem> RefundLineItems { get; }

        public ServiceCancelResult(bool success, List<ServiceLineItem> refundLineItems, string errorKey)
        {
            Success = success;
            RefundLineItems = refundLineItems;
            ErrorKey = errorKey;
        }

        public static ServiceCancelResult Ok(List<ServiceLineItem> refundLineItems = null) => new ServiceCancelResult(true, refundLineItems, null);
        public static ServiceCancelResult Fail(string errorKey) => new ServiceCancelResult(false, null, errorKey);
    }
}

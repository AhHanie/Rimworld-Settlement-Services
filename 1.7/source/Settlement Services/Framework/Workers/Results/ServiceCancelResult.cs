using System.Collections.Generic;
using Settlement_Services.Framework.Dto;

namespace Settlement_Services.Framework.Workers.Results
{
    public class ServiceCancelResult
    {
        public bool Success { get; }
        public string ErrorKey { get; }

        public List<ServiceLineItem> RefundLineItems { get; }

        public ServiceTargetDisposition TargetDisposition { get; }

        public ServiceCancelResult(bool success, List<ServiceLineItem> refundLineItems, string errorKey, ServiceTargetDisposition targetDisposition = ServiceTargetDisposition.Return)
        {
            Success = success;
            RefundLineItems = refundLineItems;
            ErrorKey = errorKey;
            TargetDisposition = targetDisposition;
        }

        public static ServiceCancelResult Ok(List<ServiceLineItem> refundLineItems = null, ServiceTargetDisposition targetDisposition = ServiceTargetDisposition.Return) =>
            new ServiceCancelResult(true, refundLineItems, null, targetDisposition);
        public static ServiceCancelResult Fail(string errorKey) => new ServiceCancelResult(false, null, errorKey);
    }
}

using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Framework.Workers.Results
{
    public class ServiceCompletionResult
    {
        public bool Success { get; }
        public string ErrorKey { get; }

        public bool RequiresCollection { get; }

        public List<Thing> ResultThings { get; }

        public ServiceTargetDisposition TargetDisposition { get; }

        public int RefundAmount { get; private set; }

        public ServiceCompletionResult WithRefund(int amount)
        {
            RefundAmount = amount > 0 ? amount : 0;
            return this;
        }

        public ServiceCompletionResult(bool success, bool requiresCollection, List<Thing> resultThings, string errorKey, ServiceTargetDisposition targetDisposition = ServiceTargetDisposition.Return)
        {
            Success = success;
            RequiresCollection = requiresCollection;
            ResultThings = resultThings;
            ErrorKey = errorKey;
            TargetDisposition = targetDisposition;
        }

        public static ServiceCompletionResult Ok(bool requiresCollection = false, List<Thing> resultThings = null, ServiceTargetDisposition targetDisposition = ServiceTargetDisposition.Return) =>
            new ServiceCompletionResult(true, requiresCollection, resultThings, null, targetDisposition);

        public static ServiceCompletionResult Fail(string errorKey) => new ServiceCompletionResult(false, false, null, errorKey);
    }
}

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

        public ServiceCompletionResult(bool success, bool requiresCollection, List<Thing> resultThings, string errorKey)
        {
            Success = success;
            RequiresCollection = requiresCollection;
            ResultThings = resultThings;
            ErrorKey = errorKey;
        }

        public static ServiceCompletionResult Ok(bool requiresCollection = false, List<Thing> resultThings = null) =>
            new ServiceCompletionResult(true, requiresCollection, resultThings, null);

        public static ServiceCompletionResult Fail(string errorKey) => new ServiceCompletionResult(false, false, null, errorKey);
    }
}

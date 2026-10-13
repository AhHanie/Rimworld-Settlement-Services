namespace Settlement_Services.Framework.Workers.Results
{
    public class ServiceTickResult
    {
        public static readonly ServiceTickResult NoChange = new ServiceTickResult(false, false, null);
        public static readonly ServiceTickResult Completed = new ServiceTickResult(true, false, null);

        public bool CompletedNow { get; }
        public bool FailedNow { get; }
        public string ErrorKey { get; }

        public ServiceTickResult(bool completedNow, bool failedNow, string errorKey)
        {
            CompletedNow = completedNow;
            FailedNow = failedNow;
            ErrorKey = errorKey;
        }

        public static ServiceTickResult Failed(string errorKey) => new ServiceTickResult(false, true, errorKey);
    }
}

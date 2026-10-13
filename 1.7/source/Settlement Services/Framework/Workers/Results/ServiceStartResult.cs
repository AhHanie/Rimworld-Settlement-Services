namespace Settlement_Services.Framework.Workers.Results
{
    public class ServiceStartResult
    {
        public static readonly ServiceStartResult Ok = new ServiceStartResult(true, null);

        public bool Success { get; }
        public string ErrorKey { get; }

        public ServiceStartResult(bool success, string errorKey)
        {
            Success = success;
            ErrorKey = errorKey;
        }

        public static ServiceStartResult Fail(string errorKey) => new ServiceStartResult(false, errorKey);
    }
}

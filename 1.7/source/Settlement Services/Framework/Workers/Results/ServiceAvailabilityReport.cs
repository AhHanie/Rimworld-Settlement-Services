namespace Settlement_Services.Framework.Workers.Results
{
    public class ServiceAvailabilityReport
    {
        public static readonly ServiceAvailabilityReport Available = new ServiceAvailabilityReport(true, null);

        public bool IsAvailable { get; }
        public string ErrorKey { get; }

        private ServiceAvailabilityReport(bool isAvailable, string errorKey)
        {
            IsAvailable = isAvailable;
            ErrorKey = errorKey;
        }

        public static ServiceAvailabilityReport Unavailable(string errorKey) => new ServiceAvailabilityReport(false, errorKey);
    }
}

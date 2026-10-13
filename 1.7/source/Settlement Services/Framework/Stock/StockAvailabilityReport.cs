namespace Settlement_Services.Framework.Stock
{
    public struct StockAvailabilityReport
    {
        public bool IsAvailable { get; }
        public string ErrorKey { get; }

        public static readonly StockAvailabilityReport Available = new StockAvailabilityReport(true, null);
        public static StockAvailabilityReport Unavailable(string errorKey) => new StockAvailabilityReport(false, errorKey);

        private StockAvailabilityReport(bool isAvailable, string errorKey)
        {
            IsAvailable = isAvailable;
            ErrorKey = errorKey;
        }
    }
}

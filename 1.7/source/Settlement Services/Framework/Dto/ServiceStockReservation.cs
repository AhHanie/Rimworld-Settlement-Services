namespace Settlement_Services.Framework.Dto
{
    public class ServiceStockReservation
    {
        public string stockKey;
        public int amount;

        public ServiceStockReservation()
        {
        }

        public ServiceStockReservation(string stockKey, int amount)
        {
            this.stockKey = stockKey;
            this.amount = amount;
        }
    }
}

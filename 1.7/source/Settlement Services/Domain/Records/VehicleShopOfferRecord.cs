using Verse;

namespace Settlement_Services.Domain.Records
{
    public class VehicleShopOfferRecord : IExposable
    {
        public int offerId = -1;
        public string vehicleDefName;
        public int tier = 1;
        public int listedPrice;

        public void ExposeData()
        {
            Scribe_Values.Look(ref offerId, "offerId", -1);
            Scribe_Values.Look(ref vehicleDefName, "vehicleDefName");
            Scribe_Values.Look(ref tier, "tier", 1);
            Scribe_Values.Look(ref listedPrice, "listedPrice");
        }
    }
}

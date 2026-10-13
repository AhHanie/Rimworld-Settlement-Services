using Verse;

namespace Settlement_Services.Domain.Records
{
    public class AnimalVendorOfferRecord : IExposable
    {
        public int offerId = -1;
        public Pawn pawn;

        public void ExposeData()
        {
            Scribe_Values.Look(ref offerId, "offerId", -1);
            Scribe_References.Look(ref pawn, "pawn", saveDestroyedThings: true);
        }
    }
}

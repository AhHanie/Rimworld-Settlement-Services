using Verse;

namespace Settlement_Services.Domain.Records
{
    public class JobBoardRecord : IExposable
    {
        public bool initialized;
        public int nextOfferTick = -1;
        public int generationSerial;

        public void ExposeData()
        {
            Scribe_Values.Look(ref initialized, "initialized");
            Scribe_Values.Look(ref nextOfferTick, "nextOfferTick", -1);
            Scribe_Values.Look(ref generationSerial, "generationSerial");
        }
    }
}

using Verse;

namespace Settlement_Services.Domain.Records
{
    public class InvestmentRecord : IExposable
    {
        public int investedTick;

        public float investedDiscountPct;
        public int decayDurationTicks;

        public void ExposeData()
        {
            Scribe_Values.Look(ref investedTick, "investedTick");
            Scribe_Values.Look(ref investedDiscountPct, "investedDiscountPct");
            Scribe_Values.Look(ref decayDurationTicks, "decayDurationTicks");
        }
    }
}

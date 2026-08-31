using Verse;

namespace Settlement_Services.Domain.Records
{
    public class ReservationRecord : IExposable
    {
        public int jobId;
        public string stockThingDefName;
        public int amountReserved;
        public int reservedAtTick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref jobId, "jobId");
            Scribe_Values.Look(ref stockThingDefName, "stockThingDefName");
            Scribe_Values.Look(ref amountReserved, "amountReserved");
            Scribe_Values.Look(ref reservedAtTick, "reservedAtTick");
        }
    }
}

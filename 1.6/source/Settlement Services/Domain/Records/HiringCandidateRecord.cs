using Verse;

namespace Settlement_Services.Domain.Records
{
    public class HiringCandidateRecord : IExposable
    {
        public int candidateId = -1;
        public Pawn pawn;
        public int expiryTick = -1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref candidateId, "candidateId", -1);
            Scribe_References.Look(ref pawn, "pawn", saveDestroyedThings: true);
            Scribe_Values.Look(ref expiryTick, "expiryTick", -1);
        }
    }
}

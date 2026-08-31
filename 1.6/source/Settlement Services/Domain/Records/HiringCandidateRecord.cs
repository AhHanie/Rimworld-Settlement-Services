using Verse;

namespace Settlement_Services.Domain.Records
{
    public class HiringCandidateRecord : IExposable
    {
        public int candidateId = -1;
        public string specialtyWorkTypeDefName;
        public int skillLevel;
        public string qualityTierKey;
        public int wage;
        public bool refusesHazardousWork;
        public int expiryTick = -1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref candidateId, "candidateId", -1);
            Scribe_Values.Look(ref specialtyWorkTypeDefName, "specialtyWorkTypeDefName");
            Scribe_Values.Look(ref skillLevel, "skillLevel");
            Scribe_Values.Look(ref qualityTierKey, "qualityTierKey");
            Scribe_Values.Look(ref wage, "wage");
            Scribe_Values.Look(ref refusesHazardousWork, "refusesHazardousWork");
            Scribe_Values.Look(ref expiryTick, "expiryTick", -1);
        }
    }
}

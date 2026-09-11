namespace Settlement_Services.Framework.Defs
{
    public class ServiceEventEffects
    {
        public string experienceSkillDefName;
        public float experienceAmount;

        public string thoughtDefName;
        public bool grantThoughtToAllParticipants;

        public int goodwillDelta;

        public int refundAmount;

        public int durationDeltaTicks;
        public float durationDeltaPct;

        public int? qualityOffset;

        public string hediffDefName;
        public float hediffSeverity;

        public string referralCategoryDefName;

        public string questHookDefName;
    }
}

using Verse;

namespace Settlement_Services.Domain.Records
{
    public class SurrogacyJobState : IExposable
    {
        public int startTick;
        public int dueTick;

        public bool birthCommitted;
        public bool stillborn;
        public bool birthNotificationSent;
        public bool completed;
        public bool refundIssued;

        public void ExposeData()
        {
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref dueTick, "dueTick");
            Scribe_Values.Look(ref birthCommitted, "birthCommitted");
            Scribe_Values.Look(ref stillborn, "stillborn");
            Scribe_Values.Look(ref birthNotificationSent, "birthNotificationSent");
            Scribe_Values.Look(ref completed, "completed");
            Scribe_Values.Look(ref refundIssued, "refundIssued");
        }
    }
}

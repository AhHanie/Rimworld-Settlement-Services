using Verse;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.Domain.Records
{
    public class ServiceEventOutcomeRecord : IExposable
    {
        public string eventDefName;

        public ServiceEventTriggerPhase triggerPhase;

        public int scheduledTick = -1;

        public int rolledTick;
        public bool presented;
        public bool applied;

        public int choiceIndexSelected = -1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref eventDefName, "eventDefName");
            Scribe_Values.Look(ref triggerPhase, "triggerPhase");
            Scribe_Values.Look(ref scheduledTick, "scheduledTick", -1);
            Scribe_Values.Look(ref rolledTick, "rolledTick");
            Scribe_Values.Look(ref applied, "applied");
            Scribe_Values.Look(ref presented, "presented", applied);
            Scribe_Values.Look(ref choiceIndexSelected, "choiceIndexSelected", -1);
        }

        public static ServiceEventOutcomeRecord None(int rolledTick) =>
            new ServiceEventOutcomeRecord { eventDefName = null, presented = true, applied = true, rolledTick = rolledTick };
    }
}

using RimWorld;
using Verse;

namespace Settlement_Services.Domain
{
    public enum TargetKind
    {
        None,
        Pawn,
        Animal,
        Item,
        Mech,
        Vehicle,
        Android
    }

    public class TargetSnapshot : IExposable
    {
        public TargetKind kind;

        public Thing liveThing;

        public string snapshotLabel;
        public string snapshotDefName;
        public QualityCategory? snapshotQuality;

        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind");
            Scribe_References.Look(ref liveThing, "liveThing");
            Scribe_Values.Look(ref snapshotLabel, "snapshotLabel");
            Scribe_Values.Look(ref snapshotDefName, "snapshotDefName");
            Scribe_Values.Look(ref snapshotQuality, "snapshotQuality");
        }

        public bool IsResolvable => liveThing != null && !liveThing.Destroyed;
    }
}

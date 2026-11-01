using RimWorld;
using Verse;

namespace Settlement_Services.Domain.Records
{
    public enum GrowthVatKind
    {
        Embryo,
        Child,
    }

    public class GrowthVatJobState : IExposable
    {
        public GrowthVatKind kind;
        public string milestoneKey;
        public long targetAgeTicks;

        public int gestationTicksLeft;
        public int lastProcessedTick;

        public float plannedNutrition;
        public float prepaidNutrition;
        public float consumedNutrition;
        public int prepaidValue;
        public int facilityFee;

        public bool birthCommitted;
        public bool stillborn;
        public bool targetReached;
        public bool completed;
        public bool refundIssued;

        public Building_GrowthVat vat;

        public bool InGestation => kind == GrowthVatKind.Embryo && !birthCommitted && !stillborn;

        public float UnusedNutrition => prepaidNutrition > consumedNutrition ? prepaidNutrition - consumedNutrition : 0f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref milestoneKey, "milestoneKey");
            Scribe_Values.Look(ref targetAgeTicks, "targetAgeTicks");
            Scribe_Values.Look(ref gestationTicksLeft, "gestationTicksLeft");
            Scribe_Values.Look(ref lastProcessedTick, "lastProcessedTick");
            Scribe_Values.Look(ref plannedNutrition, "plannedNutrition");
            Scribe_Values.Look(ref prepaidNutrition, "prepaidNutrition");
            Scribe_Values.Look(ref consumedNutrition, "consumedNutrition");
            Scribe_Values.Look(ref prepaidValue, "prepaidValue");
            Scribe_Values.Look(ref facilityFee, "facilityFee");
            Scribe_Values.Look(ref birthCommitted, "birthCommitted");
            Scribe_Values.Look(ref stillborn, "stillborn");
            Scribe_Values.Look(ref targetReached, "targetReached");
            Scribe_Values.Look(ref completed, "completed");
            Scribe_Values.Look(ref refundIssued, "refundIssued");
            Scribe_References.Look(ref vat, "vat");
        }
    }
}

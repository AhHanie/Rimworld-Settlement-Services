using Verse;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Services.Medical;

namespace Settlement_Services.Framework.Events.Workers
{
    public class SuccessfulRehabilitationServiceEventWorker : ServiceEventWorker
    {
        public override bool CanApply(ServiceJobContext ctx)
        {
            if (ctx.Job.acceptedQuote?.selectedTierKey != MedicalTreatabilityService.RehabilitationTierKey)
                return false;

            Pawn pawn = ctx.ResolvePrimaryPawn();
            return pawn != null && !pawn.Destroyed && !pawn.Dead && pawn.health != null;
        }
    }
}

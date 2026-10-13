using System.Collections.Generic;
using System.Linq;
using Verse;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events.Workers
{
    public class CureRandomInjuryServiceEventWorker : ServiceEventWorker
    {
        public override bool CanApply(ServiceJobContext ctx) =>
            EligibleInjuries(ctx.ResolvePrimaryPawn()).Any();

        public override void Apply(ServiceJobContext ctx)
        {
            Pawn pawn = ctx.ResolvePrimaryPawn();
            List<Hediff_Injury> candidates = EligibleInjuries(pawn).ToList();
            if (candidates.Count == 0) return;

            Hediff_Injury selectedInjury = candidates.RandomElement();
            pawn.health.RemoveHediff(selectedInjury);
        }

        private static IEnumerable<Hediff_Injury> EligibleInjuries(Pawn pawn) =>
            pawn?.health?.hediffSet?.hediffs.OfType<Hediff_Injury>().Where(h => !h.IsPermanent() && h.Severity > 0f)
            ?? Enumerable.Empty<Hediff_Injury>();
    }
}

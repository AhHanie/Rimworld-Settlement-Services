using System.Collections.Generic;
using System.Linq;
using Verse;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events.Workers
{
    public class GroupCureRandomInjuryServiceEventWorker : ServiceEventWorker
    {
        public override bool CanApply(ServiceJobContext ctx) =>
            AnimalGroupServiceEventUtility.HasAtLeastTwo(EligibleAnimals(ctx));

        public override void Apply(ServiceJobContext ctx)
        {
            List<Pawn> eligible = EligibleAnimals(ctx).ToList();
            List<Pawn> affected = AnimalGroupServiceEventUtility.RandomSubsetAtLeastTwo(eligible);
            if (affected.Count < 2) return;

            foreach (Pawn pawn in affected)
            {
                List<Hediff_Injury> candidates = EligibleInjuries(pawn).ToList();
                if (candidates.Count == 0) continue;

                Hediff_Injury selectedInjury = candidates.RandomElement();
                pawn.health.RemoveHediff(selectedInjury);
            }
        }

        private static IEnumerable<Pawn> EligibleAnimals(ServiceJobContext ctx) =>
            AnimalGroupServiceEventUtility.ResolveLiveAnimals(ctx).Where(p => EligibleInjuries(p).Any());

        private static IEnumerable<Hediff_Injury> EligibleInjuries(Pawn pawn) =>
            pawn?.health?.hediffSet?.hediffs.OfType<Hediff_Injury>().Where(h => !h.IsPermanent() && h.Severity > 0f)
            ?? Enumerable.Empty<Hediff_Injury>();
    }
}

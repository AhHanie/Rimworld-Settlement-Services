using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events.Workers
{
    public class RandomAnimalDiseaseServiceEventWorker : ServiceEventWorker
    {
        public override bool CanApply(ServiceJobContext ctx) => EligiblePairs(ctx).Any();

        public override void Apply(ServiceJobContext ctx)
        {
            List<(Pawn animal, IncidentWorker_Disease worker)> pairs = EligiblePairs(ctx).ToList();
            if (pairs.Count == 0) return;

            Pawn animal = pairs.Select(p => p.animal).Distinct().RandomElement();
            IncidentWorker_Disease diseaseWorker = pairs.Where(p => p.animal == animal).RandomElement().worker;

            List<Pawn> applied = diseaseWorker.ApplyToPawns(new[] { animal }, out _);
            if (applied.NullOrEmpty()) return;
        }

        private static IEnumerable<(Pawn animal, IncidentWorker_Disease worker)> EligiblePairs(ServiceJobContext ctx)
        {
            List<IncidentWorker_Disease> diseaseWorkers = AnimalGroupServiceEventUtility.DiseaseWorkers().ToList();
            if (diseaseWorkers.Count == 0) yield break;

            foreach (TargetSnapshot target in ctx.ResolveTargets())
            {
                if (!(target?.liveThing is Pawn animal)) continue;
                if (!AnimalGroupServiceEventUtility.IsDiseaseCandidateAnimal(animal)) continue;

                foreach (IncidentWorker_Disease diseaseWorker in diseaseWorkers)
                {
                    if (!AnimalGroupServiceEventUtility.IsDiseaseCompatible(animal, diseaseWorker)) continue;

                    yield return (animal, diseaseWorker);
                }
            }
        }
    }
}

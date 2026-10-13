using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events.Workers
{
    public class GroupAnimalDiseaseServiceEventWorker : ServiceEventWorker
    {
        public override bool CanApply(ServiceJobContext ctx) =>
            Cohorts(ctx).Any(c => c.Value.Count >= 2);

        public override void Apply(ServiceJobContext ctx)
        {
            List<KeyValuePair<IncidentWorker_Disease, List<Pawn>>> cohorts =
                Cohorts(ctx).Where(c => c.Value.Count >= 2).ToList();
            if (cohorts.Count == 0) return;

            KeyValuePair<IncidentWorker_Disease, List<Pawn>> chosen = cohorts.RandomElement();
            List<Pawn> affected = AnimalGroupServiceEventUtility.RandomSubsetAtLeastTwo(chosen.Value);
            if (affected.Count < 2) return;

            chosen.Key.ApplyToPawns(affected, out _);
        }

        private static Dictionary<IncidentWorker_Disease, List<Pawn>> Cohorts(ServiceJobContext ctx)
        {
            var cohorts = new Dictionary<IncidentWorker_Disease, List<Pawn>>();
            List<IncidentWorker_Disease> diseaseWorkers = AnimalGroupServiceEventUtility.DiseaseWorkers().ToList();
            if (diseaseWorkers.Count == 0) return cohorts;

            foreach (Pawn animal in AnimalGroupServiceEventUtility.ResolveLiveAnimals(ctx))
            {
                if (!AnimalGroupServiceEventUtility.IsDiseaseCandidateAnimal(animal)) continue;

                foreach (IncidentWorker_Disease diseaseWorker in diseaseWorkers)
                {
                    if (!AnimalGroupServiceEventUtility.IsDiseaseCompatible(animal, diseaseWorker)) continue;

                    if (!cohorts.TryGetValue(diseaseWorker, out List<Pawn> cohort))
                        cohorts[diseaseWorker] = cohort = new List<Pawn>();
                    cohort.Add(animal);
                }
            }
            return cohorts;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events.Workers
{
    internal static class AnimalGroupServiceEventUtility
    {
        public static List<Pawn> ResolveLiveAnimals(ServiceJobContext ctx)
        {
            var seen = new HashSet<Pawn>();
            var result = new List<Pawn>();
            foreach (TargetSnapshot target in ctx.ResolveTargets())
            {
                if (!(target?.liveThing is Pawn animal)) continue;
                if (animal.Destroyed || animal.Dead || animal.health == null) continue;
                if (animal.RaceProps == null || !animal.RaceProps.Animal) continue;
                if (seen.Add(animal)) result.Add(animal);
            }
            return result;
        }

        public static bool HasAtLeastTwo<T>(IEnumerable<T> source) => source.Take(2).Count() >= 2;

        public static List<T> RandomSubsetAtLeastTwo<T>(List<T> source)
        {
            if (source.Count < 2) return new List<T>();
            int count = Rand.RangeInclusive(2, source.Count);
            return source.InRandomOrder().Take(count).ToList();
        }

        public static IEnumerable<IncidentWorker_Disease> DiseaseWorkers()
        {
            foreach (string incidentDefName in new[] { "Disease_AnimalFlu", "Disease_AnimalPlague" })
            {
                IncidentDef incidentDef = DefDatabase<IncidentDef>.GetNamedSilentFail(incidentDefName);
                if (incidentDef?.diseaseIncident == null) continue;
                if (incidentDef.Worker is IncidentWorker_Disease diseaseWorker) yield return diseaseWorker;
            }
        }

        public static bool IsDiseaseCandidateAnimal(Pawn animal) =>
            animal != null && !animal.Destroyed && !animal.Dead && animal.health != null
            && animal.RaceProps != null && animal.RaceProps.Animal && animal.RaceProps.IsFlesh && !animal.RaceProps.Dryad;

        public static bool IsDiseaseCompatible(Pawn animal, IncidentWorker_Disease diseaseWorker) =>
            diseaseWorker.def.diseaseDevelopmentStage.Has(animal.DevelopmentalStage)
            && Mathf.Approximately(animal.health.immunity.DiseaseContractChanceFactor(diseaseWorker.def.diseaseIncident), 1f);
    }
}

using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Settlement_Services.Services.Education
{
    internal static class AnimalTrainingOptionService
    {
        public static IEnumerable<TrainableDef> FindTrainable(Pawn animal)
        {
            if (animal?.training == null) yield break;
            foreach (TrainableDef trainableDef in TrainableUtility.TrainableDefsInListOrder)
            {
                if (!animal.training.CanAssignToTrain(trainableDef, out bool visible).Accepted || !visible) continue;
                if (!animal.training.CanBeTrained(trainableDef)) continue;
                yield return trainableDef;
            }
        }
    }
}

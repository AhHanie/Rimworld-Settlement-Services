using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Education
{
    public class AnimalTrainingServiceWorker : SettlementServiceWorker
    {
        private const float WealthScalePerBehavior = 0.01f;

        public override bool UsesPerTargetOptionSelections => true;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn animal)) return ServiceAvailabilityReport.Available;
            return AnimalTrainingOptionService.FindTrainable(animal).Any()
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoTrainableBehaviors");
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            if (!(ctx.SelectedTarget is Pawn animal)) yield break;
            foreach (TrainableDef trainableDef in AnimalTrainingOptionService.FindTrainable(animal))
                yield return new ServiceDisplayOption { key = trainableDef.defName, label = trainableDef.LabelCap };
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request) => new List<ServiceLineItem>();

        public override float WealthScaleAdditionFor(SettlementServiceRequest request) =>
            new HashSet<string>(request.selectedOptionKeys).Count * WealthScalePerBehavior;

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            if (!(request.target.thing is Pawn animal)) return null;
            var selectedKeys = new HashSet<string>(request.selectedOptionKeys ?? Enumerable.Empty<string>());
            if (selectedKeys.Count == 0) return "SettlementServices.Error.NoBehaviorsSelectedForAnimal";
            var trainable = new HashSet<string>(AnimalTrainingOptionService.FindTrainable(animal).Select(td => td.defName));
            return selectedKeys.All(trainable.Contains) ? null : "SettlementServices.Error.NoTrainableBehaviors";
        }

        public override ServiceStartResult Start(ServiceJobContext ctx) => ServiceStartResult.Ok;

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            if (ctx.CurrentTarget?.liveThing is Pawn animal)
            {
                var selectedKeys = new HashSet<string>(ctx.SelectedOptionKeys);
                foreach (TrainableDef trainableDef in AnimalTrainingOptionService.FindTrainable(animal).Where(td => selectedKeys.Contains(td.defName)).ToList())
                    animal.training.Train(trainableDef, null, complete: true);
            }
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();
    }
}

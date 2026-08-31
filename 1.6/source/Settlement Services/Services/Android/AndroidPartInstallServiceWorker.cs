using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Settlement_Services.Framework.Compat;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Framework.Workers.Results;

namespace Settlement_Services.Services.Android
{
    internal class AndroidInstallOption
    {
        public RecipeDef recipe;
        public BodyPartRecord part;
        public string Key => recipe.defName + "@" + part.Index;
        public string Label => recipe.LabelCap + " (" + part.Label + ")";
    }

    public class AndroidPartInstallServiceWorker : SettlementServiceWorker
    {
        private const string PartGroupKey = "SettlementServices.Label.AndroidPartInstallChoice";
        private const float MaterialMarkupPct = 1.4f;

        public override ServiceAvailabilityReport CanOffer(SettlementServiceContext ctx)
        {
            Thing android = ctx.SelectedTarget;
            if (android == null) return ServiceAvailabilityReport.Available;
            return InstallableParts(android as Pawn).Any()
                ? ServiceAvailabilityReport.Available
                : ServiceAvailabilityReport.Unavailable("SettlementServices.Error.NoAndroidPartsInstallable");
        }

        public override IEnumerable<ServiceDisplayOption> GetDisplayOptions(SettlementServiceContext ctx)
        {
            Pawn android = ctx.SelectedTarget as Pawn;
            if (android == null) yield break;
            foreach (AndroidInstallOption option in InstallableParts(android))
                yield return new ServiceDisplayOption { key = option.Key, label = option.Label, groupKey = PartGroupKey };
        }

        public override IEnumerable<ServiceLineItem> BuildQuoteLineItems(SettlementServiceRequest request)
        {
            var items = new List<ServiceLineItem>();
            AndroidInstallOption option = ResolveOption(request.target.thing as Pawn, request.selectedOptionKeys);
            int materialsCost = AndroidMaterialPlanner.SettlementSuppliedCost(request, PartCostList(option?.recipe), MaterialMarkupPct);
            if (materialsCost > 0) items.Add(new ServiceLineItem("SettlementServices.LineItem.AndroidPartInstallMaterials", materialsCost));
            return items;
        }

        public override ServiceInputPlan PlanInputs(SettlementServiceRequest request, SettlementServiceQuote quote)
        {
            AndroidInstallOption option = ResolveOption(request.target.thing as Pawn, request.selectedOptionKeys);
            return option != null ? AndroidMaterialPlanner.PlanInputs(request, PartCostList(option.recipe)) : ServiceInputPlan.None;
        }

        public override List<ServiceStockRequirement> GetDynamicStockRequirements(SettlementServiceRequest request)
        {
            AndroidInstallOption option = ResolveOption(request.target.thing as Pawn, request.selectedOptionKeys);
            return option != null ? AndroidMaterialPlanner.RequirementsFor(PartCostList(option.recipe)) : new List<ServiceStockRequirement>();
        }

        public override string ValidateUnitRequest(SettlementServiceRequest request)
        {
            if (request.selectedOptionKeys.Count == 0) return null;
            Pawn android = request.target.thing as Pawn;
            return ResolveOption(android, request.selectedOptionKeys) != null ? null : "SettlementServices.Error.NoAndroidPartsInstallable";
        }

        public override ServiceStartResult Start(ServiceJobContext ctx)
        {
            Pawn android = ctx.CurrentTarget?.liveThing as Pawn;
            AndroidInstallOption option = android != null ? ResolveOption(android, ctx.Job.selectedOptionKeys) : null;
            return option != null ? ServiceStartResult.Ok : ServiceStartResult.Fail("SettlementServices.Error.AndroidPartNoLongerInstallable");
        }

        public override ServiceCompletionResult Complete(ServiceJobContext ctx)
        {
            Pawn android = ctx.CurrentTarget?.liveThing as Pawn;
            AndroidInstallOption option = android != null ? ResolveOption(android, ctx.Job.selectedOptionKeys) : null;
            if (option == null) return ServiceCompletionResult.Fail("SettlementServices.Error.AndroidPartNoLongerInstallable");

            option.recipe.Worker.ApplyOnPawn(android, option.part, null, new List<Thing>(), null);
            return ServiceCompletionResult.Ok();
        }

        public override ServiceCancelResult Cancel(ServiceJobContext ctx, bool playerInitiated) => ServiceCancelResult.Ok();

        private static IEnumerable<AndroidInstallOption> InstallableParts(Pawn android)
        {
            if (!AndroidsAdapter.IsAndroid(android)) yield break;
            Type partType = AndroidsAdapter.AndroidPartHediffType;
            Type reactorType = AndroidsAdapter.ReactorHediffType;
            if (partType == null) yield break;

            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (recipe.addsHediff == null || !partType.IsAssignableFrom(recipe.addsHediff.hediffClass)) continue;
                if (reactorType != null && reactorType.IsAssignableFrom(recipe.addsHediff.hediffClass)) continue;

                foreach (BodyPartRecord part in recipe.Worker.GetPartsToApplyOn(android, recipe))
                    if (recipe.Worker.AvailableOnNow(android, part))
                        yield return new AndroidInstallOption { recipe = recipe, part = part };
            }
        }

        private static AndroidInstallOption ResolveOption(Pawn android, IReadOnlyList<string> selectedOptionKeys)
        {
            if (android == null || selectedOptionKeys == null || selectedOptionKeys.Count == 0) return null;
            List<AndroidInstallOption> options = InstallableParts(android).ToList();
            return selectedOptionKeys.Select(k => options.FirstOrDefault(o => o.Key == k)).FirstOrDefault(o => o != null);
        }

        private static List<ThingDefCountClass> PartCostList(RecipeDef recipe) =>
            recipe?.fixedIngredientFilter?.AllowedThingDefs?.FirstOrDefault()?.costList;
    }
}

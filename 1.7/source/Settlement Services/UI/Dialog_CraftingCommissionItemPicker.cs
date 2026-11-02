using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Services.Crafting;

namespace Settlement_Services.UI
{
    public class Dialog_CraftingCommissionItemPicker : Dialog_CommissionPickerBase<CraftingCommissionRecipe>
    {
        private static readonly string[] TypeOrder = { CommissionPickerKeys.Weapons, CommissionPickerKeys.Apparel, CommissionPickerKeys.Other };

        private readonly Action<CraftingCommissionLine> onAdd;

        public Dialog_CraftingCommissionItemPicker(ServiceRequestSession session, List<CraftingCommissionRecipe> eligibleRecipes, Action<CraftingCommissionLine> onAdd)
            : base(session, CommissionPickerKind.Crafting, BuildEntries(eligibleRecipes), TypeLabel, TypeOrder)
        {
            this.onAdd = onAdd;
        }

        protected override string Title => "SettlementServices.Label.AddCommissionItemTitle".Translate();

        protected override string ItemLabel(CraftingCommissionRecipe item) => item.label;

        protected override ThingDef InfoCardDef(CraftingCommissionRecipe item) => item.primaryOutput;

        protected override bool PossibleWithAnyStuff(CraftingCommissionRecipe item) => availability.CraftingPossibleWithAnyStuff(item, 1);

        protected override bool StuffRequirementMet =>
            selected.Kind != CraftingRecipeKind.Vanilla || !selected.madeFromStuff || selectedStuff != null;

        protected override bool SelectionPossible() =>
            selected != null && availability.CraftingPossible(selected, selectedStuff, quantity);

        protected override void DrawRowIcon(Rect rect, CraftingCommissionRecipe item)
        {
            Def iconDef = item.Kind == CraftingRecipeKind.Vanilla ? (Def)item.VanillaRecipeDef : item.primaryOutput;
            Widgets.DefIcon(rect, iconDef, item.FixedOutputStuff, drawPlaceholder: true);
        }

        protected override void OnItemSelected(CraftingCommissionRecipe recipe)
        {
            if (recipe.Kind == CraftingRecipeKind.Vanilla && recipe.madeFromStuff)
            {
                List<ThingDef> stuffs = availability.CraftingStuffs(recipe);
                selectedStuff = (materialFilter ? stuffs.FirstOrDefault(s => availability.CraftingPossible(recipe, s, 1)) : null) ?? stuffs.FirstOrDefault();
            }
            else
            {
                selectedStuff = null;
            }
        }

        protected override void DrawDetailBody(Listing_Standard listing)
        {
            if (!selected.description.NullOrEmpty()) listing.Label(selected.description);

            if (selected.Kind == CraftingRecipeKind.Vanilla && selected.madeFromStuff) DrawMaterialSelector(listing);

            if (materialFilter && !SelectionPossible())
            {
                GUI.color = ColorLibrary.RedReadable;
                listing.Label("SettlementServices.Label.CommissionFilterUnavailable".Translate());
                GUI.color = Color.white;
            }
        }

        protected override void CommitAdd() =>
            onAdd(new CraftingCommissionLine(selected.identity.kind, selected.identity.defName, quantity, selectedStuff?.defName));

        private void DrawMaterialSelector(Listing_Standard listing)
        {
            listing.Label("SettlementServices.Label.CraftingStuffChoice".Translate());
            Rect row = listing.GetRect(28f);
            string label = selectedStuff != null ? selectedStuff.LabelCap : "SettlementServices.Label.NoTargetSelected".Translate();
            if (Widgets.ButtonText(row, label))
            {
                IEnumerable<ThingDef> options = availability.CraftingStuffs(selected);
                if (materialFilter) options = options.Where(s => availability.CraftingPossible(selected, s, quantity));
                var floatOptions = options.Select(s => new FloatMenuOption(s.LabelCap, () => selectedStuff = s)).ToList();
                if (floatOptions.Count > 0) Find.WindowStack.Add(new FloatMenu(floatOptions));
            }
            listing.Gap(4f);
        }

        private static string TypeLabel(string key)
        {
            switch (key)
            {
                case CommissionPickerKeys.Weapons: return "SettlementServices.Label.CommissionTypeWeapons".Translate();
                case CommissionPickerKeys.Apparel: return "SettlementServices.Label.CommissionTypeApparel".Translate();
                default: return "SettlementServices.Label.CommissionTypeOtherItems".Translate();
            }
        }

        private static List<CommissionPickerEntry<CraftingCommissionRecipe>> BuildEntries(List<CraftingCommissionRecipe> recipes)
        {
            var entries = new List<CommissionPickerEntry<CraftingCommissionRecipe>>(recipes.Count);
            foreach (CraftingCommissionRecipe recipe in recipes)
            {
                ThingDef output = recipe.primaryOutput;
                string typeKey = output == null ? CommissionPickerKeys.Other
                    : output.IsWeapon ? CommissionPickerKeys.Weapons
                    : output.IsApparel ? CommissionPickerKeys.Apparel
                    : CommissionPickerKeys.Other;
                entries.Add(CommissionPickerEntry<CraftingCommissionRecipe>.Create(
                    recipe, output, CommissionOrigin.IsOfficial(recipe), typeKey, true, CommissionOrigin.DefiningDef(recipe),
                    recipe.label, recipe.description, output?.LabelCap.ToString(), recipe.identity.defName, output?.defName));
            }
            return entries;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Services.Crafting;

namespace Settlement_Services.UI
{
    public class Dialog_CraftingCommissionItemPicker : Window
    {
        private const float RowHeight = 32f;
        private const float IconSize = 28f;
        private const float ListHeight = 260f;

        private readonly Settlement settlement;
        private readonly List<CraftingCommissionRecipe> eligibleRecipes;
        private readonly Action<CraftingCommissionLine> onAdd;

        private string searchText = string.Empty;
        private Vector2 scrollPosition;
        private CraftingCommissionRecipe selectedRecipe;
        private ThingDef selectedStuff;
        private int quantity = 1;
        private string quantityBuffer;

        public override Vector2 InitialSize => new Vector2(520f, 560f);

        public Dialog_CraftingCommissionItemPicker(ServiceRequestSession session, List<CraftingCommissionRecipe> eligibleRecipes, Action<CraftingCommissionLine> onAdd)
        {
            settlement = session.settlement;
            this.eligibleRecipes = eligibleRecipes;
            this.onAdd = onAdd;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "SettlementServices.Label.AddCommissionItemTitle".Translate());
            Text.Font = GameFont.Small;

            Rect searchLabelRect = new Rect(inRect.x, inRect.y + 36f, 60f, 28f);
            Widgets.Label(searchLabelRect, "SettlementServices.Label.CommissionSearchLabel".Translate());
            Rect searchRect = new Rect(searchLabelRect.xMax + 4f, inRect.y + 36f, inRect.width - searchLabelRect.width - 4f, 28f);
            searchText = Widgets.TextField(searchRect, searchText);

            Rect listRect = new Rect(inRect.x, searchRect.yMax + 6f, inRect.width, ListHeight);
            DrawRecipeList(listRect);

            Rect detailRect = new Rect(inRect.x, listRect.yMax + 10f, inRect.width, inRect.height - listRect.yMax - 10f - 45f);
            DrawSelectedDetail(detailRect);

            DrawBottomButtons(inRect);
        }

        private void DrawRecipeList(Rect rect)
        {
            List<CraftingCommissionRecipe> filtered = FilterRecipes();
            if (filtered.Count == 0)
            {
                Widgets.NoneLabelCenteredVertically(rect, "SettlementServices.Label.NoCommissionSearchResults".Translate());
                return;
            }

            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, filtered.Count * RowHeight);
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            float y = 0f;
            foreach (CraftingCommissionRecipe recipe in filtered)
            {
                Rect rowRect = new Rect(0f, y, viewRect.width, RowHeight - 2f);
                DrawRecipeRow(rowRect, recipe);
                y += RowHeight;
            }
            Widgets.EndScrollView();
        }

        private List<CraftingCommissionRecipe> FilterRecipes()
        {
            if (searchText.NullOrEmpty()) return eligibleRecipes;

            string needle = searchText.ToLowerInvariant();
            return eligibleRecipes.Where(r =>
                r.label.ToLowerInvariant().Contains(needle)
                || (!r.description.NullOrEmpty() && r.description.ToLowerInvariant().Contains(needle))
                || (r.primaryOutput != null && r.primaryOutput.LabelCap.ToString().ToLowerInvariant().Contains(needle))
                || r.identity.defName.ToLowerInvariant().Contains(needle)
            ).ToList();
        }

        private void DrawRecipeRow(Rect rowRect, CraftingCommissionRecipe recipe)
        {
            bool selected = recipe == selectedRecipe;
            if (selected) Widgets.DrawOptionBackground(rowRect, true);
            else Widgets.DrawHighlightIfMouseover(rowRect);

            Rect iconRect = new Rect(rowRect.x + 2f, rowRect.y + (rowRect.height - IconSize) / 2f, IconSize, IconSize);
            Def iconDef = recipe.Kind == CraftingRecipeKind.Vanilla ? (Def)recipe.VanillaRecipeDef : recipe.primaryOutput;
            Widgets.DefIcon(iconRect, iconDef, recipe.FixedOutputStuff, drawPlaceholder: true);

            Rect labelRect = new Rect(iconRect.xMax + 6f, rowRect.y, rowRect.width - iconRect.xMax - 6f, rowRect.height);
            TextAnchor prevAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, recipe.label);
            Text.Anchor = prevAnchor;

            if (Widgets.ButtonInvisible(rowRect)) SelectRecipe(recipe);
        }

        private void SelectRecipe(CraftingCommissionRecipe recipe)
        {
            selectedRecipe = recipe;
            quantity = 1;
            quantityBuffer = null;

            if (recipe.Kind == CraftingRecipeKind.Vanilla && recipe.madeFromStuff)
            {
                TechLevel techCeiling = CraftingCommissionCatalog.EffectiveTechCeiling(recipe, settlement);
                selectedStuff = CraftingMaterialPlanner.EligibleStuffs(recipe.primaryOutput, techCeiling, settlement).FirstOrDefault();
            }
            else
            {
                selectedStuff = null;
            }
        }

        private void DrawSelectedDetail(Rect rect)
        {
            if (selectedRecipe == null)
            {
                Widgets.NoneLabelCenteredVertically(rect, "SettlementServices.Label.NoCommissionItemSelected".Translate());
                return;
            }

            var listing = new Listing_Standard();
            listing.Begin(rect);

            listing.Label(selectedRecipe.label);
            if (!selectedRecipe.description.NullOrEmpty()) listing.Label(selectedRecipe.description);

            if (selectedRecipe.Kind == CraftingRecipeKind.Vanilla && selectedRecipe.madeFromStuff) DrawMaterialSelector(listing);
            DrawQuantityRow(listing);

            listing.End();
        }

        private void DrawMaterialSelector(Listing_Standard listing)
        {
            listing.Label("SettlementServices.Label.CraftingStuffChoice".Translate());
            Rect row = listing.GetRect(28f);
            string label = selectedStuff != null ? selectedStuff.LabelCap : "SettlementServices.Label.NoTargetSelected".Translate();
            if (Widgets.ButtonText(row, label))
            {
                TechLevel techCeiling = CraftingCommissionCatalog.EffectiveTechCeiling(selectedRecipe, settlement);
                List<ThingDef> options = CraftingMaterialPlanner.EligibleStuffs(selectedRecipe.primaryOutput, techCeiling, settlement).ToList();
                var floatOptions = options.Select(s => new FloatMenuOption(s.LabelCap, () => selectedStuff = s)).ToList();
                Find.WindowStack.Add(new FloatMenu(floatOptions));
            }
            listing.Gap(4f);
        }

        private void DrawQuantityRow(Listing_Standard listing)
        {
            Rect row = listing.GetRect(28f);
            Rect labelRect = new Rect(row.x, row.y, row.width - 116f, row.height);
            Rect minusRect = new Rect(row.xMax - 108f, row.y, 28f, 28f);
            Rect amountRect = new Rect(row.xMax - 72f, row.y, 44f, 28f);
            Rect plusRect = new Rect(row.xMax - 28f, row.y, 28f, 28f);

            Widgets.Label(labelRect, "SettlementServices.Label.Quantity".Translate());
            if (Widgets.ButtonText(minusRect, "-") && quantity > 1) quantity--;

            if (quantityBuffer == null || !int.TryParse(quantityBuffer, out int bufferedValue) || bufferedValue != quantity)
                quantityBuffer = quantity.ToString();
            Widgets.TextFieldNumeric(amountRect, ref quantity, ref quantityBuffer, 1, 9999);

            if (Widgets.ButtonText(plusRect, "+") && quantity < 9999) quantity++;
        }

        private void DrawBottomButtons(Rect inRect)
        {
            Rect cancelRect = new Rect(inRect.width - 300f, inRect.height - 35f, 140f, 35f);
            Rect addRect = new Rect(inRect.width - 150f, inRect.height - 35f, 140f, 35f);

            if (Widgets.ButtonText(cancelRect, "CancelButton".Translate())) Close();

            bool canAdd = selectedRecipe != null && quantity > 0
                && (selectedRecipe.Kind != CraftingRecipeKind.Vanilla || !selectedRecipe.madeFromStuff || selectedStuff != null);
            bool prevEnabled = GUI.enabled;
            GUI.enabled = canAdd;
            if (Widgets.ButtonText(addRect, "SettlementServices.Button.AddCommissionItem".Translate()) && canAdd)
            {
                onAdd(new CraftingCommissionLine(selectedRecipe.identity.kind, selectedRecipe.identity.defName, quantity, selectedStuff?.defName));
                Close();
            }
            GUI.enabled = prevEnabled;
        }
    }
}

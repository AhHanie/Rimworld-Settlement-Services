using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Services.Construction;

namespace Settlement_Services.UI
{
    public class Dialog_BuildingCommissionItemPicker : Window
    {
        private const float RowHeight = 32f;
        private const float IconSize = 28f;
        private const float ListHeight = 260f;

        private readonly Settlement settlement;
        private readonly List<ThingDef> eligibleBuildings;
        private readonly Action<BuildingCommissionLine> onAdd;

        private string searchText = string.Empty;
        private Vector2 scrollPosition;
        private ThingDef selectedBuilding;
        private ThingDef selectedStuff;
        private int quantity = 1;
        private string quantityBuffer;

        public override Vector2 InitialSize => new Vector2(520f, 560f);

        public Dialog_BuildingCommissionItemPicker(ServiceRequestSession session, List<ThingDef> eligibleBuildings, Action<BuildingCommissionLine> onAdd)
        {
            settlement = session.settlement;
            this.eligibleBuildings = eligibleBuildings;
            this.onAdd = onAdd;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "SettlementServices.Label.AddConstructionItemTitle".Translate());
            Text.Font = GameFont.Small;

            Rect searchLabelRect = new Rect(inRect.x, inRect.y + 36f, 60f, 28f);
            Widgets.Label(searchLabelRect, "SettlementServices.Label.CommissionSearchLabel".Translate());
            Rect searchRect = new Rect(searchLabelRect.xMax + 4f, inRect.y + 36f, inRect.width - searchLabelRect.width - 4f, 28f);
            searchText = Widgets.TextField(searchRect, searchText);

            Rect listRect = new Rect(inRect.x, searchRect.yMax + 6f, inRect.width, ListHeight);
            DrawBuildingList(listRect);

            Rect detailRect = new Rect(inRect.x, listRect.yMax + 10f, inRect.width, inRect.height - listRect.yMax - 10f - 45f);
            DrawSelectedDetail(detailRect);

            DrawBottomButtons(inRect);
        }

        private void DrawBuildingList(Rect rect)
        {
            List<ThingDef> filtered = FilterBuildings();
            if (filtered.Count == 0)
            {
                Widgets.NoneLabelCenteredVertically(rect, "SettlementServices.Label.NoCommissionSearchResults".Translate());
                return;
            }

            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, filtered.Count * RowHeight);
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            float y = 0f;
            foreach (ThingDef building in filtered)
            {
                Rect rowRect = new Rect(0f, y, viewRect.width, RowHeight - 2f);
                DrawBuildingRow(rowRect, building);
                y += RowHeight;
            }
            Widgets.EndScrollView();
        }

        private List<ThingDef> FilterBuildings()
        {
            if (searchText.NullOrEmpty()) return eligibleBuildings;

            string needle = searchText.ToLowerInvariant();
            return eligibleBuildings.Where(b =>
                b.LabelCap.ToString().ToLowerInvariant().Contains(needle)
                || (!b.description.NullOrEmpty() && b.description.ToLowerInvariant().Contains(needle))
                || b.defName.ToLowerInvariant().Contains(needle)
            ).ToList();
        }

        private void DrawBuildingRow(Rect rowRect, ThingDef building)
        {
            bool selected = building == selectedBuilding;
            if (selected) Widgets.DrawOptionBackground(rowRect, true);
            else Widgets.DrawHighlightIfMouseover(rowRect);

            Rect iconRect = new Rect(rowRect.x + 2f, rowRect.y + (rowRect.height - IconSize) / 2f, IconSize, IconSize);
            Widgets.DefIcon(iconRect, building, drawPlaceholder: true);

            Rect labelRect = new Rect(iconRect.xMax + 6f, rowRect.y, rowRect.width - iconRect.xMax - 6f, rowRect.height);
            TextAnchor prevAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, building.LabelCap);
            Text.Anchor = prevAnchor;

            if (Widgets.ButtonInvisible(rowRect)) SelectBuilding(building);
        }

        private void SelectBuilding(ThingDef building)
        {
            selectedBuilding = building;
            quantity = 1;
            quantityBuffer = null;
            selectedStuff = building.MadeFromStuff ? BuildingCommissionCatalog.EligibleStuffs(building, settlement).FirstOrDefault() : null;
        }

        private void DrawSelectedDetail(Rect rect)
        {
            if (selectedBuilding == null)
            {
                Widgets.NoneLabelCenteredVertically(rect, "SettlementServices.Label.NoCommissionItemSelected".Translate());
                return;
            }

            var listing = new Listing_Standard();
            listing.Begin(rect);

            listing.Label(selectedBuilding.LabelCap);
            if (!selectedBuilding.description.NullOrEmpty()) listing.Label(selectedBuilding.description);

            bool needsStuff = selectedBuilding.MadeFromStuff;
            if (needsStuff) DrawMaterialSelector(listing);

            if (needsStuff && selectedStuff == null)
                listing.Label("SettlementServices.Label.NoEligibleConstructionMaterial".Translate());
            else
                DrawBillPreview(listing);

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
                List<ThingDef> options = BuildingCommissionCatalog.EligibleStuffs(selectedBuilding, settlement).ToList();
                var floatOptions = options.Select(s => new FloatMenuOption(s.LabelCap, () => selectedStuff = s)).ToList();
                Find.WindowStack.Add(new FloatMenu(floatOptions));
            }
            listing.Gap(4f);
        }

        private void DrawBillPreview(Listing_Standard listing)
        {
            List<ThingDefCountClass> cost = selectedBuilding.CostListAdjusted(selectedStuff);
            foreach (ThingDefCountClass item in cost)
                listing.Label("SettlementServices.Label.RawMaterialLine".Translate(item.thingDef.LabelCap, item.count * quantity));

            int workTicks = Mathf.CeilToInt(selectedBuilding.GetStatValueAbstract(StatDefOf.WorkToBuild, selectedStuff) * quantity * 2.5f);
            listing.Label("SettlementServices.Label.ConstructionWorkload".Translate(workTicks.ToStringTicksToPeriod()));
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

            bool canAdd = selectedBuilding != null && quantity > 0 && (!selectedBuilding.MadeFromStuff || selectedStuff != null);
            bool prevEnabled = GUI.enabled;
            GUI.enabled = canAdd;
            if (Widgets.ButtonText(addRect, "SettlementServices.Button.AddCommissionItem".Translate()) && canAdd)
            {
                onAdd(new BuildingCommissionLine(selectedBuilding.defName, quantity, selectedStuff?.defName));
                Close();
            }
            GUI.enabled = prevEnabled;
        }
    }
}

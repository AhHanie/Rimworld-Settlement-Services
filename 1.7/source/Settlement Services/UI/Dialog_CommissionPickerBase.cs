using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Settlement_Services.UI
{
    public abstract class Dialog_CommissionPickerBase<T> : Window where T : class
    {
        private const float RowHeight = 32f;
        private const float IconSize = 28f;
        private const float InfoButtonSize = 24f;
        private const float QuantityRowHeight = 28f;
        private const float QuantityGap = 4f;
        private const float ButtonHeight = 35f;
        private const double ScanBudgetMilliseconds = 8d;

        private readonly CommissionPickerController<T> controller;
        private readonly ServiceRequestSession session;
        private readonly string labelAdd = "SettlementServices.Button.AddCommissionItem".Translate();
        private readonly string labelAddAndClose = "SettlementServices.Button.AddCommissionItemAndClose".Translate();

        private Vector2 scrollPosition;
        private Vector2 detailScrollPosition;
        private float detailScrollHeight;
        private int seenVersion;

        protected readonly bool materialFilter;
        private protected CommissionAvailability availability;
        protected T selected;
        protected ThingDef selectedStuff;
        protected int quantity = 1;
        protected string quantityBuffer;

        internal Dialog_CommissionPickerBase(ServiceRequestSession session, CommissionPickerKind kind, List<CommissionPickerEntry<T>> entries, Func<string, string> typeLabel, IList<string> typeOrder)
        {
            this.session = session;
            materialFilter = ModSettings.Current.commissionPickerOnlyPossibleWithCurrentMaterials;
            availability = new CommissionAvailability(session);
            controller = new CommissionPickerController<T>(kind, entries, typeLabel, typeOrder, materialFilter ? (Func<T, bool>)AvailabilityPredicate : null);
            seenVersion = controller.Version;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
        }

        public override Vector2 InitialSize => new Vector2(Mathf.Min(560f, Verse.UI.screenWidth), Mathf.Min(720f, Verse.UI.screenHeight));

        protected abstract string Title { get; }
        protected abstract string ItemLabel(T item);
        protected abstract void DrawRowIcon(Rect rect, T item);
        protected abstract ThingDef InfoCardDef(T item);
        protected abstract bool PossibleWithAnyStuff(T item);
        protected abstract void OnItemSelected(T item);
        protected abstract void DrawDetailBody(Listing_Standard listing);
        protected abstract bool StuffRequirementMet { get; }
        protected abstract bool SelectionPossible();
        protected abstract void CommitAdd();

        private bool AvailabilityPredicate(T item) => PossibleWithAnyStuff(item);

        public override void DoWindowContents(Rect inRect)
        {
            if (Event.current.type == EventType.Layout)
            {
                controller.OnLayout(ScanBudgetMilliseconds);
                if (controller.Version != seenVersion)
                {
                    seenVersion = controller.Version;
                    scrollPosition = Vector2.zero;
                    if (selected != null && !controller.IsMatched(selected)) ClearSelection();
                }
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), Title);
            Text.Font = GameFont.Small;

            if (!controller.ScanComplete)
            {
                TextAnchor titleAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, 30f), "SettlementServices.Label.CommissionFilterEvaluating".Translate());
                GUI.color = Color.white;
                Text.Anchor = titleAnchor;
            }

            Rect controlsRect = new Rect(inRect.x, inRect.y + 34f, inRect.width, CommissionPickerController<T>.ControlsHeight);
            controller.DrawControls(controlsRect);

            float listTop = controlsRect.yMax + 4f;
            float available = inRect.height - listTop - ButtonHeight - 18f;
            float listHeight = Mathf.Clamp(available * 0.45f, 72f, 300f);
            Rect listRect = new Rect(inRect.x, listTop, inRect.width, listHeight);
            DrawList(listRect);

            Rect detailRect = new Rect(inRect.x, listRect.yMax + 8f, inRect.width, Mathf.Max(0f, inRect.height - listRect.yMax - 8f - ButtonHeight - 10f));
            DrawSelectedDetail(detailRect);

            DrawBottomButtons(inRect);
        }

        private void DrawList(Rect rect)
        {
            List<T> visible = controller.Visible;
            if (visible.Count == 0)
            {
                DrawEmptyState(rect);
                return;
            }

            float totalHeight = visible.Count * RowHeight;
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, totalHeight);
            scrollPosition.y = Mathf.Clamp(scrollPosition.y, 0f, Mathf.Max(0f, totalHeight - rect.height));
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            int first = Mathf.Max(0, Mathf.FloorToInt(scrollPosition.y / RowHeight) - 1);
            int last = Mathf.Min(visible.Count - 1, Mathf.CeilToInt((scrollPosition.y + rect.height) / RowHeight) + 1);
            for (int i = first; i <= last; i++)
                DrawRow(new Rect(0f, i * RowHeight, viewRect.width, RowHeight - 2f), visible[i]);
            Widgets.EndScrollView();
        }

        private void DrawEmptyState(Rect rect)
        {
            if (controller.TotalEligible == 0)
            {
                Widgets.NoneLabelCenteredVertically(rect, "SettlementServices.Label.CommissionNothingEligible".Translate());
                return;
            }
            if (!controller.ScanComplete)
            {
                Widgets.NoneLabelCenteredVertically(rect, "SettlementServices.Label.CommissionFilterEvaluating".Translate());
                return;
            }

            bool resetAvailable = controller.AnyFilterActive;
            bool modAvailable = controller.SourceFilterActive;
            bool clearAvailable = controller.HasSearch;
            int actions = (resetAvailable ? 1 : 0) + (modAvailable ? 1 : 0) + (clearAvailable ? 1 : 0);
            float actionsHeight = actions * (30f + 4f);
            Rect labelRect = new Rect(rect.x, rect.y, rect.width, Mathf.Max(30f, (rect.height - actionsHeight) / 2f));
            Widgets.NoneLabelCenteredVertically(labelRect, "SettlementServices.Label.CommissionNoMatchesForFilters".Translate());

            float y = labelRect.yMax;
            float width = Mathf.Min(rect.width, 240f);
            float x = rect.x + (rect.width - width) / 2f;
            if (clearAvailable)
            {
                if (Widgets.ButtonText(new Rect(x, y, width, 30f), "SettlementServices.Button.CommissionSearchClear".Translate())) controller.ClearSearch();
                y += 34f;
            }
            if (modAvailable)
            {
                if (Widgets.ButtonText(new Rect(x, y, width, 30f), "SettlementServices.Button.CommissionShowAllMods".Translate())) controller.ShowAllMods();
                y += 34f;
            }
            if (resetAvailable)
            {
                if (Widgets.ButtonText(new Rect(x, y, width, 30f), "SettlementServices.Button.CommissionResetFilters".Translate())) controller.ResetFilters();
            }
        }

        private void DrawRow(Rect rowRect, T item)
        {
            bool isSelected = item == selected;
            if (isSelected) Widgets.DrawOptionBackground(rowRect, true);
            else Widgets.DrawHighlightIfMouseover(rowRect);

            Rect iconRect = new Rect(rowRect.x + 2f, rowRect.y + (rowRect.height - IconSize) / 2f, IconSize, IconSize);
            DrawRowIcon(iconRect, item);

            Rect infoRect = new Rect(rowRect.xMax - InfoButtonSize - 4f, rowRect.y + (rowRect.height - InfoButtonSize) / 2f, InfoButtonSize, InfoButtonSize);
            ThingDef infoDef = InfoCardDef(item);
            if (infoDef != null) Widgets.InfoCardButton(infoRect.x, infoRect.y, (Def)infoDef);

            Rect labelRect = new Rect(iconRect.xMax + 6f, rowRect.y, infoRect.x - iconRect.xMax - 10f, rowRect.height);
            TextAnchor previousAnchor = Text.Anchor;
            bool wordWrap = Text.WordWrap;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            Widgets.Label(labelRect, ItemLabel(item));
            Text.WordWrap = wordWrap;
            Text.Anchor = previousAnchor;

            CommissionPickerEntry<T> entry = controller.EntryFor(item);
            if (entry != null) TooltipHandler.TipRegion(labelRect, entry.SourceName);

            if (Widgets.ButtonInvisible(rowRect)) SelectItem(item);
        }

        private void SelectItem(T item)
        {
            selected = item;
            quantity = 1;
            quantityBuffer = null;
            detailScrollPosition = Vector2.zero;
            detailScrollHeight = 0f;
            OnItemSelected(item);
        }

        private void ClearSelection()
        {
            selected = null;
            selectedStuff = null;
            quantity = 1;
            quantityBuffer = null;
            detailScrollPosition = Vector2.zero;
            detailScrollHeight = 0f;
        }

        private void DrawSelectedDetail(Rect rect)
        {
            if (selected == null)
            {
                Widgets.NoneLabelCenteredVertically(rect, "SettlementServices.Label.NoCommissionItemSelected".Translate());
                return;
            }

            Rect quantityRect = new Rect(rect.x, rect.yMax - QuantityRowHeight, rect.width, QuantityRowHeight);
            Rect bodyRect = new Rect(rect.x, rect.y, rect.width, Mathf.Max(0f, quantityRect.y - QuantityGap - rect.y));

            Rect viewRect = new Rect(0f, 0f, bodyRect.width - 16f, Mathf.Max(bodyRect.height, detailScrollHeight));
            Widgets.BeginScrollView(bodyRect, ref detailScrollPosition, viewRect);
            var listing = new Listing_Standard { maxOneColumn = true };
            listing.Begin(viewRect);

            DrawDetailHeader(listing);
            DrawDetailBody(listing);

            detailScrollHeight = listing.CurHeight;
            listing.End();
            Widgets.EndScrollView();

            DrawQuantityRow(quantityRect);
        }

        private void DrawDetailHeader(Listing_Standard listing)
        {
            Rect titleRow = listing.GetRect(InfoButtonSize);
            Rect titleRect = new Rect(titleRow.x, titleRow.y, titleRow.width - InfoButtonSize - 4f, titleRow.height);
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(titleRect, ItemLabel(selected));
            Text.Anchor = previousAnchor;

            ThingDef infoDef = InfoCardDef(selected);
            if (infoDef != null)
            {
                if (selectedStuff != null) Widgets.InfoCardButton(titleRow.xMax - InfoButtonSize, titleRow.y, infoDef, selectedStuff);
                else Widgets.InfoCardButton(titleRow.xMax - InfoButtonSize, titleRow.y, (Def)infoDef);
            }

            CommissionPickerEntry<T> entry = controller.EntryFor(selected);
            if (entry == null) return;

            GUI.color = Color.gray;
            string sourceTip = entry.PackageId.NullOrEmpty() ? null : "SettlementServices.Label.CommissionSourcePackage".Translate(entry.PackageId).Resolve();
            listing.Label("SettlementServices.Label.CommissionItemSource".Translate(entry.SourceName), -1f, sourceTip);
            if (!entry.RecipeSource.NullOrEmpty())
                listing.Label("SettlementServices.Label.CommissionRecipeSource".Translate(entry.RecipeSource));
            GUI.color = Color.white;
            listing.Gap(2f);
        }

        private void DrawQuantityRow(Rect row)
        {
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

        private bool CanAdd() =>
            selected != null && quantity > 0 && controller.IsMatched(selected)
            && StuffRequirementMet
            && (!materialFilter || SelectionPossible());

        private void DrawBottomButtons(Rect inRect)
        {
            float y = inRect.yMax - ButtonHeight;
            float closeWidth = Mathf.Min(170f, inRect.width * 0.34f);
            float addWidth = Mathf.Min(140f, inRect.width * 0.28f);
            Rect cancelRect = new Rect(inRect.x, y, Mathf.Min(120f, inRect.width * 0.24f), ButtonHeight);
            Rect addAndCloseRect = new Rect(inRect.xMax - closeWidth, y, closeWidth, ButtonHeight);
            Rect addRect = new Rect(addAndCloseRect.x - 8f - addWidth, y, addWidth, ButtonHeight);

            if (Widgets.ButtonText(cancelRect, "CancelButton".Translate())) Close();

            bool canAdd = CanAdd();
            bool previousEnabled = GUI.enabled;
            GUI.enabled = canAdd;
            if (Widgets.ButtonText(addRect, labelAdd) && canAdd)
            {
                CommitAdd();
                ClearSelection();
                availability = new CommissionAvailability(session);
                controller.SetAvailabilityPredicate(materialFilter ? (Func<T, bool>)AvailabilityPredicate : null);
            }
            if (Widgets.ButtonText(addAndCloseRect, labelAddAndClose) && canAdd)
            {
                CommitAdd();
                Close();
            }
            GUI.enabled = previousEnabled;
        }
    }
}

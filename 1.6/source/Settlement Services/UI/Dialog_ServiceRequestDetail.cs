using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using Verse;
using Verse.Sound;
using Settlement_Services.Domain;
using Settlement_Services.Framework;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Validation;
using Settlement_Services.Framework.Workers;
using Settlement_Services.Services.Crafting;

namespace Settlement_Services.UI
{
    public class Dialog_ServiceRequestDetail : Window
    {
        private readonly ServiceRequestSession session;
        private readonly List<CraftingCommissionRecipe> craftingEligibleRecipes;
        private Vector2 scrollPosition;
        private float scrollHeight;
        private string quantityBuffer;
        private string crafterCountBuffer;
        private readonly Dictionary<string, string> craftingMaterialBuffers = new Dictionary<string, string>();

        private const int MaxDisplayedCrafterWorkloads = 6;

        public override Vector2 InitialSize => new Vector2(700f, 620f);

        public Dialog_ServiceRequestDetail(ServiceRequestSession session)
        {
            this.session = session;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;

            if (session.def.Worker is CraftingCommissionServiceWorker)
                craftingEligibleRecipes = CraftingCommissionCatalog.EligibleRecipes(session.settlement).ToList();
        }

        public override void DoWindowContents(Rect inRect)
        {
            bool isCrafting = craftingEligibleRecipes != null;
            List<StockInputRow> stockRows = isCrafting ? new List<StockInputRow>() : ServiceStockInputPicker.BuildRows(session);

            Rect bodyRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - 45f);
            Rect viewRect = new Rect(0f, 0f, bodyRect.width - 16f, Mathf.Max(bodyRect.height, scrollHeight));
            Widgets.BeginScrollView(bodyRect, ref scrollPosition, viewRect);

            Rect contentRect = viewRect;
            contentRect.height = 99999f;
            var listing = new Listing_Standard();
            listing.maxOneColumn = true;
            listing.Begin(contentRect);

            listing.Label(session.def.LabelCap);
            listing.Label(session.def.description);
            listing.GapLine();

            if (session.def.targetRule != ServiceTargetRule.None && session.caravan != null) DrawTargetSection(listing);
            if (session.def.EffectiveBatchMode == ServiceBatchMode.Quantity) DrawQuantitySection(listing);
            if (isCrafting) DrawCraftingCommissionSection(listing);

            CraftingMaterialPreview craftingMaterialPreview = isCrafting && session.caravan != null
                ? CraftingMaterialPreviewBuilder.Build(session)
                : null;

            if (isCrafting) DrawCraftingProductionPlanSection(listing);
            if (isCrafting) DrawCraftingMaterialsSection(listing, craftingMaterialPreview);
            if (stockRows.Count > 0) DrawStockInputSection(listing, stockRows);
            if (!session.def.priorityTiers.NullOrEmpty()) DrawTierSection(listing);
            if (isCrafting) DrawCraftingCrafterSection(listing);
            DrawOptionsSection(listing);
            DrawWorkerSummarySection(listing);

            listing.GapLine();
            SettlementServiceQuote quote = SettlementServiceOrchestrator.RequestQuote(session.BuildRequest(), session.def);
            DrawQuote(listing, quote);

            scrollHeight = listing.CurHeight;
            listing.End();
            Widgets.EndScrollView();

            DrawBottomButtons(inRect, quote);
        }

        private void DrawTargetSection(Listing_Standard listing)
        {
            List<Thing> snapshot = session.targets.ToList();
            for (int i = 0; i < snapshot.Count; i++)
            {
                Thing member = snapshot[i];
                Rect row = listing.GetRect(24f);
                Widgets.Label(new Rect(row.x, row.y, row.width - 24f, row.height), member.LabelCap);
                if (Widgets.ButtonText(new Rect(row.xMax - 24f, row.y, 24f, 24f), "-"))
                {
                    session.RemoveTargetAt(i);
                    session.selectedOptionKeys.Clear();
                }
            }

            if (session.targets.Count == 0)
            {
                Widgets.Label(listing.GetRect(24f), "SettlementServices.Label.NoTargetSelected".Translate());
            }

            bool batchTargets = session.def.EffectiveBatchMode == ServiceBatchMode.Targets;
            bool underMax = session.def.maxBatchCount <= 0 || session.targets.Count < session.def.maxBatchCount;
            bool canAddMore = (batchTargets || session.targets.Count == 0) && underMax;

            Color prevColor = GUI.color;
            if (!canAddMore) GUI.color = Color.gray;
            string addLabel = session.targets.Count == 0 ? "SettlementServices.Label.Target".Translate() : "SettlementServices.Button.AddGroupMember".Translate();
            if (listing.ButtonText(addLabel) && canAddMore)
            {
                ServiceCaravanTargetSelector.OpenTargetPicker(session, thing =>
                {
                    session.AddTarget(thing);
                    session.selectedOptionKeys.Clear();
                });
            }
            GUI.color = prevColor;
        }

        private void DrawQuantitySection(Listing_Standard listing)
        {
            int max = session.def.maxBatchCount > 0 ? session.def.maxBatchCount : int.MaxValue;

            Rect row = listing.GetRect(28f);
            Rect labelRect = new Rect(row.x, row.y, row.width - 116f, row.height);
            Rect minusRect = new Rect(row.xMax - 108f, row.y, 28f, 28f);
            Rect amountRect = new Rect(row.xMax - 72f, row.y, 44f, 28f);
            Rect plusRect = new Rect(row.xMax - 28f, row.y, 28f, 28f);

            Widgets.Label(labelRect, "SettlementServices.Label.Quantity".Translate());
            if (Widgets.ButtonText(minusRect, "-") && session.quantity > 1) session.quantity--;

            if (quantityBuffer == null || !int.TryParse(quantityBuffer, out int bufferedValue) || bufferedValue != session.quantity)
                quantityBuffer = session.quantity.ToString();
            Widgets.TextFieldNumeric(amountRect, ref session.quantity, ref quantityBuffer, 1, max);

            if (Widgets.ButtonText(plusRect, "+") && session.quantity < max) session.quantity++;

            listing.Gap(4f);
        }

        private void DrawCraftingCommissionSection(Listing_Standard listing)
        {
            listing.Label("SettlementServices.Label.CommissionedItems".Translate());

            List<CraftingCommissionLine> lines = session.craftingCommissionLines;
            if (lines.Count == 0)
            {
                listing.Label("SettlementServices.Label.NoCommissionItemsYet".Translate());
            }
            else
            {
                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    CraftingCommissionLine line = lines[i];
                    RecipeNode node = CraftingRecipeDependencyIndex.NodeFor(line.Identity);
                    if (node == null) { lines.RemoveAt(i); continue; }
                    CraftingCommissionRecipe recipe = node.recipe;

                    ThingDef stuff = recipe.Kind == CraftingRecipeKind.Vanilla
                        ? (line.stuffDefName != null ? DefDatabase<ThingDef>.GetNamedSilentFail(line.stuffDefName) : null)
                        : recipe.FixedOutputStuff;

                    Rect row = listing.GetRect(28f);
                    Rect iconRect = new Rect(row.x, row.y, 28f, 28f);
                    Def iconDef = recipe.Kind == CraftingRecipeKind.Vanilla ? (Def)recipe.VanillaRecipeDef : recipe.primaryOutput;
                    Widgets.DefIcon(iconRect, iconDef, stuff, drawPlaceholder: true);

                    int displayCount = recipe.Kind == CraftingRecipeKind.Vanilla
                        ? line.count
                        : recipe.ProductCountPerExecution(recipe.primaryOutput) * line.count;
                    string materialSuffix = stuff != null ? $" ({stuff.LabelCap})" : string.Empty;
                    string label = $"{recipe.label}{materialSuffix} x{displayCount}";
                    Rect labelRect = new Rect(iconRect.xMax + 6f, row.y, row.width - 28f - 6f - 28f, row.height);
                    TextAnchor prevAnchor = Text.Anchor;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(labelRect, label);
                    Text.Anchor = prevAnchor;

                    Rect removeRect = new Rect(row.xMax - 24f, row.y, 24f, 24f);
                    if (Widgets.ButtonText(removeRect, "-")) lines.RemoveAt(i);
                    TooltipHandler.TipRegion(removeRect, "SettlementServices.Button.RemoveCommissionItem".Translate());

                    listing.Gap(4f);
                }
            }

            if (listing.ButtonText("SettlementServices.Button.AddCommissionItem".Translate()))
                Find.WindowStack.Add(new Dialog_CraftingCommissionItemPicker(session, craftingEligibleRecipes, AddOrMergeCommissionLine));

            listing.Gap();
        }

        private void AddOrMergeCommissionLine(CraftingCommissionLine line)
        {
            CraftingCommissionLine existing = session.craftingCommissionLines
                .Find(l => l.recipeKind == line.recipeKind && l.recipeDefName == line.recipeDefName && l.stuffDefName == line.stuffDefName);
            if (existing != null) existing.count += line.count;
            else session.craftingCommissionLines.Add(line);
        }

        private void DrawCraftingProductionPlanSection(Listing_Standard listing)
        {
            if (session.craftingCommissionLines.Count == 0) return;
            if (!CraftingProductionPlanner.TryPlan(session.settlement, session.craftingCommissionLines, session.playerSuppliedInputs, session.craftingCrafterCount, out CraftingProductionPlan plan, out _)) return;

            List<CraftingProductionOperation> intermediateOperations = plan.operations.Where(o => !o.producesFinalResult).ToList();
            if (intermediateOperations.Count == 0 && plan.rawMaterials.Count == 0) return;

            listing.Label("SettlementServices.Label.ProductionPlan".Translate());

            foreach (CraftingProductionOperation op in intermediateOperations)
            {
                RecipeNode node = CraftingRecipeDependencyIndex.NodeFor(op.Identity);
                string label = node?.recipe?.primaryOutput != null ? node.recipe.primaryOutput.LabelCap.ToString() : op.recipeDefName;
                listing.Label("SettlementServices.Label.IntermediateOperation".Translate(label, op.executions));
            }

            if (plan.rawMaterials.Count > 0)
            {
                listing.Label("SettlementServices.Label.RawMaterials".Translate());
                foreach (CraftingMaterialRequirement raw in plan.rawMaterials)
                {
                    ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(raw.thingDefName);
                    string label = thingDef != null ? thingDef.LabelCap.ToString() : raw.thingDefName;
                    listing.Label("SettlementServices.Label.RawMaterialLine".Translate(label, raw.amount));
                }
            }

            listing.Gap();
        }

        private void DrawCraftingMaterialsSection(Listing_Standard listing, CraftingMaterialPreview preview)
        {
            if (preview == null || preview.rows.Count == 0) return;

            listing.Label("SettlementServices.Label.CraftingMaterialsFromCaravan".Translate());
            listing.Label("SettlementServices.Label.CraftingMaterialsFromCaravanExplanation".Translate());
            if (!preview.planValid) listing.Label("SettlementServices.Label.CraftingMaterialsPlanBlocked".Translate());

            foreach (CraftingMaterialPreviewRow row in preview.rows) DrawCraftingMaterialRow(listing, row);

            listing.Gap();
        }

        private void DrawCraftingMaterialRow(Listing_Standard listing, CraftingMaterialPreviewRow row)
        {
            int max = Mathf.Min(row.required, row.carried);
            Rect rowRect = listing.GetRect(28f);

            Rect iconRect = new Rect(rowRect.x, rowRect.y, 28f, 28f);
            Widgets.DefIcon(iconRect, row.thingDef, drawPlaceholder: true);

            Rect labelRect = new Rect(iconRect.xMax + 6f, rowRect.y, 130f, rowRect.height);
            Rect infoRect = new Rect(labelRect.xMax + 6f, rowRect.y, 150f, rowRect.height);
            Rect minusRect = new Rect(infoRect.xMax + 6f, rowRect.y, 24f, 24f);
            Rect amountRect = new Rect(minusRect.xMax + 2f, rowRect.y, 36f, 24f);
            Rect plusRect = new Rect(amountRect.xMax + 2f, rowRect.y, 24f, 24f);
            Rect settlementRect = new Rect(plusRect.xMax + 6f, rowRect.y, rowRect.xMax - plusRect.xMax - 6f, rowRect.height);

            TextAnchor prevAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, row.thingDef.LabelCap);
            Widgets.Label(infoRect, "SettlementServices.Label.CraftingMaterialRequiredCarried".Translate(row.required, row.carried));
            Widgets.Label(settlementRect, row.blocking
                ? "SettlementServices.Label.CraftingMaterialAdditionalNeeded".Translate()
                : "SettlementServices.Label.CraftingMaterialSettlementSupplies".Translate(row.settlementSupplied));
            Text.Anchor = prevAnchor;

            if (Widgets.ButtonText(minusRect, "-") && row.playerSupplied > 0)
                CraftingMaterialPreviewBuilder.SetPlayerSupplied(session, row.thingDef, row.playerSupplied - 1);

            string bufferKey = row.thingDef.defName;
            craftingMaterialBuffers.TryGetValue(bufferKey, out string buffer);
            if (buffer == null || !int.TryParse(buffer, out int bufferedValue) || bufferedValue != row.playerSupplied)
                buffer = row.playerSupplied.ToString();
            int fieldValue = row.playerSupplied;
            Widgets.TextFieldNumeric(amountRect, ref fieldValue, ref buffer, 0, max);
            craftingMaterialBuffers[bufferKey] = buffer;
            if (fieldValue != row.playerSupplied)
                CraftingMaterialPreviewBuilder.SetPlayerSupplied(session, row.thingDef, fieldValue);

            if (Widgets.ButtonText(plusRect, "+") && row.playerSupplied < max)
                CraftingMaterialPreviewBuilder.SetPlayerSupplied(session, row.thingDef, row.playerSupplied + 1);

            listing.Gap(4f);
        }

        private void DrawStockInputSection(Listing_Standard listing, List<StockInputRow> stockRows)
        {
            listing.Label("SettlementServices.Label.SuppliedInputs".Translate());
            bool changed = false;
            foreach (StockInputRow row in stockRows)
            {
                int max = Math.Min(row.availableInCaravan, row.requirement.amount);
                Rect rowRect = listing.GetRect(24f);
                Rect labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width - 96f, rowRect.height);
                Rect minusRect = new Rect(rowRect.xMax - 90f, rowRect.y, 24f, 24f);
                Rect amountRect = new Rect(rowRect.xMax - 60f, rowRect.y, 36f, 24f);
                Rect plusRect = new Rect(rowRect.xMax - 24f, rowRect.y, 24f, 24f);

                Widgets.Label(labelRect, $"{row.thingDef.LabelCap} (0-{max})");
                if (Widgets.ButtonText(minusRect, "-") && row.suppliedAmount > 0) { row.suppliedAmount--; changed = true; }
                TextAnchor prevAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(amountRect, row.suppliedAmount.ToString());
                Text.Anchor = prevAnchor;
                if (Widgets.ButtonText(plusRect, "+") && row.suppliedAmount < max) { row.suppliedAmount++; changed = true; }

                listing.Gap(4f);
            }
            if (changed) ServiceStockInputPicker.ApplyTo(session, stockRows);
        }

        private void DrawTierSection(Listing_Standard listing)
        {
            listing.Label((session.def.priorityTierHeaderKey ?? "SettlementServices.Label.PriorityTier").Translate());
            if (listing.RadioButton("SettlementServices.Label.BaselineTier".Translate(), session.selectedTierKey == null))
                SetSelectedTier(null);
            foreach (ServicePriorityTierDef tier in session.def.priorityTiers)
            {
                bool goodwillMet = tier.minimumGoodwill == int.MinValue
                    || (session.settlement?.Faction?.PlayerGoodwill ?? 0) >= tier.minimumGoodwill;
                if (listing.RadioButton(tier.label, session.selectedTierKey == tier.key, 0f, null, null, !goodwillMet) && goodwillMet)
                    SetSelectedTier(tier.key);
                if (!goodwillMet)
                    listing.SubLabel("SettlementServices.Label.PriorityTierMinimumGoodwill".Translate(tier.minimumGoodwill), 1f);
            }
        }

        private void SetSelectedTier(string tierKey)
        {
            if (session.selectedTierKey == tierKey) return;
            session.selectedTierKey = tierKey;
            PruneStaleOptionSelections();
        }

        private void PruneStaleOptionSelections()
        {
            if (session.def.Worker.UsesPerTargetOptionSelections)
            {
                for (int i = 0; i < session.targets.Count; i++)
                    PruneOptionSelections(session.targets[i], session.OptionsForTarget(i));
            }
            else
            {
                PruneOptionSelections(session.targets.FirstOrDefault(), session.selectedOptionKeys);
            }
        }

        private void PruneOptionSelections(Thing target, List<string> selectedKeys)
        {
            if (selectedKeys.Count == 0) return;
            var ctx = new SettlementServiceContext(SettlementServicesWorldComponent.Current, session.settlement, null, target, session.caravan, selectedKeys, session.selectedTierKey);
            var validKeys = new HashSet<string>(session.def.Worker.GetDisplayOptions(ctx).Select(o => o.key));
            selectedKeys.RemoveAll(k => !validKeys.Contains(k));
        }

        private void DrawCraftingCrafterSection(Listing_Standard listing)
        {
            if (session.craftingCommissionLines.Count == 0) return;
            if (!CraftingProductionPlanner.TryPlan(session.settlement, session.craftingCommissionLines, session.playerSuppliedInputs, 1, out CraftingProductionPlan referencePlan, out _)) return;

            int maxCrafters = Mathf.Max(1, referencePlan.operations.Sum(o => o.executions));
            session.craftingCrafterCount = Mathf.Clamp(session.craftingCrafterCount, 1, maxCrafters);

            Rect row = listing.GetRect(28f);
            Rect labelRect = new Rect(row.x, row.y, row.width - 116f, row.height);
            Rect minusRect = new Rect(row.xMax - 108f, row.y, 28f, 28f);
            Rect amountRect = new Rect(row.xMax - 72f, row.y, 44f, 28f);
            Rect plusRect = new Rect(row.xMax - 28f, row.y, 28f, 28f);

            Widgets.Label(labelRect, "SettlementServices.Label.CrafterCount".Translate());
            if (Widgets.ButtonText(minusRect, "-") && session.craftingCrafterCount > 1) session.craftingCrafterCount--;

            if (crafterCountBuffer == null || !int.TryParse(crafterCountBuffer, out int bufferedValue) || bufferedValue != session.craftingCrafterCount)
                crafterCountBuffer = session.craftingCrafterCount.ToString();
            Widgets.TextFieldNumeric(amountRect, ref session.craftingCrafterCount, ref crafterCountBuffer, 1, maxCrafters);

            if (Widgets.ButtonText(plusRect, "+") && session.craftingCrafterCount < maxCrafters) session.craftingCrafterCount++;

            listing.Gap(4f);

            if (!CraftingProductionPlanner.TryPlan(session.settlement, session.craftingCommissionLines, session.playerSuppliedInputs, session.craftingCrafterCount, out CraftingProductionPlan staffedPlan, out _)) return;

            int highestAssignedWorkTicks = staffedPlan.assignedCrafterWorkTicks.Count > 0 ? staffedPlan.assignedCrafterWorkTicks.Max() : 0;

            listing.Label("SettlementServices.Label.CraftingAssignedCrafters".Translate(staffedPlan.crafterCount));
            listing.Label("SettlementServices.Label.CraftingLongestWorkload".Translate(highestAssignedWorkTicks.ToStringTicksToPeriod()));
            listing.Label("SettlementServices.Label.CraftingBilledCrafterDays".Translate(staffedPlan.billedCrafterDays));

            if (staffedPlan.crafterCount > 1 && staffedPlan.crafterCount <= MaxDisplayedCrafterWorkloads)
            {
                for (int i = 0; i < staffedPlan.assignedCrafterWorkTicks.Count; i++)
                    listing.Label("SettlementServices.Label.CrafterWorkloadLine".Translate(i + 1, staffedPlan.assignedCrafterWorkTicks[i].ToStringTicksToPeriod()));
            }

            listing.Gap();
        }

        private void DrawOptionsSection(Listing_Standard listing)
        {
            if (session.def.Worker.UsesPerTargetOptionSelections) DrawPerTargetOptionsSection(listing);
            else DrawSharedOptionsSection(listing);
        }

        private void DrawSharedOptionsSection(Listing_Standard listing)
        {
            Thing primaryTarget = session.targets.FirstOrDefault();
            var ctx = new SettlementServiceContext(SettlementServicesWorldComponent.Current, session.settlement, null, primaryTarget, session.caravan, session.selectedOptionKeys, session.selectedTierKey);
            List<ServiceDisplayOption> options = session.def.Worker.GetDisplayOptions(ctx).ToList();
            DrawOptionsForList(listing, options, session.selectedOptionKeys);
        }

        private void DrawPerTargetOptionsSection(Listing_Standard listing)
        {
            if (session.targets.Count == 0)
            {
                listing.Label("SettlementServices.Label.NoTargetsForPerTargetOptions".Translate());
                return;
            }

            bool firstTarget = true;
            for (int i = 0; i < session.targets.Count; i++)
            {
                Thing target = session.targets[i];
                List<string> optionKeys = session.OptionsForTarget(i);
                var ctx = new SettlementServiceContext(SettlementServicesWorldComponent.Current, session.settlement, null, target, session.caravan, optionKeys, session.selectedTierKey);
                List<ServiceDisplayOption> options = session.def.Worker.GetDisplayOptions(ctx).ToList();
                if (options.Count == 0) continue;

                if (!firstTarget) listing.GapLine();
                firstTarget = false;

                listing.Label("SettlementServices.Label.PerTargetOptionsHeading".Translate(target.LabelCap));
                DrawOptionsForList(listing, options, optionKeys);
            }
        }

        private void DrawOptionsForList(Listing_Standard listing, List<ServiceDisplayOption> options, List<string> selectedKeys)
        {
            if (options.Count == 0) return;

            bool firstGroup = true;
            foreach (IGrouping<string, ServiceDisplayOption> group in options.GroupBy(o => o.groupKey))
            {
                if (!firstGroup) listing.GapLine();
                firstGroup = false;

                if (group.Key == null)
                {
                    foreach (ServiceDisplayOption option in group)
                    {
                        bool selected = selectedKeys.Contains(option.key);
                        bool newSelected = selected;
                        listing.CheckboxLabeled(option.label, ref newSelected, option.description);
                        if (newSelected && !selected) selectedKeys.Add(option.key);
                        else if (!newSelected && selected) selectedKeys.Remove(option.key);
                    }
                    continue;
                }

                listing.Label(group.Key.Translate());
                List<ServiceDisplayOption> groupOptions = group.ToList();
                if (groupOptions[0].allowMultipleSelectionInGroup)
                {
                    DrawCheckboxColumn(listing, groupOptions, selectedKeys);
                    continue;
                }

                int columnCount = Mathf.Max(1, groupOptions[0].groupColumnCount);
                if (columnCount == 1) DrawRadioColumn(listing, groupOptions, selectedKeys);
                else DrawRadioGrid(listing, groupOptions, columnCount, selectedKeys);
            }
        }

        private void DrawCheckboxColumn(Listing_Standard listing, List<ServiceDisplayOption> group, List<string> selectedKeys)
        {
            foreach (ServiceDisplayOption option in group)
            {
                bool selected = selectedKeys.Contains(option.key);
                bool newSelected = selected;
                listing.CheckboxLabeled(option.label, ref newSelected, option.description);
                if (newSelected && !selected)
                {
                    if (!option.conflictingOptionKeys.NullOrEmpty())
                        selectedKeys.RemoveAll(k => option.conflictingOptionKeys.Contains(k));
                    selectedKeys.Add(option.key);
                }
                else if (!newSelected && selected)
                {
                    selectedKeys.Remove(option.key);
                }
            }
        }

        private void DrawRadioColumn(Listing_Standard listing, List<ServiceDisplayOption> group, List<string> selectedKeys)
        {
            foreach (ServiceDisplayOption option in group)
            {
                bool selected = selectedKeys.Contains(option.key);
                Rect rowRect = listing.GetRect(Text.LineHeight);
                bool clicked = DrawRadioOption(rowRect, option, selected);
                listing.Gap(listing.verticalSpacing);
                if (clicked && !selected) SelectOnly(group, option, selectedKeys);
            }
        }

        private void DrawRadioGrid(Listing_Standard listing, List<ServiceDisplayOption> group, int columnCount, List<string> selectedKeys)
        {
            int rowCount = Mathf.CeilToInt(group.Count / (float)columnCount);
            float rowHeight = Text.LineHeight + listing.verticalSpacing;
            Rect grid = listing.GetRect(rowCount * rowHeight);
            float columnWidth = (grid.width - Listing.ColumnSpacing * (columnCount - 1)) / columnCount;

            for (int i = 0; i < group.Count; i++)
            {
                int column = i / rowCount;
                int row = i % rowCount;
                Rect cellRect = new Rect(grid.x + column * (columnWidth + Listing.ColumnSpacing), grid.y + row * rowHeight, columnWidth, Text.LineHeight);

                ServiceDisplayOption option = group[i];
                bool selected = selectedKeys.Contains(option.key);
                if (DrawRadioOption(cellRect, option, selected) && !selected) SelectOnly(group, option, selectedKeys);
            }
        }

        private void SelectOnly(List<ServiceDisplayOption> group, ServiceDisplayOption option, List<string> selectedKeys)
        {
            foreach (ServiceDisplayOption other in group) selectedKeys.Remove(other.key);
            selectedKeys.Add(option.key);
        }

        private void DrawWorkerSummarySection(Listing_Standard listing)
        {
            List<Thing> targets = session.targets;
            bool showPerTargetHeader = targets.Count > 1;
            bool perTargetOptions = session.def.Worker.UsesPerTargetOptionSelections;
            bool any = false;

            for (int i = 0; i < targets.Count; i++)
            {
                Thing target = targets[i];
                List<string> optionKeys = perTargetOptions ? session.OptionsForTarget(i) : session.selectedOptionKeys;
                var ctx = new SettlementServiceContext(SettlementServicesWorldComponent.Current, session.settlement, null, target, session.caravan, optionKeys, session.selectedTierKey);
                List<string> lines = session.def.Worker.GetDisplaySummaryLines(ctx).ToList();
                if (lines.Count == 0) continue;

                listing.Gap();
                any = true;
                if (showPerTargetHeader) listing.Label(target.LabelCap);
                foreach (string line in lines) listing.Label(line);
            }

            if (!any && targets.Count == 0)
            {
                var ctx = new SettlementServiceContext(SettlementServicesWorldComponent.Current, session.settlement, null, null, session.caravan, session.selectedOptionKeys, session.selectedTierKey);
                List<string> lines = session.def.Worker.GetDisplaySummaryLines(ctx).ToList();
                if (lines.Count > 0)
                {
                    listing.Gap();
                    foreach (string line in lines) listing.Label(line);
                }
            }
        }

        private const float OptionIconSize = 24f;
        private const float OptionIconGap = 4f;

        private static bool DrawRadioOption(Rect rowRect, ServiceDisplayOption option, bool selected)
        {
            Texture2D icon = ServiceUITextures.Resolve(option.iconTexPath);
            return icon == null ? Widgets.RadioButtonLabeled(rowRect, option.label, selected) : IconRadioButton(rowRect, option.label, icon, option.iconColor, selected);
        }

        private static bool IconRadioButton(Rect rowRect, string label, Texture2D icon, Color? iconColor, bool active)
        {
            Rect iconRect = new Rect(rowRect.x, rowRect.y + (rowRect.height - OptionIconSize) / 2f, OptionIconSize, OptionIconSize);
            Rect labelRect = new Rect(rowRect.x + OptionIconSize + OptionIconGap, rowRect.y, rowRect.width - OptionIconSize - OptionIconGap, rowRect.height);

            Color previousColor = GUI.color;
            if (iconColor.HasValue) GUI.color = iconColor.Value;
            Widgets.DrawTextureFitted(iconRect, icon, 1f);
            GUI.color = previousColor;

            bool clickedLabel = Widgets.RadioButtonLabeled(labelRect, label, active);
            bool clickedIcon = Widgets.ButtonInvisible(iconRect) && !clickedLabel;
            if (clickedIcon && !active) SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            return clickedLabel || clickedIcon;
        }

        private void DrawQuote(Listing_Standard listing, SettlementServiceQuote quote)
        {
            if (!quote.IsValid)
            {
                Color prevColor = GUI.color;
                GUI.color = ColorLibrary.RedReadable;
                foreach (string errorKey in quote.validationErrors) listing.Label(ServiceErrorFormatting.Format(errorKey, session.def, session.settlement));
                GUI.color = prevColor;
                return;
            }

            bool isBatchedFuelTopUp = quote.unitCount > 1 && session.def.defName == "SettlementService_VehicleFuelTopUp";
            foreach (ServiceLineItem lineItem in quote.lineItems)
            {
                if (isBatchedFuelTopUp && lineItem.labelKey == "SettlementServices.LineItem.VehicleFuelTopUp")
                    listing.Label("SettlementServices.Label.VehicleFuelBatchSurcharge".Translate(lineItem.amount));
                else
                    listing.Label($"{lineItem.DisplayLabel}: {lineItem.amount}");
            }

            if (quote.unitCount > 1)
            {
                bool perUnitCostsDiffer = quote.perUnitQuotes.Count > 1
                    && quote.perUnitQuotes.Select(u => u.totalCost).Distinct().Count() > 1;
                if (perUnitCostsDiffer)
                {
                    foreach (ServiceUnitQuote unitQuote in quote.perUnitQuotes)
                    {
                        string label = unitQuote.targetLabel ?? (unitQuote.unitIndex + 1).ToString();
                        listing.Label("SettlementServices.Label.PerTargetPrice".Translate(label, unitQuote.totalCost));
                    }
                }
                else
                {
                    listing.Label("SettlementServices.Label.UnitPrice".Translate(quote.unitCount, quote.unitPrice));
                }
            }

            listing.Label("SettlementServices.Label.TotalCost".Translate(quote.totalCost));
            listing.Label("SettlementServices.Label.ExpectedDuration".Translate(quote.expectedDurationTicks.ToStringTicksToPeriod()));
        }

        private void DrawBottomButtons(Rect inRect, SettlementServiceQuote quote)
        {
            bool targetOk = session.def.targetRule == ServiceTargetRule.None || session.targets.Count > 0;
            Rect confirmRect = new Rect(inRect.width - 150f, inRect.height - 35f, 140f, 35f);
            Rect cancelRect = new Rect(inRect.width - 300f, inRect.height - 35f, 140f, 35f);

            if (Widgets.ButtonText(cancelRect, "CancelButton".Translate())) Close();

            bool prevEnabled = GUI.enabled;
            GUI.enabled = quote.IsValid && targetOk && RequiredOptionsSatisfied();
            if (Widgets.ButtonText(confirmRect, "SettlementServices.Button.Confirm".Translate())) Confirm(quote);
            GUI.enabled = prevEnabled;
        }

        private bool RequiredOptionsSatisfied()
        {
            if (session.def.Worker.UsesPerTargetOptionSelections)
            {
                return session.targets.Count > 0
                    && Enumerable.Range(0, session.targets.Count).All(i => TargetOptionsSatisfied(session.targets[i], session.OptionsForTarget(i)));
            }

            Thing primaryTarget = session.targets.FirstOrDefault();
            return TargetOptionsSatisfied(primaryTarget, session.selectedOptionKeys);
        }

        private bool TargetOptionsSatisfied(Thing target, List<string> optionKeys)
        {
            var ctx = new SettlementServiceContext(SettlementServicesWorldComponent.Current, session.settlement, null, target, session.caravan, optionKeys, session.selectedTierKey);
            List<ServiceDisplayOption> options = session.def.Worker.GetDisplayOptions(ctx).ToList();
            List<ServiceDisplayOption> requiredOptions = options.Where(o => !o.isOptional).ToList();
            if (requiredOptions.Count == 0) return true;

            bool anySelected = requiredOptions.Any(o => optionKeys.Contains(o.key));
            bool groupsSatisfied = requiredOptions.Where(o => o.groupKey != null).GroupBy(o => o.groupKey)
                .All(g => g.Any(o => optionKeys.Contains(o.key)));
            return anySelected && groupsSatisfied;
        }

        private void Confirm(SettlementServiceQuote quote) => ServiceConfirmationFlow.TryConfirm(session, quote, this);
    }
}

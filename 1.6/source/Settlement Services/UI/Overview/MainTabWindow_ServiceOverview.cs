using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Framework;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.UI.Overview
{
    public class MainTabWindow_ServiceOverview : MainTabWindow
    {
        private enum ServiceOverviewViewMode { Jobs, KnownServices }

        private Vector2 scrollPosition;
        private Vector2 knownServicesScrollPosition;
        private ServiceOverviewViewMode viewMode = ServiceOverviewViewMode.Jobs;
        private ServiceOverviewGrouping grouping = ServiceOverviewGrouping.Settlement;
        private int? filterSettlementWorldObjectId;
        private ServiceCategoryDef filterCategory;
        private ServiceTargetRule? filterTargetRule;
        private ServiceJobStatus? filterStatus;

        public override Vector2 RequestedTabSize => new Vector2(920f, 520f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;

            Rect viewToggleRect = new Rect(0f, 0f, 150f, 30f);
            if (Widgets.ButtonText(viewToggleRect, ("SettlementServices.Label.ViewMode." + viewMode).Translate()))
                viewMode = viewMode == ServiceOverviewViewMode.Jobs ? ServiceOverviewViewMode.KnownServices : ServiceOverviewViewMode.Jobs;

            Rect remainder = new Rect(0f, viewToggleRect.yMax + 4f, inRect.width, inRect.height - viewToggleRect.height - 4f);
            if (viewMode == ServiceOverviewViewMode.Jobs) DrawJobsView(remainder);
            else DrawKnownServicesView(remainder);
        }

        private void DrawJobsView(Rect inRect)
        {
            List<ServiceOverviewEntry> allEntries = ServiceOverviewQueryService.BuildEntries();
            List<ServiceOverviewEntry> filtered = allEntries.Where(Matches).ToList();

            Rect toolbarRect = new Rect(inRect.x, inRect.y, inRect.width, 30f);
            DrawToolbar(toolbarRect, allEntries);

            Rect listRect = new Rect(inRect.x, toolbarRect.yMax + 6f, inRect.width, inRect.height - toolbarRect.height - 6f);
            if (filtered.Count == 0)
            {
                Widgets.NoneLabelCenteredVertically(listRect, "SettlementServices.Label.NoServicesToShow".Translate());
                return;
            }
            DrawList(listRect, filtered);
        }

        private const float KnownServicesRowMinHeight = 60f;
        private const float KnownServicesJumpButtonWidth = 90f;
        private const float KnownServicesJumpButtonGutter = 6f;

        private void DrawKnownServicesView(Rect inRect)
        {
            List<ServiceDiscoveryOverviewEntry> entries = ServiceOverviewQueryService.BuildDiscoveryEntries();
            if (entries.Count == 0)
            {
                Widgets.NoneLabelCenteredVertically(inRect, "SettlementServices.Label.NoKnownServicesToShow".Translate());
                return;
            }

            float textWidth = inRect.width - 16f - KnownServicesJumpButtonWidth - KnownServicesJumpButtonGutter;
            float[] rowStrides = entries.Select(e => KnownServicesRowStride(e, textWidth)).ToArray();
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, rowStrides.Sum());
            Widgets.BeginScrollView(inRect, ref knownServicesScrollPosition, viewRect);
            float y = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                float stride = rowStrides[i];
                Rect rowRect = new Rect(0f, y, viewRect.width, stride - 2f);
                if (rowRect.y + stride >= knownServicesScrollPosition.y && rowRect.y <= knownServicesScrollPosition.y + inRect.height)
                    DrawKnownServicesRow(rowRect, entries[i]);
                y += stride;
            }
            Widgets.EndScrollView();
        }

        private static float KnownServicesRowStride(ServiceDiscoveryOverviewEntry entry, float textWidth)
        {
            float height = Text.CalcHeight(entry.settlementLabel, textWidth);
            if (entry.inPersonLabel != null)
                height += Text.CalcHeight("SettlementServices.Label.KnownServicesInPerson".Translate(entry.inPersonLabel), textWidth);
            if (entry.remoteLabel != null)
                height += Text.CalcHeight("SettlementServices.Label.KnownServicesRemote".Translate(entry.remoteLabel), textWidth);
            return Mathf.Max(height + 2f, KnownServicesRowMinHeight);
        }

        private void DrawKnownServicesRow(Rect rect, ServiceDiscoveryOverviewEntry entry)
        {
            Widgets.DrawHighlightIfMouseover(rect);

            Rect jumpRect = new Rect(rect.x, rect.y, KnownServicesJumpButtonWidth, rect.height);
            if (Widgets.ButtonText(jumpRect, "SettlementServices.Button.Jump".Translate()))
                JumpToSettlement(entry.settlement);

            Rect textRect = new Rect(jumpRect.xMax + KnownServicesJumpButtonGutter, rect.y, rect.width - jumpRect.width - KnownServicesJumpButtonGutter, rect.height);
            float y = textRect.y;
            float settlementHeight = Text.CalcHeight(entry.settlementLabel, textRect.width);
            Widgets.Label(new Rect(textRect.x, y, textRect.width, settlementHeight), entry.settlementLabel);
            y += settlementHeight;

            Color prevColor = GUI.color;
            GUI.color = Color.gray;
            if (entry.inPersonLabel != null)
            {
                string inPersonText = "SettlementServices.Label.KnownServicesInPerson".Translate(entry.inPersonLabel);
                float inPersonHeight = Text.CalcHeight(inPersonText, textRect.width);
                Widgets.Label(new Rect(textRect.x, y, textRect.width, inPersonHeight), inPersonText);
                y += inPersonHeight;
            }
            if (entry.remoteLabel != null)
            {
                string remoteText = "SettlementServices.Label.KnownServicesRemote".Translate(entry.remoteLabel);
                float remoteHeight = Text.CalcHeight(remoteText, textRect.width);
                Widgets.Label(new Rect(textRect.x, y, textRect.width, remoteHeight), remoteText);
                y += remoteHeight;
            }
            GUI.color = prevColor;
        }

        private bool Matches(ServiceOverviewEntry entry)
        {
            if (filterSettlementWorldObjectId != null && entry.job.settlementWorldObjectId != filterSettlementWorldObjectId.Value) return false;
            if (filterCategory != null && entry.def?.category != filterCategory) return false;
            if (filterTargetRule != null && (entry.def?.targetRule ?? ServiceTargetRule.None) != filterTargetRule.Value) return false;
            if (filterStatus != null && entry.job.status != filterStatus.Value) return false;
            return true;
        }


        private void DrawToolbar(Rect rect, List<ServiceOverviewEntry> allEntries)
        {
            float buttonWidth = rect.width / 5f;
            Rect groupRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect settlementRect = new Rect(groupRect.xMax, rect.y, buttonWidth, rect.height);
            Rect categoryRect = new Rect(settlementRect.xMax, rect.y, buttonWidth, rect.height);
            Rect targetRect = new Rect(categoryRect.xMax, rect.y, buttonWidth, rect.height);
            Rect statusRect = new Rect(targetRect.xMax, rect.y, buttonWidth, rect.height);

            string all = "SettlementServices.Label.All".Translate();

            string groupingLabel = ("SettlementServices.Label.GroupingOption." + grouping).Translate();
            if (Widgets.ButtonText(groupRect, "SettlementServices.Label.GroupBy".Translate(groupingLabel)))
                OpenGroupingMenu();

            string settlementLabel = filterSettlementWorldObjectId == null
                ? all
                : (WorldObjectLookup.ResolveSettlement(filterSettlementWorldObjectId.Value)?.LabelCap ?? "SettlementServices.Label.UnknownSettlement".Translate());
            if (Widgets.ButtonText(settlementRect, "SettlementServices.Label.FilterSettlement".Translate(settlementLabel)))
                OpenSettlementFilterMenu(allEntries);

            string categoryLabel = filterCategory?.LabelCap ?? all;
            if (Widgets.ButtonText(categoryRect, "SettlementServices.Label.FilterCategory".Translate(categoryLabel)))
                OpenCategoryFilterMenu(allEntries);

            string targetLabel = filterTargetRule == null ? all : ServiceOverviewFormatting.TargetRuleLabel(filterTargetRule.Value);
            if (Widgets.ButtonText(targetRect, "SettlementServices.Label.FilterTarget".Translate(targetLabel)))
                OpenTargetFilterMenu();

            string statusLabel = filterStatus == null ? all : ServiceOverviewFormatting.StatusLabel(filterStatus.Value);
            if (Widgets.ButtonText(statusRect, "SettlementServices.Label.FilterStatus".Translate(statusLabel)))
                OpenStatusFilterMenu();
        }

        private void OpenGroupingMenu()
        {
            var options = new List<FloatMenuOption>();
            foreach (ServiceOverviewGrouping g in Enum.GetValues(typeof(ServiceOverviewGrouping)))
            {
                ServiceOverviewGrouping captured = g;
                options.Add(new FloatMenuOption(("SettlementServices.Label.GroupingOption." + g).Translate(), () => grouping = captured));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenSettlementFilterMenu(List<ServiceOverviewEntry> allEntries)
        {
            var options = new List<FloatMenuOption> { new FloatMenuOption("SettlementServices.Label.All".Translate(), () => filterSettlementWorldObjectId = null) };
            var distinctSettlements = allEntries
                .GroupBy(e => e.job.settlementWorldObjectId)
                .Select(g => new { id = g.Key, label = g.First().settlementLabel })
                .OrderBy(s => s.label);
            foreach (var s in distinctSettlements)
            {
                int id = s.id;
                options.Add(new FloatMenuOption(s.label, () => filterSettlementWorldObjectId = id));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenCategoryFilterMenu(List<ServiceOverviewEntry> allEntries)
        {
            var options = new List<FloatMenuOption> { new FloatMenuOption("SettlementServices.Label.All".Translate(), () => filterCategory = null) };
            var distinctCategories = allEntries
                .Where(e => e.def?.category != null)
                .Select(e => e.def.category)
                .Distinct()
                .OrderBy(c => (string)c.LabelCap);
            foreach (ServiceCategoryDef category in distinctCategories)
            {
                ServiceCategoryDef captured = category;
                options.Add(new FloatMenuOption(category.LabelCap, () => filterCategory = captured));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenTargetFilterMenu()
        {
            var options = new List<FloatMenuOption> { new FloatMenuOption("SettlementServices.Label.All".Translate(), () => filterTargetRule = null) };
            foreach (ServiceTargetRule rule in Enum.GetValues(typeof(ServiceTargetRule)))
            {
                ServiceTargetRule captured = rule;
                options.Add(new FloatMenuOption(ServiceOverviewFormatting.TargetRuleLabel(rule), () => filterTargetRule = captured));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenStatusFilterMenu()
        {
            var options = new List<FloatMenuOption> { new FloatMenuOption("SettlementServices.Label.All".Translate(), () => filterStatus = null) };
            foreach (ServiceJobStatus status in Enum.GetValues(typeof(ServiceJobStatus)))
            {
                ServiceJobStatus captured = status;
                options.Add(new FloatMenuOption(ServiceOverviewFormatting.StatusLabel(status), () => filterStatus = captured));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }


        private void DrawList(Rect rect, List<ServiceOverviewEntry> entries)
        {
            const float rowHeight = 44f;
            const float headerHeight = 24f;
            List<IGrouping<string, ServiceOverviewEntry>> groups = ServiceOverviewQueryService.Grouped(entries, grouping);
            float viewHeight = groups.Sum(g => (grouping == ServiceOverviewGrouping.None ? 0f : headerHeight) + g.Count() * rowHeight);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, viewHeight);

            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            float y = 0f;
            foreach (IGrouping<string, ServiceOverviewEntry> group in groups)
            {
                if (grouping != ServiceOverviewGrouping.None)
                    Widgets.ListSeparator(ref y, viewRect.width, group.Key);

                foreach (ServiceOverviewEntry entry in group)
                {
                    Rect rowRect = new Rect(0f, y, viewRect.width, rowHeight - 2f);
                    if (rowRect.y + rowHeight >= scrollPosition.y && rowRect.y <= scrollPosition.y + rect.height)
                        DrawRow(rowRect, entry);
                    y += rowHeight;
                }
            }
            Widgets.EndScrollView();
        }

        private const float DebugCompleteButtonWidth = 150f;
        private const float LeaveEarlyButtonWidth = 110f;

        private void DrawRow(Rect rect, ServiceOverviewEntry entry)
        {
            Widgets.DrawHighlightIfMouseover(rect);

            Rect jumpRect = new Rect(rect.x, rect.y, 90f, rect.height);
            if (Widgets.ButtonText(jumpRect, "SettlementServices.Button.Jump".Translate()))
                JumpToSettlement(entry.settlement);

            bool canDebugComplete = Prefs.DevMode && entry.job.status == ServiceJobStatus.Active;
            bool canLeaveEarly = entry.job.status == ServiceJobStatus.Active && entry.def != null
                && entry.def.Worker.CanLeaveEarly(new ServiceJobContext(SettlementServicesWorldComponent.Current, entry.job));

            float textWidth = rect.width - jumpRect.width - 6f;
            if (canDebugComplete) textWidth -= DebugCompleteButtonWidth + 6f;
            if (canLeaveEarly) textWidth -= LeaveEarlyButtonWidth + 6f;
            Rect textRect = new Rect(jumpRect.xMax + 6f, rect.y, textWidth, rect.height);
            string line1 = $"{entry.settlementLabel} - {entry.ServiceLabel} ({entry.TargetLabel})";
            string line2 = $"{ServiceOverviewFormatting.StatusLabel(entry.job.status)}: {ServiceOverviewFormatting.ExpectedCompletionLabel(entry.job)}";

            Widgets.Label(new Rect(textRect.x, textRect.y, textRect.width, textRect.height / 2f), line1);
            Color prevColor = GUI.color;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(textRect.x, textRect.y + textRect.height / 2f, textRect.width, textRect.height / 2f), line2);
            GUI.color = prevColor;

            float buttonsXMax = rect.xMax;

            if (canDebugComplete)
            {
                Rect debugCompleteRect = new Rect(buttonsXMax - DebugCompleteButtonWidth, rect.y, DebugCompleteButtonWidth, rect.height);
                buttonsXMax -= DebugCompleteButtonWidth + 6f;
                TooltipHandler.TipRegion(debugCompleteRect, "DEV: Immediately complete this active service.");
                if (Widgets.ButtonText(debugCompleteRect, "DEV: Complete now"))
                {
                    if (!SettlementServiceJobScheduler.TryCompleteActiveJobNow(entry.job.jobId))
                        Messages.Message("DEV: Job is no longer eligible to be force-completed.", MessageTypeDefOf.RejectInput, historical: false);
                }
            }

            if (canLeaveEarly)
            {
                Rect leaveEarlyRect = new Rect(buttonsXMax - LeaveEarlyButtonWidth, rect.y, LeaveEarlyButtonWidth, rect.height);
                buttonsXMax -= LeaveEarlyButtonWidth + 6f;
                TooltipHandler.TipRegion(leaveEarlyRect, "SettlementServices.Button.LeaveEarlyDesc".Translate());
                if (Widgets.ButtonText(leaveEarlyRect, "SettlementServices.Button.LeaveEarly".Translate()))
                    ConfirmLeaveEarly(entry);
            }
        }

        private static void ConfirmLeaveEarly(ServiceOverviewEntry entry)
        {
            int jobId = entry.job.jobId;
            TaggedString body = "SettlementServices.Label.LeaveEarlyConfirmBody".Translate(entry.TargetLabel, entry.settlementLabel);
            string title = "SettlementServices.Label.LeaveEarlyConfirmTitle".Translate(entry.ServiceLabel);

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(body, () =>
            {
                if (!SettlementServiceOrchestrator.TryLeaveJobEarly(jobId))
                    Messages.Message("SettlementServices.Message.LeaveEarlyNoLongerEligible".Translate(), MessageTypeDefOf.RejectInput, historical: false);
            }, destructive: true, title: title));
        }

        private static void JumpToSettlement(Settlement settlement)
        {
            if (settlement == null)
            {
                Messages.Message("SettlementServices.Error.SettlementNoLongerExists".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            CameraJumper.TryJumpAndSelect(settlement);
        }
    }
}

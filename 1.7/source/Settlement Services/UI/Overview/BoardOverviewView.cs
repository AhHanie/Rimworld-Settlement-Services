using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Board;
using Settlement_Services.UI.JobBoard;

namespace Settlement_Services.UI.Overview
{
    internal sealed class BoardOverviewView
    {
        private enum Grouping
        {
            Settlement,
            Status,
            None
        }

        private sealed class Entry
        {
            public BoardJobRecord job;
            public Settlement settlement;
            public string settlementLabel;
        }

        private const float RowHeight = 44f;
        private const float HeaderHeight = 24f;
        private const float JumpButtonWidth = 90f;
        private const float DebugCompleteButtonWidth = 150f;
        private const float CancelButtonWidth = 110f;

        private Vector2 scroll;
        private Grouping grouping = Grouping.Settlement;
        private int? filterSettlementWorldObjectId;
        private BoardJobStatus? filterStatus;

        internal void Draw(Rect rect)
        {
            List<Entry> all = BuildEntries();
            List<Entry> filtered = all.Where(Matches).ToList();

            Rect toolbarRect = new Rect(rect.x, rect.y, rect.width, 30f);
            DrawToolbar(toolbarRect, all);

            Rect listRect = new Rect(rect.x, toolbarRect.yMax + 6f, rect.width, rect.height - toolbarRect.height - 6f);
            if (filtered.Count == 0)
            {
                Widgets.NoneLabelCenteredVertically(listRect, "SettlementServices.JobBoard.Overview.Empty".Translate());
                return;
            }
            DrawList(listRect, filtered);
        }

        private static List<Entry> BuildEntries()
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null) return new List<Entry>();

            return domain.AllBoardJobs
                .Where(j => j.status != BoardJobStatus.Available && j.status != BoardJobStatus.Expired)
                .OrderBy(j => j.HoldsWorkers ? 0 : 1)
                .ThenBy(j => j.HoldsWorkers ? j.completionTick : -j.statusChangedTick)
                .Select(j =>
                {
                    Settlement settlement = WorldObjectLookup.ResolveSettlement(j.settlementWorldObjectId);
                    return new Entry
                    {
                        job = j,
                        settlement = settlement,
                        settlementLabel = settlement?.LabelCap ?? "SettlementServices.Label.UnknownSettlement".Translate(),
                    };
                })
                .ToList();
        }

        private bool Matches(Entry entry)
        {
            if (filterSettlementWorldObjectId != null && entry.job.settlementWorldObjectId != filterSettlementWorldObjectId.Value) return false;
            if (filterStatus != null && entry.job.status != filterStatus.Value) return false;
            return true;
        }

        private static string StatusLabel(BoardJobStatus status) =>
            ("SettlementServices.JobBoard.Overview.Status." + status).Translate();

        private void DrawToolbar(Rect rect, List<Entry> all)
        {
            float buttonWidth = rect.width / 5f;
            Rect groupRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect settlementRect = new Rect(groupRect.xMax, rect.y, buttonWidth, rect.height);
            Rect statusRect = new Rect(settlementRect.xMax, rect.y, buttonWidth, rect.height);

            string allLabel = "SettlementServices.Label.All".Translate();

            string groupingLabel = ("SettlementServices.Label.GroupingOption." + grouping).Translate();
            if (Widgets.ButtonText(groupRect, "SettlementServices.Label.GroupBy".Translate(groupingLabel)))
            {
                var options = new List<FloatMenuOption>();
                foreach (Grouping g in Enum.GetValues(typeof(Grouping)))
                {
                    Grouping captured = g;
                    options.Add(new FloatMenuOption(("SettlementServices.Label.GroupingOption." + g).Translate(), () => grouping = captured));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            string settlementLabel = filterSettlementWorldObjectId == null
                ? allLabel
                : (WorldObjectLookup.ResolveSettlement(filterSettlementWorldObjectId.Value)?.LabelCap ?? "SettlementServices.Label.UnknownSettlement".Translate());
            if (Widgets.ButtonText(settlementRect, "SettlementServices.Label.FilterSettlement".Translate(settlementLabel)))
            {
                var options = new List<FloatMenuOption> { new FloatMenuOption(allLabel, () => filterSettlementWorldObjectId = null) };
                foreach (var s in all.GroupBy(e => e.job.settlementWorldObjectId).Select(g => new { id = g.Key, label = g.First().settlementLabel }).OrderBy(s => s.label))
                {
                    int id = s.id;
                    options.Add(new FloatMenuOption(s.label, () => filterSettlementWorldObjectId = id));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            string statusLabel = filterStatus == null ? allLabel : StatusLabel(filterStatus.Value);
            if (Widgets.ButtonText(statusRect, "SettlementServices.Label.FilterStatus".Translate(statusLabel)))
            {
                var options = new List<FloatMenuOption> { new FloatMenuOption(allLabel, () => filterStatus = null) };
                foreach (BoardJobStatus status in new[] { BoardJobStatus.Active, BoardJobStatus.ReturnPending, BoardJobStatus.Completed, BoardJobStatus.Failed, BoardJobStatus.Cancelled })
                {
                    BoardJobStatus captured = status;
                    options.Add(new FloatMenuOption(StatusLabel(status), () => filterStatus = captured));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void DrawList(Rect rect, List<Entry> entries)
        {
            List<IGrouping<string, Entry>> groups = Group(entries);
            float viewHeight = groups.Sum(g => (grouping == Grouping.None ? 0f : HeaderHeight) + g.Count() * RowHeight);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, viewHeight);

            Widgets.BeginScrollView(rect, ref scroll, viewRect);
            float y = 0f;
            foreach (IGrouping<string, Entry> group in groups)
            {
                if (grouping != Grouping.None) Widgets.ListSeparator(ref y, viewRect.width, group.Key);

                foreach (Entry entry in group)
                {
                    Rect rowRect = new Rect(0f, y, viewRect.width, RowHeight - 2f);
                    if (rowRect.y + RowHeight >= scroll.y && rowRect.y <= scroll.y + rect.height) DrawRow(rowRect, entry);
                    y += RowHeight;
                }
            }
            Widgets.EndScrollView();
        }

        private List<IGrouping<string, Entry>> Group(List<Entry> entries)
        {
            switch (grouping)
            {
                case Grouping.Settlement:
                    return entries.GroupBy(e => e.settlementLabel).OrderBy(g => g.Key).ToList();
                case Grouping.Status:
                    return entries.GroupBy(e => StatusLabel(e.job.status)).OrderBy(g => g.Key).ToList();
                default:
                    return entries.GroupBy(e => string.Empty).ToList();
            }
        }

        private void DrawRow(Rect rect, Entry entry)
        {
            BoardJobRecord job = entry.job;
            Widgets.DrawHighlightIfMouseover(rect);

            Rect jumpRect = new Rect(rect.x, rect.y, JumpButtonWidth, rect.height);
            if (Widgets.ButtonText(jumpRect, "SettlementServices.Button.Jump".Translate())) JumpToSettlement(entry.settlement);

            bool canDebugComplete = Prefs.DevMode && job.status == BoardJobStatus.Active;
            bool canCancel = job.status == BoardJobStatus.Active;

            float textWidth = rect.width - jumpRect.width - 6f;
            if (canDebugComplete) textWidth -= DebugCompleteButtonWidth + 6f;
            if (canCancel) textWidth -= CancelButtonWidth + 6f;
            Rect textRect = new Rect(jumpRect.xMax + 6f, rect.y, textWidth, rect.height);

            string line1 = "SettlementServices.JobBoard.Overview.Line1".Translate(entry.settlementLabel, job.title, job.partySize);
            string line2 = job.status == BoardJobStatus.Completed || job.status == BoardJobStatus.Active || job.status == BoardJobStatus.ReturnPending
                ? "SettlementServices.JobBoard.Overview.Line2".Translate(BoardJobFormatting.StatusLabel(job), BoardJobFormatting.RewardValue(job)).Resolve()
                : BoardJobFormatting.StatusLabel(job) + (job.failureKey.NullOrEmpty() ? string.Empty : " - " + BoardJobNotifier.FailureText(job));

            Widgets.Label(new Rect(textRect.x, textRect.y, textRect.width, textRect.height / 2f), line1);
            Color previous = GUI.color;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(textRect.x, textRect.y + textRect.height / 2f, textRect.width, textRect.height / 2f), line2);
            GUI.color = previous;

            float buttonsXMax = rect.xMax;

            if (canDebugComplete)
            {
                Rect debugRect = new Rect(buttonsXMax - DebugCompleteButtonWidth, rect.y, DebugCompleteButtonWidth, rect.height);
                buttonsXMax -= DebugCompleteButtonWidth + 6f;
                TooltipHandler.TipRegion(debugRect, "DEV: Immediately complete this active job board job.");
                if (Widgets.ButtonText(debugRect, "DEV: Complete now") && !BoardJobCoordinator.TryForceComplete(SettlementServicesWorldComponent.Current, job.boardJobId))
                    Messages.Message("DEV: Job is no longer eligible to be force-completed.", MessageTypeDefOf.RejectInput, historical: false);
            }

            if (canCancel)
            {
                Rect cancelRect = new Rect(buttonsXMax - CancelButtonWidth, rect.y, CancelButtonWidth, rect.height);
                if (Widgets.ButtonText(cancelRect, "SettlementServices.JobBoard.CancelJob".Translate())) ConfirmCancel(job);
            }
        }

        private static void ConfirmCancel(BoardJobRecord job)
        {
            int boardJobId = job.boardJobId;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "SettlementServices.JobBoard.CancelConfirm".Translate(job.title),
                () => BoardJobCoordinator.TryCancelActive(SettlementServicesWorldComponent.Current, SettlementServicesWorldComponent.Current?.GetBoardJob(boardJobId)),
                destructive: true,
                title: job.title));
        }

        private static void JumpToSettlement(Settlement settlement) => MainTabWindow_ServiceOverview.JumpToSettlement(settlement);
    }
}

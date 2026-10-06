using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;

namespace Settlement_Services.UI.JobBoard
{
    internal sealed class JobBoardTabPresenter
    {
        private enum Filter
        {
            Available,
            Active,
            Recent
        }

        private const float FilterHeight = 30f;
        private const float RowHeight = 78f;
        private const int RecentLimit = 10;

        private readonly ServiceRequestSession session;
        private readonly BoardJobDetailPresenter detail;

        private Filter filter = Filter.Available;
        private Vector2 listScroll;
        private int selectedJobId = -1;

        internal JobBoardTabPresenter(ServiceRequestSession session, Action closeWindow)
        {
            this.session = session;
            detail = new BoardJobDetailPresenter(session, closeWindow);
        }

        internal void Draw(Rect rect)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null) return;

            if (selectedJobId >= 0)
            {
                BoardJobRecord selected = domain.GetBoardJob(selectedJobId);
                if (selected == null || selected.settlementWorldObjectId != session.settlement.ID)
                {
                    selectedJobId = -1;
                }
                else
                {
                    if (detail.Draw(rect, selected)) selectedJobId = -1;
                    return;
                }
            }

            DrawFilterBar(new Rect(rect.x, rect.y, rect.width, FilterHeight));
            DrawList(new Rect(rect.x, rect.y + FilterHeight + 6f, rect.width, rect.height - FilterHeight - 6f), domain);
        }

        private void DrawFilterBar(Rect rect)
        {
            float width = 140f;
            float x = rect.x;
            foreach (Filter option in Enum.GetValues(typeof(Filter)))
            {
                Rect buttonRect = new Rect(x, rect.y, width, rect.height);
                if (option == filter) Widgets.DrawOptionBackground(buttonRect, true);
                if (Widgets.ButtonText(buttonRect, ("SettlementServices.JobBoard.Filter." + option).Translate()))
                {
                    filter = option;
                    listScroll = Vector2.zero;
                }
                x += width + 6f;
            }
        }

        private void DrawList(Rect rect, SettlementServicesWorldComponent domain)
        {
            List<BoardJobRecord> jobs = JobsForFilter(domain);
            if (jobs.Count == 0)
            {
                Widgets.NoneLabelCenteredVertically(rect, ("SettlementServices.JobBoard.Empty." + filter).Translate());
                return;
            }

            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, jobs.Count * RowHeight);
            Widgets.BeginScrollView(rect, ref listScroll, viewRect);
            float y = 0f;
            foreach (BoardJobRecord job in jobs)
            {
                Rect rowRect = new Rect(0f, y, viewRect.width, RowHeight - 4f);
                if (rowRect.yMax >= listScroll.y && rowRect.y <= listScroll.y + rect.height) DrawRow(rowRect, job);
                y += RowHeight;
            }
            Widgets.EndScrollView();
        }

        private List<BoardJobRecord> JobsForFilter(SettlementServicesWorldComponent domain)
        {
            List<BoardJobRecord> all = domain.BoardJobsForSettlement(session.settlement.ID);
            switch (filter)
            {
                case Filter.Available:
                    return all.Where(j => j.status == BoardJobStatus.Available).OrderBy(j => j.expiryTick).ToList();
                case Filter.Active:
                    return all.Where(j => j.HoldsWorkers).OrderBy(j => j.completionTick).ToList();
                default:
                    return all
                        .Where(j => j.status == BoardJobStatus.Completed || j.status == BoardJobStatus.Failed || j.status == BoardJobStatus.Cancelled)
                        .OrderByDescending(j => j.statusChangedTick)
                        .Take(RecentLimit)
                        .ToList();
            }
        }

        private void DrawRow(Rect rect, BoardJobRecord job)
        {
            Widgets.DrawOptionBackground(rect, false);
            Widgets.DrawHighlightIfMouseover(rect);

            Rect inner = rect.ContractedBy(6f);

            Rect valueRect = new Rect(inner.xMax - 120f, inner.y, 120f, 24f);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(valueRect, BoardJobFormatting.RewardValue(job));

            Rect titleRect = new Rect(inner.x, inner.y, inner.width - 126f, 24f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(titleRect, job.title.Truncate(titleRect.width));

            Text.Font = GameFont.Tiny;
            string secondary = job.status == BoardJobStatus.Available
                ? "SettlementServices.JobBoard.Row.Offer".Translate(job.partySize, BoardJobFormatting.WorkDuration(job), BoardJobFormatting.ExpiresIn(job)).Resolve()
                : "SettlementServices.JobBoard.Row.Worked".Translate(job.partySize, BoardJobFormatting.StatusLabel(job)).Resolve();
            Widgets.Label(new Rect(inner.x, inner.y + 24f, inner.width, 18f), secondary);

            string roles = BoardJobFormatting.RolesSummary(job);
            Widgets.Label(new Rect(inner.x, inner.y + 42f, inner.width, 18f), roles.Truncate(inner.width));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            if (Widgets.ButtonInvisible(rect)) selectedJobId = job.boardJobId;
        }
    }
}

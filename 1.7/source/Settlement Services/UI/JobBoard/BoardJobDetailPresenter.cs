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

namespace Settlement_Services.UI.JobBoard
{
    internal sealed class BoardJobDetailPresenter
    {
        private const float RowGap = 6f;
        private const float ButtonHeight = 32f;
        private const float RewardIconSize = 36f;

        private readonly ServiceRequestSession session;
        private readonly BoardPawnAssignmentPresenter assignment = new BoardPawnAssignmentPresenter();
        private readonly Action closeWindow;

        private string lastErrorKey;
        private int confirmingCancelJobId = -1;

        internal BoardJobDetailPresenter(ServiceRequestSession session, Action closeWindow)
        {
            this.session = session;
            this.closeWindow = closeWindow;
        }

        internal bool Draw(Rect rect, BoardJobRecord job)
        {
            bool back = false;

            Rect backRect = new Rect(rect.x, rect.y, 90f, 28f);
            if (Widgets.ButtonText(backRect, "SettlementServices.JobBoard.Back".Translate())) back = true;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect titleRect = new Rect(backRect.xMax + 10f, rect.y, rect.width - backRect.width - 10f, 28f);
            Widgets.Label(titleRect, job.title);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            float y = rect.y + 34f;
            y = DrawFlavor(rect, y, job);
            y = DrawSummary(rect, y, job);

            List<Pawn> selected = job.status == BoardJobStatus.Available ? assignment.SelectedFor(job, session.caravan) : new List<Pawn>();
            y = DrawRoles(rect, y, job, selected);

            float bottomHeight = job.status == BoardJobStatus.Available || job.status == BoardJobStatus.Active ? ButtonHeight + 30f : 24f;
            Rect bodyRect = new Rect(rect.x, y + RowGap, rect.width, Mathf.Max(0f, rect.yMax - bottomHeight - y - RowGap));
            Rect bottomRect = new Rect(rect.x, rect.yMax - bottomHeight, rect.width, bottomHeight);

            switch (job.status)
            {
                case BoardJobStatus.Available:
                    assignment.Draw(bodyRect, job, session.caravan);
                    DrawStartBar(bottomRect, job, selected);
                    break;
                case BoardJobStatus.Active:
                    DrawActiveBody(bodyRect, job);
                    DrawCancelBar(bottomRect, job);
                    break;
                default:
                    DrawFinishedBody(bodyRect, job);
                    break;
            }

            return back;
        }

        private float DrawFlavor(Rect rect, float y, BoardJobRecord job)
        {
            if (job.flavor.NullOrEmpty()) return y;

            float height = Text.CalcHeight(job.flavor, rect.width);
            Widgets.Label(new Rect(rect.x, y, rect.width, height), job.flavor);
            return y + height + RowGap;
        }

        private float DrawSummary(Rect rect, float y, BoardJobRecord job)
        {
            float labelWidth = 150f;
            y = DrawKeyValue(rect, y, labelWidth, "SettlementServices.JobBoard.Detail.Workers".Translate(), job.partySize.ToString());
            y = DrawKeyValue(rect, y, labelWidth, "SettlementServices.JobBoard.Detail.Duration".Translate(), BoardJobFormatting.WorkDuration(job));

            if (job.status == BoardJobStatus.Available)
                y = DrawKeyValue(rect, y, labelWidth, "SettlementServices.JobBoard.Detail.Expires".Translate(), BoardJobFormatting.ExpiresIn(job));
            else
                y = DrawKeyValue(rect, y, labelWidth, "SettlementServices.JobBoard.Detail.Status".Translate(), BoardJobFormatting.StatusLabel(job));

            Rect rewardLabel = new Rect(rect.x, y, labelWidth, RewardIconSize);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(rewardLabel, "SettlementServices.JobBoard.Detail.Reward".Translate());

            float x = rewardLabel.xMax;
            if (job.status == BoardJobStatus.Available || (job.status == BoardJobStatus.Active && !job.reward.settled))
            {
                foreach (Thing item in job.reward.items.Where(t => t != null && !t.Destroyed))
                {
                    Rect iconRect = new Rect(x, y, RewardIconSize, RewardIconSize);
                    Widgets.DrawHighlightIfMouseover(iconRect);
                    Widgets.ThingIcon(iconRect, item);
                    if (item.stackCount > 1)
                    {
                        Text.Font = GameFont.Tiny;
                        Text.Anchor = TextAnchor.LowerRight;
                        Widgets.Label(iconRect, item.stackCount.ToStringCached());
                        Text.Font = GameFont.Small;
                    }
                    TooltipHandler.TipRegion(iconRect, item.LabelCap);
                    x += RewardIconSize + 2f;
                    if (x + RewardIconSize > rect.xMax - 140f) break;
                }
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            Rect valueRect = new Rect(x + 6f, y, rect.xMax - x - 6f, RewardIconSize);
            string valueText = job.reward.label.NullOrEmpty()
                ? BoardJobFormatting.RewardValue(job)
                : "SettlementServices.JobBoard.Detail.RewardValue".Translate(BoardJobFormatting.RewardValue(job)).Resolve();
            Widgets.Label(valueRect, valueText);
            if (!job.reward.label.NullOrEmpty()) TooltipHandler.TipRegion(valueRect, job.reward.label);
            Text.Anchor = TextAnchor.UpperLeft;

            return y + RewardIconSize + RowGap;
        }

        private static float DrawKeyValue(Rect rect, float y, float labelWidth, string key, string value)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x, y, labelWidth, 24f), key);
            Widgets.Label(new Rect(rect.x + labelWidth, y, rect.width - labelWidth, 24f), value);
            Text.Anchor = TextAnchor.UpperLeft;
            return y + 24f;
        }

        private float DrawRoles(Rect rect, float y, BoardJobRecord job, List<Pawn> selected)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "SettlementServices.JobBoard.Detail.Requirements".Translate());
            y += 24f;

            bool showProgress = job.status == BoardJobStatus.Available;
            foreach (BoardSkillRequirement requirement in job.requirements)
            {
                Rect line = new Rect(rect.x + 12f, y, rect.width - 12f, 22f);
                Color previous = GUI.color;
                string text = BoardJobFormatting.RequirementLine(requirement);

                if (showProgress)
                {
                    int qualified = BoardRequirementMatcher.QualifiedCount(requirement, selected);
                    bool met = qualified >= requirement.requiredCount;
                    GUI.color = met ? new Color(0.55f, 0.9f, 0.55f) : Color.white;
                    text = "SettlementServices.JobBoard.Role.Progress".Translate(text, qualified, requirement.requiredCount).Resolve();
                }

                Widgets.Label(line, text);
                GUI.color = previous;
                y += 22f;
            }

            Text.Anchor = TextAnchor.UpperLeft;
            return y;
        }

        private void DrawStartBar(Rect rect, BoardJobRecord job, List<Pawn> selected)
        {
            Settlement settlement = session.settlement;
            string blockKey = BoardJobCoordinator.GetStartBlockKey(job, settlement, session.caravan, selected);

            Rect buttonRect = new Rect(rect.xMax - 180f, rect.yMax - ButtonHeight, 180f, ButtonHeight);
            Rect messageRect = new Rect(rect.x, rect.y, rect.width - 190f, rect.height);

            string message = lastErrorKey != null ? lastErrorKey : blockKey != null && selected.Count > 0 ? blockKey : null;
            if (message != null)
            {
                Color previous = GUI.color;
                GUI.color = new Color(1f, 0.6f, 0.5f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(messageRect, message.Translate(job.partySize, job.title));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = previous;
            }

            if (blockKey != null)
            {
                Widgets.DrawAtlas(buttonRect, Widgets.ButtonBGAtlas);
                Color previous = GUI.color;
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(buttonRect, "SettlementServices.JobBoard.Start".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = previous;
                TooltipHandler.TipRegion(buttonRect, blockKey.Translate(job.partySize, job.title));
                return;
            }

            if (!Widgets.ButtonText(buttonRect, "SettlementServices.JobBoard.Start".Translate())) return;

            lastErrorKey = null;
            if (BoardJobCoordinator.TryStart(SettlementServicesWorldComponent.Current, job, settlement, session.caravan, selected.ToList(), out string errorKey))
            {
                assignment.Clear(job.boardJobId);
                closeWindow();
            }
            else
            {
                lastErrorKey = errorKey;
                Messages.Message(errorKey.Translate(job.partySize, job.title), MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        private void DrawActiveBody(Rect rect, BoardJobRecord job)
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(rect, "SettlementServices.JobBoard.Detail.ActiveWorkers".Translate(BoardJobFormatting.WorkerNames(job), BoardJobFormatting.RemainingTime(job)));
        }

        private void DrawCancelBar(Rect rect, BoardJobRecord job)
        {
            if (confirmingCancelJobId != job.boardJobId)
            {
                Rect cancelRect = new Rect(rect.xMax - 200f, rect.yMax - ButtonHeight, 200f, ButtonHeight);
                if (Widgets.ButtonText(cancelRect, "SettlementServices.JobBoard.CancelJob".Translate())) confirmingCancelJobId = job.boardJobId;
                return;
            }

            Rect textRect = new Rect(rect.x, rect.y, rect.width - 260f, rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(textRect, "SettlementServices.JobBoard.CancelConfirm".Translate(job.title));
            Text.Anchor = TextAnchor.UpperLeft;

            Rect confirmRect = new Rect(rect.xMax - 250f, rect.yMax - ButtonHeight, 120f, ButtonHeight);
            Rect keepRect = new Rect(rect.xMax - 124f, rect.yMax - ButtonHeight, 120f, ButtonHeight);
            if (Widgets.ButtonText(confirmRect, "SettlementServices.JobBoard.CancelConfirmYes".Translate()))
            {
                confirmingCancelJobId = -1;
                BoardJobCoordinator.TryCancelActive(SettlementServicesWorldComponent.Current, job);
            }
            if (Widgets.ButtonText(keepRect, "SettlementServices.JobBoard.CancelConfirmNo".Translate())) confirmingCancelJobId = -1;
        }

        private void DrawFinishedBody(Rect rect, BoardJobRecord job)
        {
            string text = BoardJobFormatting.StatusLabel(job);
            if (!job.failureKey.NullOrEmpty()) text += "\n" + BoardJobNotifier.FailureText(job);
            Widgets.Label(rect, text);
        }
    }
}

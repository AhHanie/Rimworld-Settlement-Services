using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Board;

namespace Settlement_Services.UI.JobBoard
{
    internal sealed class BoardPawnAssignmentPresenter
    {
        private const float RowHeight = 32f;
        private const float HeaderHeight = 24f;
        private const float SkillColumnWidth = 72f;
        private const float CheckboxSize = 24f;

        private readonly Dictionary<int, List<Pawn>> drafts = new Dictionary<int, List<Pawn>>();
        private Vector2 scroll;

        internal List<Pawn> SelectedFor(BoardJobRecord job, Caravan caravan)
        {
            if (!drafts.TryGetValue(job.boardJobId, out List<Pawn> draft)) return new List<Pawn>();

            draft.RemoveAll(p => p == null || p.Destroyed || caravan == null || caravan.Destroyed || !caravan.PawnsListForReading.Contains(p));
            return draft;
        }

        internal void Clear(int boardJobId) => drafts.Remove(boardJobId);

        internal void Draw(Rect rect, BoardJobRecord job, Caravan caravan)
        {
            List<Pawn> selected = SelectedFor(job, caravan);
            List<SkillDef> skills = job.requirements.Where(r => r.skill != null).Select(r => r.skill).Distinct().ToList();

            Rect headerRect = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
            DrawHeader(headerRect, job, skills, selected.Count);

            List<Pawn> candidates = caravan == null || caravan.Destroyed
                ? new List<Pawn>()
                : caravan.PawnsListForReading.Where(p => p.RaceProps.Humanlike).ToList();

            var rows = candidates
                .Select(p => new { pawn = p, reason = BoardPawnEligibility.GetBlockReasonKey(p, caravan, job) })
                .OrderBy(r => r.reason != null)
                .ThenBy(r => r.pawn.LabelShortCap)
                .ToList();

            Rect listRect = new Rect(rect.x, headerRect.yMax, rect.width, rect.height - HeaderHeight);
            if (rows.Count == 0)
            {
                Widgets.NoneLabelCenteredVertically(listRect, "SettlementServices.JobBoard.NoCaravanPawns".Translate());
                return;
            }

            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, rows.Count * RowHeight);
            Widgets.BeginScrollView(listRect, ref scroll, viewRect);
            float y = 0f;
            foreach (var row in rows)
            {
                Rect rowRect = new Rect(0f, y, viewRect.width, RowHeight);
                if (rowRect.yMax >= scroll.y && rowRect.y <= scroll.y + listRect.height)
                    DrawRow(rowRect, job, skills, selected, row.pawn, row.reason);
                y += RowHeight;
            }
            Widgets.EndScrollView();
        }

        private static void DrawHeader(Rect rect, BoardJobRecord job, List<SkillDef> skills, int selectedCount)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect labelRect = new Rect(rect.x, rect.y, rect.width - skills.Count * SkillColumnWidth - 260f, rect.height);
            Widgets.Label(labelRect, "SettlementServices.JobBoard.SelectParty".Translate(selectedCount, job.partySize));

            float x = rect.xMax - 16f - skills.Count * SkillColumnWidth;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            foreach (SkillDef skill in skills)
            {
                Widgets.Label(new Rect(x, rect.y, SkillColumnWidth, rect.height), skill.skillLabel.CapitalizeFirst());
                x += SkillColumnWidth;
            }
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawRow(Rect rect, BoardJobRecord job, List<SkillDef> skills, List<Pawn> selected, Pawn pawn, string blockReasonKey)
        {
            bool blocked = blockReasonKey != null;
            bool isSelected = selected.Contains(pawn);

            if (isSelected) Widgets.DrawHighlightSelected(rect);
            else Widgets.DrawHighlightIfMouseover(rect);

            Color previous = GUI.color;
            if (blocked) GUI.color = Color.gray;

            bool canToggle = !blocked && (isSelected || selected.Count < job.partySize);
            bool checkOn = isSelected;
            Widgets.Checkbox(rect.x + 4f, rect.y + (rect.height - CheckboxSize) / 2f, ref checkOn, CheckboxSize, disabled: !canToggle);
            if (checkOn != isSelected) Toggle(job, selected, pawn, checkOn);

            Rect infoRect = new Rect(rect.x + CheckboxSize + 8f, rect.y + (rect.height - 24f) / 2f, 24f, 24f);
            Widgets.InfoCardButton(infoRect.x, infoRect.y, pawn);

            float skillsWidth = skills.Count * SkillColumnWidth;
            float statusWidth = 190f;
            Rect nameRect = new Rect(infoRect.xMax + 4f, rect.y, rect.width - infoRect.xMax - skillsWidth - statusWidth - 8f, rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameRect, pawn.LabelShortCap.Truncate(nameRect.width));

            float x = rect.xMax - skillsWidth;
            Text.Anchor = TextAnchor.MiddleCenter;
            foreach (SkillDef skill in skills)
            {
                DrawSkillCell(new Rect(x, rect.y, SkillColumnWidth, rect.height), job, pawn, skill, blocked);
                x += SkillColumnWidth;
            }

            if (blocked)
            {
                Rect reasonRect = new Rect(nameRect.xMax, rect.y, statusWidth, rect.height);
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Tiny;
                string reason = blockReasonKey.Translate();
                Widgets.Label(reasonRect, reason.Truncate(reasonRect.width));
                TooltipHandler.TipRegion(reasonRect, reason);
                Text.Font = GameFont.Small;
            }

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = previous;

            if (canToggle && Widgets.ButtonInvisible(new Rect(nameRect.x, rect.y, nameRect.width, rect.height)))
                Toggle(job, selected, pawn, !isSelected);
        }

        private static void DrawSkillCell(Rect rect, BoardJobRecord job, Pawn pawn, SkillDef skill, bool blocked)
        {
            SkillRecord record = pawn.skills?.GetSkill(skill);
            string text;
            Color color = GUI.color;

            if (record == null || record.TotallyDisabled)
            {
                text = "-";
            }
            else
            {
                text = record.Level.ToString();
                bool meets = job.requirements.Any(r => r.skill == skill && BoardRequirementMatcher.Qualifies(pawn, r));
                if (!blocked) GUI.color = meets ? new Color(0.55f, 0.9f, 0.55f) : Color.white;
            }

            Widgets.Label(rect, text);
            GUI.color = color;
        }

        private void Toggle(BoardJobRecord job, List<Pawn> selected, Pawn pawn, bool on)
        {
            if (!drafts.TryGetValue(job.boardJobId, out List<Pawn> draft))
            {
                draft = selected;
                drafts[job.boardJobId] = draft;
            }

            if (on && !draft.Contains(pawn) && draft.Count < job.partySize) draft.Add(pawn);
            else if (!on) draft.Remove(pawn);
        }
    }
}

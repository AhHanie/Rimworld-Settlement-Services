using System;
using System.Linq;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;
using Settlement_Services.Domain;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Registry;
using Settlement_Services.Framework.Specialty;

namespace Settlement_Services.UI
{
    public class Dialog_SettlementServices : Window
    {
        private const float HeaderHeight = 42f;
        private const float HeaderGutter = 10f;
        private const float BadgeSize = 30f;
        private const float BadgeSpacing = 6f;
        private const float CategoryColumnWidth = 220f;
        private const float ServiceColumnX = CategoryColumnWidth + HeaderGutter;

        private readonly ServiceRequestSession contextTemplate;
        private ServiceCategoryDef selectedCategory;
        private Vector2 categoryScroll, serviceScroll;

        public override Vector2 InitialSize => new Vector2(900f, 700f);
        public Settlement Settlement => contextTemplate.settlement;

        public Dialog_SettlementServices(ServiceRequestSession session)
        {
            contextTemplate = session;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            selectedCategory = DefDatabase<ServiceCategoryDef>.AllDefsListForReading
                .Where(CategoryHasCandidates)
                .OrderBy(c => c.order)
                .FirstOrDefault();
            ServiceDiscoveryRecorder.RecordCandidates(session.settlement, session.channel);
        }

        public override void DoWindowContents(Rect inRect)
        {
            DrawHeader(new Rect(0f, 0f, inRect.width, HeaderHeight));

            Rect bodyRect = new Rect(0f, HeaderHeight + HeaderGutter, inRect.width, inRect.height - HeaderHeight - HeaderGutter);
            Rect categoryRect = new Rect(bodyRect.x, bodyRect.y, CategoryColumnWidth, bodyRect.height);
            Rect serviceRect = new Rect(ServiceColumnX, bodyRect.y, bodyRect.width - ServiceColumnX, bodyRect.height);
            DrawCategoryList(categoryRect);
            DrawServiceList(serviceRect);
        }

        private void DrawHeader(Rect rect)
        {
            Faction faction = Settlement?.Faction;
            DrawFactionBlock(new Rect(rect.x, rect.y, CategoryColumnWidth, rect.height), faction);
            DrawSpecialtyBlock(rect, faction);
        }

        private static void DrawFactionBlock(Rect rect, Faction faction)
        {
            if (faction == null)
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(rect, "SettlementServices.Header.NoFaction".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            Rect iconRect = new Rect(rect.x, rect.y + (rect.height - BadgeSize) / 2f, BadgeSize, BadgeSize);
            GUI.color = faction.Color;
            GUI.DrawTexture(iconRect, faction.def.FactionIcon);
            GUI.color = Color.white;

            Rect nameRect = new Rect(iconRect.xMax + 6f, rect.y, rect.xMax - iconRect.xMax - 6f, rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            string factionName = faction.Name.CapitalizeFirst();
            string label = DebugSettings.godMode
                ? "SettlementServices.Header.FactionNameWithTechLevel".Translate(factionName, faction.def.techLevel.ToStringHuman()).Resolve()
                : factionName;
            Widgets.Label(nameRect, label.Truncate(nameRect.width));
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawSpecialtyBlock(Rect rect, Faction faction)
        {
            string label = "SettlementServices.Header.SpecialtiesLabel".Translate();
            Rect labelRect = new Rect(ServiceColumnX, rect.y, Text.CalcSize(label).x, rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, label);
            Text.Anchor = TextAnchor.UpperLeft;

            IReadOnlyList<SettlementSpecialtyDef> specialties = faction == null
                ? (IReadOnlyList<SettlementSpecialtyDef>)Array.Empty<SettlementSpecialtyDef>()
                : SettlementSpecialtyService.GetSpecialties(Settlement);
            DrawSpecialtyBadges(rect, labelRect.xMax + BadgeSpacing, specialties);
        }

        private void DrawSpecialtyBadges(Rect rect, float x, IReadOnlyList<SettlementSpecialtyDef> specialties)
        {
            if (specialties.Count == 0)
            {
                Rect statusRect = new Rect(x, rect.y, Mathf.Max(0f, rect.xMax - x), rect.height);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(statusRect, "SettlementServices.Header.NoSpecialties".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            float available = rect.xMax - x;
            int maxSlots = Mathf.Max(0, Mathf.FloorToInt((available + BadgeSpacing) / (BadgeSize + BadgeSpacing)));
            bool overflow = specialties.Count > maxSlots;
            int shown = overflow ? Mathf.Max(0, maxSlots - 1) : specialties.Count;

            for (int i = 0; i < shown; i++)
            {
                Rect badgeRect = new Rect(x, rect.y + (rect.height - BadgeSize) / 2f, BadgeSize, BadgeSize);
                DrawSpecialtyBadge(badgeRect, specialties[i]);
                x += BadgeSize + BadgeSpacing;
            }

            if (overflow)
            {
                Rect countRect = new Rect(x, rect.y + (rect.height - BadgeSize) / 2f, BadgeSize, BadgeSize);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(countRect, "SettlementServices.Header.HiddenSpecialtyCount".Translate(specialties.Count - shown));
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        private void DrawSpecialtyBadge(Rect rect, SettlementSpecialtyDef specialty)
        {
            Widgets.DrawHighlightIfMouseover(rect);

            Texture2D icon = ServiceUITextures.Resolve(specialty.iconTexPath);
            if (icon != null)
            {
                GUI.DrawTexture(rect, icon);
            }
            else
            {
                GUI.BeginGroup(rect);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(0f, 0f, rect.width, rect.height), specialty.LabelCap);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.EndGroup();
            }

            Settlement settlement = Settlement;
            TipSignal tip = new TipSignal(() => SettlementSpecialtyTooltip.Build(settlement, specialty), settlement.ID ^ specialty.shortHash);
            TooltipHandler.TipRegion(rect, tip);
        }

        private void DrawCategoryList(Rect rect)
        {
            List<ServiceCategoryDef> categories = DefDatabase<ServiceCategoryDef>.AllDefsListForReading
                .Where(CategoryHasCandidates)
                .OrderBy(c => c.order)
                .ToList();
            float rowHeight = 34f;
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, categories.Count * rowHeight);
            Widgets.BeginScrollView(rect, ref categoryScroll, viewRect);
            float y = 0f;
            foreach (ServiceCategoryDef category in categories)
            {
                Rect rowRect = new Rect(0f, y, viewRect.width, rowHeight - 2f);
                bool selected = category == selectedCategory;
                if (DrawSelectableRow(rowRect, category.LabelCap, ServiceUITextures.Resolve(category.iconTexPath), selected, false, null))
                    selectedCategory = category;
                y += rowHeight;
            }
            Widgets.EndScrollView();
        }

        private bool CategoryHasCandidates(ServiceCategoryDef category) =>
            SettlementServiceRegistry.ForCategory(category).Any(IsCandidate);

        private bool IsCandidate(SettlementServiceDef def) =>
            ServiceCandidacyService.IsCandidateForChannel(def, contextTemplate.settlement, contextTemplate.channel);

        private void DrawServiceList(Rect rect)
        {
            if (selectedCategory == null)
            {
                Widgets.NoneLabelCenteredVertically(rect, "SettlementServices.Label.NoServicesInCategory".Translate());
                return;
            }
            List<SettlementServiceDef> candidates = SettlementServiceRegistry.ForCategory(selectedCategory)
                .Where(IsCandidate)
                .OrderBy(d => d.label)
                .ToList();

            if (candidates.Count == 0)
            {
                Widgets.NoneLabelCenteredVertically(rect, "SettlementServices.Label.NoServicesInCategory".Translate());
                return;
            }

            float rowHeight = 56f;
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, candidates.Count * rowHeight);
            Widgets.BeginScrollView(rect, ref serviceScroll, viewRect);
            float y = 0f;
            foreach (SettlementServiceDef def in candidates)
            {
                Rect rowRect = new Rect(0f, y, viewRect.width, rowHeight - 4f);
                if (rowRect.y + rowHeight >= serviceScroll.y && rowRect.y <= serviceScroll.y + rect.height)
                    DrawServiceRow(rowRect, def);
                y += rowHeight;
            }
            Widgets.EndScrollView();
        }

        private void DrawServiceRow(Rect rect, SettlementServiceDef def)
        {
            bool unavailable = ServiceCandidacyService.TryGetUnavailableReason(def, contextTemplate.settlement, out string reasonKey);
            Texture2D icon = ServiceUITextures.Resolve(def.iconTexPath ?? selectedCategory.iconTexPath);
            string tooltip = unavailable ? ServiceErrorFormatting.Format(reasonKey, def, contextTemplate.settlement) : null;
            if (DrawSelectableRow(rect, def.LabelCap, icon, false, unavailable, tooltip))
                OpenDetail(def);
        }

        private static bool DrawSelectableRow(Rect rect, string label, Texture2D icon, bool selected, bool grayedOut, string tooltip)
        {
            if (selected) Widgets.DrawOptionBackground(rect, true);
            else Widgets.DrawHighlightIfMouseover(rect);

            Rect content = rect;
            if (icon != null)
            {
                Rect iconRect = new Rect(content.x + 4f, content.y + (content.height - 24f) / 2f, 24f, 24f);
                Color prevColor = GUI.color;
                if (grayedOut) GUI.color = Color.gray;
                GUI.DrawTexture(iconRect, icon);
                GUI.color = prevColor;
                content.xMin = iconRect.xMax + 6f;
            }

            Color labelPrevColor = GUI.color;
            if (grayedOut) GUI.color = Color.gray;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(content, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = labelPrevColor;

            if (!tooltip.NullOrEmpty()) TooltipHandler.TipRegion(rect, tooltip);

            bool clicked = Widgets.ButtonInvisible(rect);
            if (clicked) SoundDefOf.Click.PlayOneShotOnCamera();
            return clicked;
        }

        private void OpenDetail(SettlementServiceDef def)
        {
            if (Find.WindowStack.IsOpen<Dialog_ServiceRequestDetail>()) return;
            var session = contextTemplate.channel == RequestChannel.Remote
                ? ServiceRequestSession.ForRemote(contextTemplate.settlement, contextTemplate.negotiator)
                : ServiceRequestSession.ForInPersonVisit(contextTemplate.settlement, contextTemplate.caravan);
            session.def = def;
            Find.WindowStack.Add(new Dialog_ServiceRequestDetail(session));
        }
    }
}

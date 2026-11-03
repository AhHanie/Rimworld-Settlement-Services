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
        private enum ServiceOverviewViewMode { Services, JobBoard, KnownServices }

        private Vector2 scrollPosition;
        private Vector2 knownServicesScrollPosition;
        private readonly BoardOverviewView boardView = new BoardOverviewView();
        private ServiceOverviewViewMode viewMode = ServiceOverviewViewMode.Services;
        private ServiceOverviewGrouping grouping = ServiceOverviewGrouping.Settlement;
        private int? filterSettlementWorldObjectId;
        private ServiceCategoryDef filterCategory;
        private ServiceTargetRule? filterTargetRule;
        private ServiceJobStatus? filterStatus;

        public override Vector2 RequestedTabSize => new Vector2(920f, 520f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;

            float modeX = 0f;
            foreach (ServiceOverviewViewMode mode in Enum.GetValues(typeof(ServiceOverviewViewMode)))
            {
                Rect modeRect = new Rect(modeX, 0f, 150f, 30f);
                if (mode == viewMode) Widgets.DrawOptionBackground(modeRect, true);
                if (Widgets.ButtonText(modeRect, ("SettlementServices.Label.ViewMode." + mode).Translate())) viewMode = mode;
                modeX += 156f;
            }

            Rect remainder = new Rect(0f, 34f, inRect.width, inRect.height - 34f);
            switch (viewMode)
            {
                case ServiceOverviewViewMode.Services: DrawJobsView(remainder); break;
                case ServiceOverviewViewMode.JobBoard: boardView.Draw(remainder); break;
                default: DrawKnownServicesView(remainder); break;
            }
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

        private enum KnownServicesDisplayMode { Text, Icons }
        private enum KnownServicesScope { All, Specialties, Services }
        private enum KnownServicesFilterKind { All, Specialty, Service }

        private KnownServicesDisplayMode knownServicesDisplay = KnownServicesDisplayMode.Icons;
        private KnownServicesScope knownServicesScope = KnownServicesScope.All;
        private KnownServicesFilterKind knownServicesFilterKind = KnownServicesFilterKind.All;
        private string knownServicesFilterDefName;

        private const float KnownServicesRowMinHeight = 60f;
        private const float KnownServicesJumpButtonWidth = 90f;
        private const float KnownServicesJumpButtonGutter = 6f;
        private const float KnownServicesToolbarHeight = 30f;
        private const float KnownServicesToolbarGap = 6f;
        private const float KnownServicesDisplayButtonWidth = 90f;
        private const float KnownServicesFindButtonWidth = 340f;
        private const float KnownServicesScopeButtonWidth = 170f;
        private const float KnownServicesBadgeSize = 30f;
        private const float KnownServicesBadgeGap = 4f;

        private void DrawKnownServicesView(Rect inRect)
        {
            List<ServiceDiscoveryOverviewEntry> allEntries = ServiceOverviewQueryService.BuildDiscoveryEntries();
            List<ServiceDiscoveryOverviewEntry> liveEntries = allEntries.Where(e => e.settlement != null).ToList();
            List<SettlementSpecialtyDef> specialtyChoices = liveEntries
                .SelectMany(e => e.specialties)
                .GroupBy(s => s.defName)
                .Select(g => g.First())
                .OrderBy(s => (string)s.LabelCap, StringComparer.CurrentCulture)
                .ThenBy(s => s.defName, StringComparer.Ordinal)
                .ToList();
            List<SettlementServiceDef> serviceChoices = liveEntries
                .SelectMany(e => e.inPersonServices.Concat(e.remoteServices))
                .GroupBy(s => s.defName)
                .Select(g => g.First())
                .OrderBy(s => (string)s.LabelCap, StringComparer.CurrentCulture)
                .ThenBy(s => s.defName, StringComparer.Ordinal)
                .ToList();
            ResetStaleKnownServicesFilter(specialtyChoices, serviceChoices);

            Rect toolbarRect = new Rect(inRect.x, inRect.y, inRect.width, KnownServicesToolbarHeight);
            DrawKnownServicesToolbar(toolbarRect, specialtyChoices, serviceChoices);

            float listTop = toolbarRect.yMax + KnownServicesToolbarGap;
            Rect listRect = new Rect(inRect.x, listTop, inRect.width, inRect.height - (listTop - inRect.y));

            if (allEntries.Count == 0)
            {
                Widgets.NoneLabelCenteredVertically(listRect, "SettlementServices.Label.NoKnownServicesToShow".Translate());
                return;
            }

            List<ServiceDiscoveryOverviewEntry> entries = allEntries.Where(MatchesKnownServicesFilter).ToList();
            if (entries.Count == 0)
            {
                Widgets.NoneLabelCenteredVertically(listRect, "SettlementServices.Label.KnownServicesNoMatches".Translate());
                return;
            }

            bool icons = knownServicesDisplay == KnownServicesDisplayMode.Icons;
            KnownServicesScope scope = knownServicesScope;
            float textWidth = listRect.width - 16f - KnownServicesJumpButtonWidth - KnownServicesJumpButtonGutter;
            float[] rowStrides = entries.Select(e => icons ? KnownServicesIconsRowStride(e, textWidth, scope) : KnownServicesRowStride(e, textWidth, scope == KnownServicesScope.Specialties)).ToArray();
            float totalHeight = rowStrides.Sum();
            knownServicesScrollPosition.y = Mathf.Clamp(knownServicesScrollPosition.y, 0f, Mathf.Max(0f, totalHeight - listRect.height));

            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, totalHeight);
            Widgets.BeginScrollView(listRect, ref knownServicesScrollPosition, viewRect);
            float y = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                float stride = rowStrides[i];
                Rect rowRect = new Rect(0f, y, viewRect.width, stride - 2f);
                if (rowRect.y + stride >= knownServicesScrollPosition.y && rowRect.y <= knownServicesScrollPosition.y + listRect.height)
                {
                    if (icons) DrawKnownServicesIconsRow(rowRect, entries[i], scope);
                    else DrawKnownServicesRow(rowRect, entries[i], scope == KnownServicesScope.Specialties);
                }
                y += stride;
            }
            Widgets.EndScrollView();
        }

        private void ResetStaleKnownServicesFilter(List<SettlementSpecialtyDef> specialtyChoices, List<SettlementServiceDef> serviceChoices)
        {
            bool stale = false;
            switch (knownServicesFilterKind)
            {
                case KnownServicesFilterKind.Specialty:
                    stale = !specialtyChoices.Any(s => s.defName == knownServicesFilterDefName);
                    break;
                case KnownServicesFilterKind.Service:
                    stale = !serviceChoices.Any(s => s.defName == knownServicesFilterDefName);
                    break;
            }
            if (stale) SetKnownServicesFilter(KnownServicesFilterKind.All, null);
        }

        private void SetKnownServicesFilter(KnownServicesFilterKind kind, string defName)
        {
            knownServicesFilterKind = kind;
            knownServicesFilterDefName = defName;
            knownServicesScrollPosition.y = 0f;
        }

        private void SetKnownServicesScope(KnownServicesScope scope)
        {
            knownServicesScope = scope;
            knownServicesScrollPosition.y = 0f;
            bool filterFits = knownServicesFilterKind == KnownServicesFilterKind.All
                || (knownServicesFilterKind == KnownServicesFilterKind.Specialty && scope != KnownServicesScope.Services)
                || (knownServicesFilterKind == KnownServicesFilterKind.Service && scope != KnownServicesScope.Specialties);
            if (!filterFits) SetKnownServicesFilter(KnownServicesFilterKind.All, null);
        }

        private bool MatchesKnownServicesFilter(ServiceDiscoveryOverviewEntry entry)
        {
            switch (knownServicesFilterKind)
            {
                case KnownServicesFilterKind.Specialty:
                    return entry.settlement != null && entry.HasSpecialty(knownServicesFilterDefName);
                case KnownServicesFilterKind.Service:
                    return entry.settlement != null && entry.HasDiscoveredService(knownServicesFilterDefName);
                default:
                    return knownServicesScope != KnownServicesScope.Specialties || (entry.settlement != null && entry.specialties.Count > 0);
            }
        }

        private void DrawKnownServicesToolbar(Rect rect, List<SettlementSpecialtyDef> specialtyChoices, List<SettlementServiceDef> serviceChoices)
        {
            float x = rect.x;
            foreach (KnownServicesDisplayMode mode in Enum.GetValues(typeof(KnownServicesDisplayMode)))
            {
                Rect modeRect = new Rect(x, rect.y, KnownServicesDisplayButtonWidth, rect.height);
                if (mode == knownServicesDisplay) Widgets.DrawOptionBackground(modeRect, true);
                if (Widgets.ButtonText(modeRect, ("SettlementServices.Label.KnownServicesView" + mode).Translate())) knownServicesDisplay = mode;
                x += KnownServicesDisplayButtonWidth + 6f;
            }

            float findX = Mathf.Max(x + 6f + KnownServicesScopeButtonWidth + 6f, rect.xMax - KnownServicesFindButtonWidth);
            Rect scopeRect = new Rect(findX - 6f - KnownServicesScopeButtonWidth, rect.y, KnownServicesScopeButtonWidth, rect.height);
            if (Widgets.ButtonText(scopeRect, "SettlementServices.Label.KnownServicesScope".Translate(("SettlementServices.Label.KnownServicesScope" + knownServicesScope).Translate())))
                OpenKnownServicesScopeMenu();

            Rect findRect = new Rect(findX, rect.y, KnownServicesFindButtonWidth, rect.height);
            if (Widgets.ButtonText(findRect, "SettlementServices.Label.KnownServicesFind".Translate(KnownServicesFilterLabel(specialtyChoices, serviceChoices))))
                OpenKnownServicesFindMenu(specialtyChoices, serviceChoices);
        }

        private string KnownServicesFilterLabel(List<SettlementSpecialtyDef> specialtyChoices, List<SettlementServiceDef> serviceChoices)
        {
            switch (knownServicesFilterKind)
            {
                case KnownServicesFilterKind.Specialty:
                    SettlementSpecialtyDef specialty = specialtyChoices.FirstOrDefault(s => s.defName == knownServicesFilterDefName);
                    if (specialty != null) return "SettlementServices.Label.KnownServicesFindOption".Translate("SettlementServices.Label.KnownServicesFindSpecialty".Translate(), specialty.LabelCap);
                    break;
                case KnownServicesFilterKind.Service:
                    SettlementServiceDef service = serviceChoices.FirstOrDefault(s => s.defName == knownServicesFilterDefName);
                    if (service != null) return "SettlementServices.Label.KnownServicesFindOption".Translate("SettlementServices.Label.KnownServicesFindService".Translate(), service.LabelCap);
                    break;
            }
            return "SettlementServices.Label.All".Translate();
        }

        private void OpenKnownServicesScopeMenu()
        {
            var options = new List<FloatMenuOption>();
            foreach (KnownServicesScope scope in Enum.GetValues(typeof(KnownServicesScope)))
            {
                KnownServicesScope chosen = scope;
                options.Add(new FloatMenuOption(("SettlementServices.Label.KnownServicesScope" + scope).Translate(), () => SetKnownServicesScope(chosen)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenKnownServicesFindMenu(List<SettlementSpecialtyDef> specialtyChoices, List<SettlementServiceDef> serviceChoices)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("SettlementServices.Label.All".Translate(), () => SetKnownServicesFilter(KnownServicesFilterKind.All, null))
            };

            string specialtyGroup = "SettlementServices.Label.KnownServicesFindSpecialty".Translate();
            foreach (SettlementSpecialtyDef specialty in specialtyChoices)
            {
                if (knownServicesScope == KnownServicesScope.Services) break;
                string defName = specialty.defName;
                options.Add(new FloatMenuOption("SettlementServices.Label.KnownServicesFindOption".Translate(specialtyGroup, specialty.LabelCap),
                    () => SetKnownServicesFilter(KnownServicesFilterKind.Specialty, defName)));
            }

            string serviceGroup = "SettlementServices.Label.KnownServicesFindService".Translate();
            foreach (SettlementServiceDef service in serviceChoices)
            {
                if (knownServicesScope == KnownServicesScope.Specialties) break;
                string defName = service.defName;
                options.Add(new FloatMenuOption("SettlementServices.Label.KnownServicesFindOption".Translate(serviceGroup, service.LabelCap),
                    () => SetKnownServicesFilter(KnownServicesFilterKind.Service, defName)));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static float KnownServicesRowStride(ServiceDiscoveryOverviewEntry entry, float textWidth, bool specialtiesOnly)
        {
            float height = Text.CalcHeight(entry.settlementLabel, textWidth);
            if (specialtiesOnly)
                return Mathf.Max(height + Text.CalcHeight(KnownServicesSpecialtiesText(entry), textWidth) + 2f, KnownServicesRowMinHeight);
            if (entry.inPersonLabel != null)
                height += Text.CalcHeight("SettlementServices.Label.KnownServicesInPerson".Translate(entry.inPersonLabel), textWidth);
            if (entry.remoteLabel != null)
                height += Text.CalcHeight("SettlementServices.Label.KnownServicesRemote".Translate(entry.remoteLabel), textWidth);
            return Mathf.Max(height + 2f, KnownServicesRowMinHeight);
        }

        private static string KnownServicesSpecialtiesText(ServiceDiscoveryOverviewEntry entry)
        {
            return "SettlementServices.Label.InspectSpecialties".Translate(string.Join(", ", entry.specialties.Select(s => (string)s.LabelCap)));
        }

        private void DrawKnownServicesRow(Rect rect, ServiceDiscoveryOverviewEntry entry, bool specialtiesOnly)
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
            if (specialtiesOnly)
            {
                string specialtiesText = KnownServicesSpecialtiesText(entry);
                Widgets.Label(new Rect(textRect.x, y, textRect.width, Text.CalcHeight(specialtiesText, textRect.width)), specialtiesText);
                GUI.color = prevColor;
                return;
            }
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

        private static float KnownServicesIconsRowStride(ServiceDiscoveryOverviewEntry entry, float textWidth, KnownServicesScope scope)
        {
            float height = LayoutKnownServicesIcons(entry, new Rect(0f, 0f, textWidth, 0f), false, scope);
            return Mathf.Max(height + 2f, KnownServicesRowMinHeight);
        }

        private static void DrawKnownServicesIconsRow(Rect rect, ServiceDiscoveryOverviewEntry entry, KnownServicesScope scope)
        {
            Widgets.DrawHighlightIfMouseover(rect);

            Rect jumpRect = new Rect(rect.x, rect.y, KnownServicesJumpButtonWidth, rect.height);
            if (Widgets.ButtonText(jumpRect, "SettlementServices.Button.Jump".Translate()))
                JumpToSettlement(entry.settlement);

            Rect contentRect = new Rect(jumpRect.xMax + KnownServicesJumpButtonGutter, rect.y, rect.width - jumpRect.width - KnownServicesJumpButtonGutter, rect.height);
            LayoutKnownServicesIcons(entry, contentRect, true, scope);
        }

        private static float LayoutKnownServicesIcons(ServiceDiscoveryOverviewEntry entry, Rect rect, bool paint, KnownServicesScope scope)
        {
            float y = rect.y;
            float titleHeight = Text.CalcHeight(entry.settlementLabel, rect.width);
            if (paint) Widgets.Label(new Rect(rect.x, y, rect.width, titleHeight), entry.settlementLabel);
            y += titleHeight;

            if (scope != KnownServicesScope.Services)
            {
                y = LayoutKnownServicesBadgeSection(rect, y, "SettlementServices.Header.SpecialtiesLabel".Translate(),
                    "SettlementServices.Header.NoSpecialties".Translate(), entry.specialties.Count,
                    (badgeRect, i) => DrawKnownServicesSpecialtyBadge(badgeRect, entry.settlement, entry.specialties[i]), paint);
            }

            if (scope == KnownServicesScope.Specialties) return y - rect.y;

            if (entry.inPersonServices.Count > 0)
            {
                y = LayoutKnownServicesBadgeSection(rect, y, "SettlementServices.Label.KnownServicesIconsInPerson".Translate(),
                    null, entry.inPersonServices.Count,
                    (badgeRect, i) => DrawKnownServicesServiceBadge(badgeRect, entry.settlement, entry.inPersonServices[i], RequestChannel.InPerson), paint);
            }
            if (entry.remoteServices.Count > 0)
            {
                y = LayoutKnownServicesBadgeSection(rect, y, "SettlementServices.Label.KnownServicesIconsRemote".Translate(),
                    null, entry.remoteServices.Count,
                    (badgeRect, i) => DrawKnownServicesServiceBadge(badgeRect, entry.settlement, entry.remoteServices[i], RequestChannel.Remote), paint);
            }

            return y - rect.y;
        }

        private static float LayoutKnownServicesBadgeSection(Rect rect, float y, string heading, string emptyText, int count, Action<Rect, int> drawBadge, bool paint)
        {
            float headingHeight = Text.LineHeight;
            if (paint)
            {
                Color prevColor = GUI.color;
                GUI.color = Color.gray;
                float headingWidth = Text.CalcSize(heading).x;
                Widgets.Label(new Rect(rect.x, y, Mathf.Min(rect.width, headingWidth + 4f), headingHeight), heading);
                if (count == 0 && emptyText != null)
                {
                    float emptyX = rect.x + headingWidth + 8f;
                    Widgets.Label(new Rect(emptyX, y, Mathf.Max(0f, rect.xMax - emptyX), headingHeight), emptyText);
                }
                GUI.color = prevColor;
            }
            y += headingHeight;
            if (count == 0) return y;

            float stepX = KnownServicesBadgeSize + KnownServicesBadgeGap;
            int perLine = Mathf.Max(1, Mathf.FloorToInt((rect.width + KnownServicesBadgeGap) / stepX));
            int lines = Mathf.CeilToInt(count / (float)perLine);
            if (paint)
            {
                for (int i = 0; i < count; i++)
                {
                    Rect badgeRect = new Rect(rect.x + (i % perLine) * stepX, y + (i / perLine) * stepX, KnownServicesBadgeSize, KnownServicesBadgeSize);
                    drawBadge(badgeRect, i);
                }
            }
            return y + lines * stepX;
        }

        private static void DrawKnownServicesSpecialtyBadge(Rect rect, Settlement settlement, SettlementSpecialtyDef specialty)
        {
            DrawKnownServicesBadge(rect, ServiceUITextures.Resolve(specialty.iconTexPath), specialty.LabelCap);
            TipSignal tip = new TipSignal(() => SettlementSpecialtyTooltip.Build(settlement, specialty), settlement.ID ^ specialty.shortHash);
            TooltipHandler.TipRegion(rect, tip);
        }

        private static void DrawKnownServicesServiceBadge(Rect rect, Settlement settlement, SettlementServiceDef service, RequestChannel channel)
        {
            DrawKnownServicesBadge(rect, ServiceUITextures.Resolve(service.iconTexPath ?? service.category?.iconTexPath), service.LabelCap);
            string categoryLabel = service.category != null ? (string)service.category.LabelCap : string.Empty;
            string key = channel == RequestChannel.InPerson
                ? "SettlementServices.Label.KnownServicesServiceTipInPerson"
                : "SettlementServices.Label.KnownServicesServiceTipRemote";
            string tip = key.Translate(service.LabelCap, categoryLabel);
            TooltipHandler.TipRegion(rect, new TipSignal(tip, (settlement.ID * 397) ^ service.shortHash ^ (int)channel));
        }

        private static void DrawKnownServicesBadge(Rect rect, Texture2D icon, string fallbackLabel)
        {
            Widgets.DrawHighlightIfMouseover(rect);
            if (icon != null)
            {
                Widgets.DrawTextureFitted(rect.ContractedBy(2f), icon, 1f);
                return;
            }

            Widgets.DrawBox(rect);
            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            string glyph = fallbackLabel.NullOrEmpty() ? "?" : fallbackLabel.Substring(0, Mathf.Min(2, fallbackLabel.Length));
            Widgets.Label(rect, glyph);
            Text.Anchor = prevAnchor;
            Text.Font = prevFont;
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

        internal static void JumpToSettlement(Settlement settlement)
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

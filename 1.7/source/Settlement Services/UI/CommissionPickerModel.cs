using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using Verse;

namespace Settlement_Services.UI
{
    internal enum CommissionPickerKind
    {
        Crafting,
        Construction,
    }

    internal static class CommissionPickerKeys
    {
        public const string All = "all";
        public const string Official = "official";
        public const string Unknown = "unknown";
        public const string None = "@none";
        public const string Ranged = "@ranged";
        public const string Melee = "@melee";
        public const string Weapons = "weapons";
        public const string Apparel = "apparel";
        public const string Other = "other";
    }

    internal static class CommissionPickerPrefs
    {
        public static void Load(CommissionPickerKind kind, out string type, out string subcategory, out string source)
        {
            ModSettings settings = ModSettings.Current;
            if (kind == CommissionPickerKind.Crafting)
            {
                type = settings.craftingPickerType;
                subcategory = settings.craftingPickerSubcategory;
                source = settings.craftingPickerSource;
            }
            else
            {
                type = settings.constructionPickerType;
                subcategory = settings.constructionPickerSubcategory;
                source = settings.constructionPickerSource;
            }
            type = type.NullOrEmpty() ? CommissionPickerKeys.All : type;
            subcategory = subcategory.NullOrEmpty() ? CommissionPickerKeys.All : subcategory;
            source = source.NullOrEmpty() ? CommissionPickerKeys.All : source.ToLowerInvariant();
        }

        public static void Save(CommissionPickerKind kind, string type, string subcategory, string source)
        {
            ModSettings settings = ModSettings.Current;
            if (kind == CommissionPickerKind.Crafting)
            {
                settings.craftingPickerType = type;
                settings.craftingPickerSubcategory = subcategory;
                settings.craftingPickerSource = source;
            }
            else
            {
                settings.constructionPickerType = type;
                settings.constructionPickerSubcategory = subcategory;
                settings.constructionPickerSource = source;
            }
            LoadedModManager.GetMod<Mod>().WriteSettings();
        }
    }

    internal sealed class CommissionPickerEntry<T> where T : class
    {
        public T Item;
        public string TypeKey;
        public string SourceKey;
        public string SourceName;
        public string PackageId;
        public bool Official;
        public HashSet<string> Categories;
        public string SearchText;
        public string RecipeSource;

        public static CommissionPickerEntry<T> Create(T item, ThingDef subject, bool official, string typeKey, bool weaponSubtypes, Def recipeDef, params string[] searchParts)
        {
            ModContentPack pack = subject?.modContentPack;
            var entry = new CommissionPickerEntry<T>
            {
                Item = item,
                TypeKey = typeKey,
                Official = official,
                SourceKey = pack != null ? pack.PackageId.ToLowerInvariant() : CommissionPickerKeys.Unknown,
                SourceName = pack != null ? pack.Name : (string)"SettlementServices.Label.CommissionSourceUnknown".Translate(),
                PackageId = pack?.PackageId,
                Categories = new HashSet<string>(),
            };

            bool categorised = false;
            if (subject?.thingCategories != null)
            {
                foreach (ThingCategoryDef category in subject.thingCategories)
                {
                    if (category == null || category.parent == null) continue;
                    categorised = true;
                    entry.Categories.Add(category.defName);
                    foreach (ThingCategoryDef ancestor in category.Parents)
                        if (ancestor.parent != null) entry.Categories.Add(ancestor.defName);
                }
            }
            if (!categorised) entry.Categories.Add(CommissionPickerKeys.None);
            if (weaponSubtypes && subject != null && subject.IsWeapon)
                entry.Categories.Add(subject.IsRangedWeapon ? CommissionPickerKeys.Ranged : CommissionPickerKeys.Melee);

            if (recipeDef != null)
            {
                ModContentPack recipePack = recipeDef.modContentPack;
                if (!string.Equals(recipePack?.PackageId, pack?.PackageId, StringComparison.OrdinalIgnoreCase))
                    entry.RecipeSource = recipePack != null ? recipePack.Name : (string)"SettlementServices.Label.CommissionSourceUnknown".Translate();
            }

            entry.SearchText = string.Join("\n", searchParts.Where(p => !p.NullOrEmpty()).Concat(new[] { entry.SourceName })).ToLowerInvariant();
            return entry;
        }
    }

    internal sealed class CommissionPickerOption
    {
        public string Key;
        public string Label;
        public string Tooltip;
        public int Count;
    }

    internal sealed class CommissionPickerController<T> where T : class
    {
        private const float RowHeight = 28f;
        private const float RowGap = 4f;
        public const float ControlsHeight = RowHeight * 3f + RowGap * 3f + 22f;

        private readonly CommissionPickerKind kind;
        private readonly List<CommissionPickerEntry<T>> entries;
        private readonly Dictionary<T, CommissionPickerEntry<T>> byItem = new Dictionary<T, CommissionPickerEntry<T>>();
        private readonly Func<string, string> typeLabel;
        private readonly List<CommissionPickerOption> typeOptions;
        private readonly List<CommissionPickerOption> sourceOptions;
        private readonly Dictionary<string, List<CommissionPickerOption>> subOptions = new Dictionary<string, List<CommissionPickerOption>>();
        private readonly Dictionary<string, string> truncationCache = new Dictionary<string, string>();

        private readonly string labelSearch = "SettlementServices.Label.CommissionSearchLabel".Translate();
        private readonly string labelClear = "SettlementServices.Button.CommissionSearchClear".Translate();
        private readonly string labelType = "SettlementServices.Label.CommissionFilterType".Translate();
        private readonly string labelSub = "SettlementServices.Label.CommissionFilterSubcategory".Translate();
        private readonly string labelMod = "SettlementServices.Label.CommissionFilterMod".Translate();
        private readonly string labelReset = "SettlementServices.Button.CommissionResetFilters".Translate();
        private readonly string labelAllTypes = "SettlementServices.Label.CommissionAllTypes".Translate();
        private readonly string labelAllSubs = "SettlementServices.Label.CommissionAllSubcategories".Translate();
        private readonly string labelAllMods = "SettlementServices.Label.CommissionAllMods".Translate();
        private readonly string labelOfficial = "SettlementServices.Label.CommissionSourceOfficial".Translate();
        private readonly string labelUnknown = "SettlementServices.Label.CommissionSourceUnknown".Translate();

        private Func<T, bool> availabilityPredicate;
        private CommissionRowScan<T> scan;
        private HashSet<T> matchedSet = new HashSet<T>();
        private bool dirty = true;
        private string searchText = string.Empty;
        private string searchNeedle = string.Empty;
        private string typeButtonLabel;
        private string subButtonLabel;
        private string sourceButtonLabel;
        private string activeSummary;
        private float labelWidthLeft = -1f;
        private float labelWidthRight;
        private float lastButtonWidth = -1f;
        private string countText;
        private int countVisible;
        private int countEvaluated;
        private int countTotal;

        public CommissionPickerController(CommissionPickerKind kind, List<CommissionPickerEntry<T>> entries, Func<string, string> typeLabel, IList<string> typeOrder, Func<T, bool> availabilityPredicate)
        {
            this.kind = kind;
            this.entries = entries;
            this.typeLabel = typeLabel;
            this.availabilityPredicate = availabilityPredicate;
            foreach (CommissionPickerEntry<T> entry in entries) byItem[entry.Item] = entry;

            typeOptions = BuildTypeOptions(typeOrder);
            sourceOptions = BuildSourceOptions();

            CommissionPickerPrefs.Load(kind, out string type, out string sub, out string source);
            TypeKey = type;
            SubKey = sub;
            SourceKey = source;
            if (Validate()) Persist();
            RefreshLabels();
            Rebuild();
        }

        public string TypeKey { get; private set; }
        public string SubKey { get; private set; }
        public string SourceKey { get; private set; }
        public int Version { get; private set; }
        public int TotalEligible => entries.Count;
        public List<T> Visible => scan.Visible;
        public bool ScanComplete => scan.Complete;
        public bool AnyFilterActive => TypeKey != CommissionPickerKeys.All || SubKey != CommissionPickerKeys.All || SourceKey != CommissionPickerKeys.All;
        public bool SourceFilterActive => SourceKey != CommissionPickerKeys.All;
        public bool HasSearch => searchText.Length > 0;

        public CommissionPickerEntry<T> EntryFor(T item) => item != null && byItem.TryGetValue(item, out CommissionPickerEntry<T> entry) ? entry : null;

        public bool IsMatched(T item) => item != null && matchedSet.Contains(item);

        public void OnLayout(double budgetMilliseconds)
        {
            if (dirty) Rebuild();
            scan.Step(budgetMilliseconds);
        }

        public void SetAvailabilityPredicate(Func<T, bool> predicate)
        {
            availabilityPredicate = predicate;
            dirty = true;
        }

        public void ResetFilters()
        {
            if (!AnyFilterActive) return;
            TypeKey = SubKey = SourceKey = CommissionPickerKeys.All;
            Commit();
        }

        public void ShowAllMods()
        {
            if (SourceKey == CommissionPickerKeys.All) return;
            SourceKey = CommissionPickerKeys.All;
            Commit();
        }

        public void ClearSearch() => SetSearch(string.Empty);

        private void SetSearch(string value)
        {
            if (value == searchText) return;
            searchText = value;
            searchNeedle = value.Trim().ToLowerInvariant();
            dirty = true;
        }

        private void SetType(string key)
        {
            if (key == TypeKey) return;
            TypeKey = key;
            SubKey = CommissionPickerKeys.All;
            Commit();
        }

        private void SetSub(string key)
        {
            if (key == SubKey) return;
            SubKey = key;
            Commit();
        }

        private void SetSource(string key)
        {
            if (key == SourceKey) return;
            SourceKey = key;
            Commit();
        }

        private void Commit()
        {
            Persist();
            RefreshLabels();
            dirty = true;
        }

        private void Persist() => CommissionPickerPrefs.Save(kind, TypeKey, SubKey, SourceKey);

        private bool Validate()
        {
            bool repaired = false;

            if (SourceKey != CommissionPickerKeys.All && SourceKey != CommissionPickerKeys.Official)
            {
                bool exists = SourceKey == CommissionPickerKeys.Unknown
                    ? entries.Any(e => e.SourceKey == CommissionPickerKeys.Unknown)
                    : LoadedModManager.RunningModsListForReading.Any(m => string.Equals(m.PackageId, SourceKey, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                {
                    SourceKey = CommissionPickerKeys.All;
                    repaired = true;
                }
            }

            if (!TypeKeyValid(TypeKey))
            {
                TypeKey = CommissionPickerKeys.All;
                SubKey = CommissionPickerKeys.All;
                repaired = true;
            }

            if (!SubKeyValid(TypeKey, SubKey))
            {
                SubKey = CommissionPickerKeys.All;
                repaired = true;
            }

            return repaired;
        }

        private bool TypeKeyValid(string key)
        {
            if (key == CommissionPickerKeys.All) return true;
            if (kind == CommissionPickerKind.Crafting)
                return key == CommissionPickerKeys.Weapons || key == CommissionPickerKeys.Apparel || key == CommissionPickerKeys.Other;
            return key == CommissionPickerKeys.None || DefDatabase<DesignationCategoryDef>.GetNamedSilentFail(key) != null;
        }

        private bool SubKeyValid(string type, string sub)
        {
            if (sub == CommissionPickerKeys.All || sub == CommissionPickerKeys.None) return true;
            if (sub == CommissionPickerKeys.Ranged || sub == CommissionPickerKeys.Melee)
                return kind == CommissionPickerKind.Crafting && type == CommissionPickerKeys.Weapons;
            return DefDatabase<ThingCategoryDef>.GetNamedSilentFail(sub) != null;
        }

        private bool Matches(CommissionPickerEntry<T> entry)
        {
            if (TypeKey != CommissionPickerKeys.All && entry.TypeKey != TypeKey) return false;
            if (SubKey != CommissionPickerKeys.All && !entry.Categories.Contains(SubKey)) return false;
            if (SourceKey == CommissionPickerKeys.Official)
            {
                if (!entry.Official) return false;
            }
            else if (SourceKey != CommissionPickerKeys.All && entry.SourceKey != SourceKey)
            {
                return false;
            }
            if (searchNeedle.Length > 0 && entry.SearchText.IndexOf(searchNeedle, StringComparison.Ordinal) < 0) return false;
            return true;
        }

        private void Rebuild()
        {
            var matched = new List<T>();
            foreach (CommissionPickerEntry<T> entry in entries)
                if (Matches(entry)) matched.Add(entry.Item);
            matchedSet = new HashSet<T>(matched);
            scan = new CommissionRowScan<T>(matched, availabilityPredicate);
            Version++;
            dirty = false;
        }

        private List<CommissionPickerOption> BuildTypeOptions(IList<string> typeOrder)
        {
            var counts = new Dictionary<string, int>();
            foreach (CommissionPickerEntry<T> entry in entries)
            {
                counts.TryGetValue(entry.TypeKey, out int count);
                counts[entry.TypeKey] = count + 1;
            }
            IEnumerable<KeyValuePair<string, int>> ordered = typeOrder != null
                ? counts.OrderBy(kv => { int index = typeOrder.IndexOf(kv.Key); return index < 0 ? int.MaxValue : index; })
                : counts.OrderBy(kv => typeLabel(kv.Key), StringComparer.CurrentCultureIgnoreCase);
            return ordered.Select(kv => new CommissionPickerOption { Key = kv.Key, Label = typeLabel(kv.Key), Count = kv.Value }).ToList();
        }

        private List<CommissionPickerOption> BuildSourceOptions()
        {
            var options = new List<CommissionPickerOption>();
            int official = entries.Count(e => e.Official);
            if (official > 0)
                options.Add(new CommissionPickerOption { Key = CommissionPickerKeys.Official, Label = labelOfficial, Count = official, Tooltip = "SettlementServices.Label.CommissionSourceOfficialTip".Translate() });
            int unknown = entries.Count(e => e.SourceKey == CommissionPickerKeys.Unknown);
            if (unknown > 0)
                options.Add(new CommissionPickerOption { Key = CommissionPickerKeys.Unknown, Label = labelUnknown, Count = unknown });

            var named = entries
                .Where(e => e.SourceKey != CommissionPickerKeys.Unknown)
                .GroupBy(e => e.SourceKey)
                .Select(g => new { Key = g.Key, Name = g.First().SourceName, PackageId = g.First().PackageId, Count = g.Count() })
                .ToList();
            var duplicateNames = new HashSet<string>(named.GroupBy(n => n.Name).Where(g => g.Count() > 1).Select(g => g.Key));
            foreach (var n in named.OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(n => n.PackageId, StringComparer.OrdinalIgnoreCase))
            {
                options.Add(new CommissionPickerOption
                {
                    Key = n.Key,
                    Label = duplicateNames.Contains(n.Name) ? $"{n.Name} ({n.PackageId})" : n.Name,
                    Tooltip = "SettlementServices.Label.CommissionSourcePackage".Translate(n.PackageId),
                    Count = n.Count,
                });
            }
            return options;
        }

        private List<CommissionPickerOption> GetSubOptions(string typeKey)
        {
            if (subOptions.TryGetValue(typeKey, out List<CommissionPickerOption> cached)) return cached;

            var counts = new Dictionary<string, int>();
            int total = 0;
            foreach (CommissionPickerEntry<T> entry in entries)
            {
                if (typeKey != CommissionPickerKeys.All && entry.TypeKey != typeKey) continue;
                total++;
                foreach (string category in entry.Categories)
                {
                    counts.TryGetValue(category, out int count);
                    counts[category] = count + 1;
                }
            }

            var options = new List<CommissionPickerOption>();
            foreach (string special in new[] { CommissionPickerKeys.Ranged, CommissionPickerKeys.Melee })
                if (counts.TryGetValue(special, out int specialCount))
                    options.Add(new CommissionPickerOption { Key = special, Label = SubLabelFor(special), Count = specialCount });

            var categories = new List<(ThingCategoryDef def, string path, int depth, int count)>();
            foreach (KeyValuePair<string, int> kv in counts)
            {
                if (kv.Key.StartsWith("@")) continue;
                ThingCategoryDef def = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(kv.Key);
                if (def == null || (kv.Value == total && total > 0)) continue;
                List<ThingCategoryDef> chain = def.Parents.Where(p => p.parent != null).Reverse().ToList();
                chain.Add(def);
                categories.Add((def, string.Join("\u0001", chain.Select(CategoryLabel)), chain.Count - 1, kv.Value));
            }
            int minDepth = categories.Count > 0 ? categories.Min(c => c.depth) : 0;
            foreach (var c in categories.OrderBy(c => c.path, StringComparer.CurrentCultureIgnoreCase))
                options.Add(new CommissionPickerOption { Key = c.def.defName, Label = new string(' ', (c.depth - minDepth) * 2) + CategoryLabel(c.def), Count = c.count });

            if (counts.TryGetValue(CommissionPickerKeys.None, out int noneCount))
                options.Add(new CommissionPickerOption { Key = CommissionPickerKeys.None, Label = SubLabelFor(CommissionPickerKeys.None), Count = noneCount });

            subOptions[typeKey] = options;
            return options;
        }

        private static string CategoryLabel(ThingCategoryDef def) => def.label.NullOrEmpty() ? def.defName : def.LabelCap.ToString();

        private string SubLabelFor(string key)
        {
            switch (key)
            {
                case CommissionPickerKeys.All: return labelAllSubs;
                case CommissionPickerKeys.Ranged: return "SettlementServices.Label.CommissionSubRanged".Translate();
                case CommissionPickerKeys.Melee: return "SettlementServices.Label.CommissionSubMelee".Translate();
                case CommissionPickerKeys.None: return "SettlementServices.Label.CommissionSubUncategorised".Translate();
            }
            ThingCategoryDef def = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(key);
            return def != null ? CategoryLabel(def) : key;
        }

        private string SourceLabelFor(string key)
        {
            if (key == CommissionPickerKeys.All) return labelAllMods;
            if (key == CommissionPickerKeys.Official) return labelOfficial;
            if (key == CommissionPickerKeys.Unknown) return labelUnknown;
            CommissionPickerOption option = sourceOptions.Find(o => o.Key == key);
            if (option != null) return option.Label;
            ModContentPack pack = LoadedModManager.RunningModsListForReading.Find(m => string.Equals(m.PackageId, key, StringComparison.OrdinalIgnoreCase));
            return pack != null ? pack.Name : key;
        }

        private void RefreshLabels()
        {
            typeButtonLabel = TypeKey == CommissionPickerKeys.All ? labelAllTypes : typeLabel(TypeKey);
            subButtonLabel = SubLabelFor(SubKey);
            sourceButtonLabel = SourceLabelFor(SourceKey);
            truncationCache.Clear();

            if (!AnyFilterActive)
            {
                activeSummary = null;
                return;
            }
            var parts = new List<string>();
            if (TypeKey != CommissionPickerKeys.All) parts.Add(typeButtonLabel);
            if (SubKey != CommissionPickerKeys.All) parts.Add(subButtonLabel);
            if (SourceKey != CommissionPickerKeys.All) parts.Add(sourceButtonLabel);
            activeSummary = "SettlementServices.Label.CommissionActiveFilters".Translate(string.Join(" / ", parts));
        }

        public void DrawControls(Rect rect)
        {
            if (labelWidthLeft < 0f)
            {
                labelWidthLeft = Mathf.Max(Text.CalcSize(labelType).x, Text.CalcSize(labelMod).x) + 6f;
                labelWidthRight = Text.CalcSize(labelSub).x + 6f;
            }

            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;

            float y = rect.y;
            float searchLabelWidth = Text.CalcSize(labelSearch).x + 6f;
            float clearWidth = Mathf.Max(60f, Text.CalcSize(labelClear).x + 20f);
            Widgets.Label(new Rect(rect.x, y, searchLabelWidth, RowHeight), labelSearch);
            Rect clearRect = new Rect(rect.xMax - clearWidth, y, clearWidth, RowHeight);
            Rect searchRect = new Rect(rect.x + searchLabelWidth, y, clearRect.x - rect.x - searchLabelWidth - 4f, RowHeight);
            SetSearch(Widgets.TextField(searchRect, searchText));
            Text.Anchor = previousAnchor;
            if (Widgets.ButtonText(clearRect, labelClear))
            {
                ClearSearch();
                GUI.FocusControl(null);
            }

            float half = (rect.width - 12f) / 2f;
            float rightX = rect.x + half + 12f;

            y += RowHeight + RowGap;
            DrawSelector(new Rect(rect.x, y, half, RowHeight), labelType, labelWidthLeft, typeButtonLabel, null, OpenTypeMenu);
            DrawSelector(new Rect(rightX, y, half, RowHeight), labelSub, labelWidthRight, subButtonLabel, null, OpenSubMenu);

            y += RowHeight + RowGap;
            string sourceTip = SourceTooltip();
            DrawSelector(new Rect(rect.x, y, half, RowHeight), labelMod, labelWidthLeft, sourceButtonLabel, sourceTip, OpenSourceMenu);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = AnyFilterActive;
            if (Widgets.ButtonText(new Rect(rightX, y, half, RowHeight), labelReset)) ResetFilters();
            GUI.enabled = previousEnabled;

            y += RowHeight + RowGap;
            Rect countRect = new Rect(rect.x, y, rect.width, 22f);
            bool wordWrap = Text.WordWrap;
            Text.WordWrap = false;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.gray;
            string count = CountText();
            Widgets.Label(countRect, activeSummary != null ? count + "  -  " + activeSummary : count);
            GUI.color = Color.white;
            if (activeSummary != null) TooltipHandler.TipRegion(countRect, activeSummary);
            Text.Anchor = previousAnchor;
            Text.WordWrap = wordWrap;
        }

        private string CountText()
        {
            int visible = scan.Visible.Count;
            int evaluated = scan.Evaluated;
            int total = scan.Total;
            if (countText == null || countVisible != visible || countEvaluated != evaluated || countTotal != total)
            {
                countVisible = visible;
                countEvaluated = evaluated;
                countTotal = total;
                countText = scan.Complete
                    ? "SettlementServices.Label.CommissionShowing".Translate(visible, entries.Count).Resolve()
                    : "SettlementServices.Label.CommissionShowingPartial".Translate(visible, evaluated, total).Resolve();
            }
            return countText;
        }

        private string SourceTooltip()
        {
            if (SourceKey == CommissionPickerKeys.Official) return "SettlementServices.Label.CommissionSourceOfficialTip".Translate();
            CommissionPickerOption option = sourceOptions.Find(o => o.Key == SourceKey);
            return option?.Tooltip;
        }

        private void DrawSelector(Rect rect, string label, float labelWidth, string buttonLabel, string tooltip, Action openMenu)
        {
            Widgets.Label(new Rect(rect.x, rect.y, labelWidth, rect.height), label);
            Rect buttonRect = new Rect(rect.x + labelWidth, rect.y, rect.width - labelWidth, rect.height);
            if (!Mathf.Approximately(lastButtonWidth, buttonRect.width))
            {
                lastButtonWidth = buttonRect.width;
                truncationCache.Clear();
            }
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonText(buttonRect, buttonLabel.Truncate(buttonRect.width - 28f, truncationCache))) openMenu();
            Text.Anchor = TextAnchor.MiddleLeft;
            TooltipHandler.TipRegion(buttonRect, tooltip.NullOrEmpty() ? buttonLabel : buttonLabel + "\n" + tooltip);
        }

        private void OpenTypeMenu() => OpenMenu(typeOptions, labelAllTypes, SetType);

        private void OpenSubMenu() => OpenMenu(GetSubOptions(TypeKey), labelAllSubs, SetSub);

        private void OpenSourceMenu() => OpenMenu(sourceOptions, labelAllMods, SetSource);

        private void OpenMenu(List<CommissionPickerOption> options, string allLabel, Action<string> choose)
        {
            var menu = new List<FloatMenuOption> { new FloatMenuOption(allLabel, () => choose(CommissionPickerKeys.All)) };
            foreach (CommissionPickerOption option in options)
            {
                string key = option.Key;
                var floatOption = new FloatMenuOption($"{option.Label} ({option.Count})", () => choose(key));
                if (!option.Tooltip.NullOrEmpty()) floatOption.tooltip = new TipSignal(option.Tooltip);
                menu.Add(floatOption);
            }
            Find.WindowStack.Add(new FloatMenu(menu));
        }
    }
}

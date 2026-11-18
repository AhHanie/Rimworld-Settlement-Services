using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Services.Stashing;

namespace Settlement_Services.UI
{
    public class Dialog_SettlementStash : Window
    {
        private sealed class CountInput { public int count; public string buffer; }
        private sealed class InventoryGroup
        {
            public readonly List<Thing> stacks = new List<Thing>();
            public Thing Sample => stacks[0];
            public long Available => stacks.Sum(t => (long)t.stackCount);
        }

        private readonly Settlement settlement;
        private readonly Caravan caravan;
        private readonly Dictionary<Thing, CountInput> counts = new Dictionary<Thing, CountInput>();
        private readonly Dictionary<int, CountInput> extensionDays = new Dictionary<int, CountInput>();
        private int days = 10;
        private string daysBuffer = "10";
        private bool showActive;
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(900f, 760f);

        public Dialog_SettlementStash(Settlement settlement, Caravan caravan)
        {
            this.settlement = settlement;
            this.caravan = caravan;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null) return;
            domain.ExpireDueStashes();
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "SettlementServices.Stash.Title".Translate());
            Text.Font = GameFont.Small;
            if (!showActive) Widgets.DrawOptionBackground(new Rect(0f, 38f, 160f, 30f), true);
            if (Widgets.ButtonText(new Rect(0f, 38f, 160f, 30f), "SettlementServices.Stash.DepositTab".Translate()))
            { showActive = false; scroll = Vector2.zero; }
            if (showActive) Widgets.DrawOptionBackground(new Rect(168f, 38f, 180f, 30f), true);
            int activeCount = domain.StashesForSettlement(settlement.ID).Count;
            if (Widgets.ButtonText(new Rect(168f, 38f, 180f, 30f), "SettlementServices.Stash.ActiveTabCount".Translate(activeCount)))
            { showActive = true; scroll = Vector2.zero; }
            string block = StashService.VisitBlock(settlement, caravan);
            if (block != null)
                Widgets.Label(new Rect(0f, 73f, inRect.width, 28f), block.Translate());
            else
                Widgets.Label(new Rect(0f, 73f, inRect.width, 28f), "SettlementServices.Stash.InPersonShort".Translate());
            Rect content = new Rect(0f, 106f, inRect.width, inRect.height - 106f);
            if (showActive) DrawActive(content, domain, block);
            else DrawDeposit(content, domain, block);
        }

        private void DrawDeposit(Rect rect, SettlementServicesWorldComponent domain, string block)
        {
            List<Thing> items = StashService.EligibleInventory(caravan);
            List<InventoryGroup> groups = BuildInventoryGroups(items);
            foreach (Thing stale in counts.Keys.Where(t => groups.All(g => g.Sample != t)).ToList()) counts.Remove(stale);

            Rect listPanel = new Rect(rect.x, rect.y, rect.width, rect.height - 258f);
            Widgets.DrawBoxSolid(listPanel, new Color(0.16f, 0.18f, 0.20f, 0.62f));
            Widgets.Label(new Rect(listPanel.x + 10f, listPanel.y + 4f, listPanel.width - 300f, 25f),
                "SettlementServices.Stash.ChooseItems".Translate());
            Widgets.Label(new Rect(listPanel.xMax - 267f, listPanel.y + 4f, 90f, 25f),
                "SettlementServices.Stash.Available".Translate());
            Widgets.Label(new Rect(listPanel.xMax - 166f, listPanel.y + 4f, 90f, 25f),
                "SettlementServices.Stash.Store".Translate());
            Rect listRect = new Rect(listPanel.x + 4f, listPanel.y + 32f, listPanel.width - 8f, listPanel.height - 36f);
            int rowCount = groups.Count;
            Rect view = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(listRect.height, rowCount * 49f));
            Widgets.BeginScrollView(listRect, ref scroll, view);
            float y = 0f;
            var totalsByName = groups.GroupBy(g => g.Sample.LabelCapNoCount)
                .ToDictionary(g => g.Key, g => g.Count());
            var seenByName = new Dictionary<string, int>();
            foreach (InventoryGroup group in groups)
            {
                Thing item = group.Sample;
                if (!counts.TryGetValue(item, out CountInput input)) counts[item] = input = new CountInput { buffer = "0" };
                int maximum = (int)Math.Min(int.MaxValue, group.Available);
                if (input.count > maximum) SetCount(input, maximum);
                string name = item.LabelCapNoCount;
                int ordinal = seenByName.TryGetValue(name, out int previous) ? previous + 1 : 1;
                seenByName[name] = ordinal;
                string subtitle = group.stacks.Count > 1
                    ? "SettlementServices.Stash.GroupedStacks".Translate(group.stacks.Count)
                    : Carrier(item);
                if (totalsByName[name] > 1)
                    subtitle += "  •  " + "SettlementServices.Stash.StackNumber".Translate(ordinal, totalsByName[name]);
                DrawInventoryRow(new Rect(0f, y, view.width, 46f), item, subtitle,
                    group.Available, input, maximum,
                    group.stacks.Count > 1 ? "SettlementServices.Stash.GroupedTooltip".Translate(group.stacks.Count) : null);
                y += 49f;
            }
            Widgets.EndScrollView();

            var selected = new List<StashSelection>();
            foreach (InventoryGroup group in groups)
            {
                int remaining = counts[group.Sample].count;
                foreach (Thing stack in group.stacks)
                {
                    if (remaining <= 0) break;
                    int take = Math.Min(remaining, stack.stackCount);
                    selected.Add(new StashSelection(stack, take));
                    remaining -= take;
                }
            }

            Rect summaryRect = new Rect(rect.x, listPanel.yMax + 8f, rect.width, 212f);
            Widgets.DrawBoxSolid(summaryRect, new Color(0.19f, 0.21f, 0.23f, 0.72f));
            Widgets.TextFieldNumeric(new Rect(summaryRect.x + 140f, summaryRect.y + 7f, 70f, 29f),
                ref days, ref daysBuffer, 1, StashPricing.MaximumPurchaseDays);
            bool quoted = StashPricing.TryQuote(selected, days, true, out StashQuote quote);
            long paymentSilver = CaravanInventoryUtility.AllInventoryItems(caravan)
                .Where(t => t.def == ThingDefOf.Silver && !domain.IsTargetReserved(t)).Sum(t => (long)t.stackCount)
                - selected.Where(s => s.thing.def == ThingDefOf.Silver).Sum(s => (long)s.count);
            DrawDepositSummary(summaryRect, selected, quote, quoted, paymentSilver);
            bool canDeposit = block == null && quoted && paymentSilver >= quote.total;
            bool wasEnabled = GUI.enabled;
            GUI.enabled = canDeposit;
            bool clicked = Widgets.ButtonText(new Rect(rect.xMax - 190f, rect.yMax - 38f, 190f, 36f),
                "SettlementServices.Stash.Deposit".Translate());
            GUI.enabled = wasEnabled;
            if (clicked)
            {
                List<StashSelection> snapshot = selected.ToList();
                int purchasedDays = days;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "SettlementServices.Stash.ConfirmDeposit".Translate(quote.total, quote.days, Deadline(Find.TickManager.TicksGame + (long)purchasedDays * GenDate.TicksPerDay)),
                    () =>
                    {
                        if (StashService.TryDeposit(settlement, caravan, snapshot, purchasedDays, out StashContractRecord created, out string error))
                        {
                            counts.Clear();
                            showActive = true;
                            scroll = Vector2.zero;
                            Messages.Message("SettlementServices.Stash.Deposited".Translate(created.stashId), MessageTypeDefOf.PositiveEvent, false);
                        }
                        else Reject(error);
                    }, destructive: false));
            }
        }

        private static List<InventoryGroup> BuildInventoryGroups(List<Thing> items)
        {
            var groups = new List<InventoryGroup>();
            foreach (Thing item in items)
            {
                InventoryGroup group = item.def.stackLimit > 1
                    ? groups.FirstOrDefault(g => g.Sample.def.stackLimit > 1
                        && g.Sample.LabelCapNoCount == item.LabelCapNoCount
                        && g.stacks.All(other => other.CanStackWith(item) && item.CanStackWith(other)))
                    : null;
                if (group == null)
                {
                    group = new InventoryGroup();
                    groups.Add(group);
                }
                group.stacks.Add(item);
            }
            return groups.OrderBy(g => g.Sample.LabelCapNoCount).ThenBy(g => g.Sample.thingIDNumber).ToList();
        }

        private void DrawInventoryRow(Rect row, Thing item, string subtitle, long available,
            CountInput input, int maximum, string extraTooltip)
        {
            if (input.count > maximum) SetCount(input, maximum);
            if (input.count > 0)
                Widgets.DrawBoxSolid(row, new Color(0.26f, 0.32f, 0.38f, 0.62f));
            Widgets.DrawHighlightIfMouseover(row);
            bool selected = input.count > 0;
            Widgets.Checkbox(row.x + 7f, row.y + 11f, ref selected, 24f);
            if (selected != (input.count > 0)) SetCount(input, selected ? maximum : 0);
            Widgets.ThingIcon(new Rect(row.x + 37f, row.y + 6f, 34f, 34f), item);
            Rect nameRect = new Rect(row.x + 79f, row.y + 2f, row.width - 355f, 22f);
            Widgets.Label(nameRect, item.LabelCapNoCount.Truncate(nameRect.width));
            Color oldColor = GUI.color;
            GUI.color = Color.gray;
            Rect subtitleRect = new Rect(row.x + 79f, row.y + 23f, row.width - 355f, 18f);
            Widgets.Label(subtitleRect, subtitle.Truncate(subtitleRect.width));
            GUI.color = oldColor;
            Widgets.Label(new Rect(row.xMax - 267f, row.y + 11f, 90f, 24f), available.ToString());
            Widgets.TextFieldNumeric(new Rect(row.xMax - 166f, row.y + 8f, 90f, 29f),
                ref input.count, ref input.buffer, 0, maximum);
            if (Widgets.ButtonText(new Rect(row.xMax - 64f, row.y + 8f, 56f, 29f),
                "SettlementServices.Stash.All".Translate()))
                SetCount(input, maximum);
            string tooltip = item.LabelCap;
            if (!extraTooltip.NullOrEmpty()) tooltip += "\n\n" + extraTooltip;
            TooltipHandler.TipRegion(new Rect(row.x, row.y, row.width - 180f, row.height), tooltip);
        }

        private void DrawDepositSummary(Rect rect, List<StashSelection> selected, StashQuote quote,
            bool quoted, long paymentSilver)
        {
            float x = rect.x + 10f;
            float y = rect.y + 7f;
            Widgets.Label(new Rect(x, y + 3f, 130f, 25f), "SettlementServices.Stash.Term".Translate());
            if (!quoted)
            {
                Widgets.Label(new Rect(x + 220f, y + 3f, rect.width - 240f, 25f),
                    selected.Count == 0 ? "SettlementServices.Stash.SelectItems".Translate()
                        : "SettlementServices.Stash.Error.Price".Translate());
                Widgets.Label(new Rect(x, y + 48f, rect.width - 20f, 45f),
                    "SettlementServices.Stash.Preservation".Translate());
                return;
            }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(x + 235f, y - 2f, rect.width - 255f, 34f),
                "SettlementServices.Stash.TotalDue".Translate(quote.total));
            Text.Font = GameFont.Small;
            y += 38f;
            Widgets.Label(new Rect(x, y, rect.width - 20f, 24f),
                "SettlementServices.Stash.FeeBreakdown".Translate(quote.dailyFee, quote.days, quote.handlingFee));
            y += 25f;
            Widgets.Label(new Rect(x, y, rect.width - 20f, 24f),
                "SettlementServices.Stash.GoodsSummary".Translate(quote.value.ToString("0.##"), quote.mass.ToString("0.##")));
            y += 25f;
            bool canPay = paymentSilver >= quote.total;
            Color oldColor = GUI.color;
            if (!canPay) GUI.color = ColorLibrary.RedReadable;
            Widgets.Label(new Rect(x, y, rect.width - 20f, 24f),
                "SettlementServices.Stash.PaymentBalance".Translate(paymentSilver, quote.total));
            GUI.color = oldColor;
            y += 25f;
            Widgets.Label(new Rect(x, y, rect.width - 20f, 24f),
                "SettlementServices.Stash.Deadline".Translate(Deadline(Find.TickManager.TicksGame + (long)days * GenDate.TicksPerDay)));
            y += 25f;
            float feeSilverMass = ThingDefOf.Silver.GetStatValueAbstract(StatDefOf.Mass) * quote.total;
            float projectedMass = Math.Max(0f, caravan.MassUsage - (float)quote.mass - feeSilverMass);
            if (projectedMass > caravan.MassCapacity) GUI.color = ColorLibrary.RedReadable;
            Widgets.Label(new Rect(x, y, rect.width - 20f, 24f),
                "SettlementServices.Stash.DepositCapacity".Translate(projectedMass.ToString("F1"), caravan.MassCapacity.ToString("F1")));
            GUI.color = oldColor;
            y += 27f;
            Widgets.Label(new Rect(x, y, rect.width - 20f, 24f), "SettlementServices.Stash.PreservationShort".Translate());
        }

        private string Carrier(Thing item)
        {
            Pawn pawn = caravan.PawnsListForReading.FirstOrDefault(p => p.inventory?.innerContainer?.Contains(item) == true);
            return pawn == null ? "" : "SettlementServices.Stash.CarriedBy".Translate(pawn.LabelShortCap);
        }

        private static void SetCount(CountInput input, int count)
        {
            input.count = count;
            input.buffer = count.ToString();
        }

        private void DrawActive(Rect rect, SettlementServicesWorldComponent domain, string block)
        {
            List<StashContractRecord> stashes = domain.StashesForSettlement(settlement.ID);
            if (stashes.Count == 0)
            { Widgets.NoneLabelCenteredVertically(rect, "SettlementServices.Stash.None".Translate()); return; }
            var cards = stashes.Select(stash => new
            {
                stash,
                groups = BuildInventoryGroups(stash.items.Where(t => t != null && !t.Destroyed).ToList())
            }).ToList();
            float totalHeight = cards.Sum(card => ActiveCardHeight(card.groups.Count));
            Rect view = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, totalHeight));
            Widgets.BeginScrollView(rect, ref scroll, view);
            float y = 0f;
            foreach (var card in cards)
            {
                StashContractRecord stash = card.stash;
                List<InventoryGroup> groups = card.groups;
                float height = ActiveCardHeight(groups.Count);
                Widgets.DrawBoxSolid(new Rect(0f, y, view.width, height - 8f), new Color(0.18f, 0.20f, 0.22f, 0.72f));
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(10f, y + 5f, view.width - 20f, 29f),
                    "SettlementServices.Stash.CardTitle".Translate(stash.stashId,
                        (stash.expiryTick - Find.TickManager.TicksGame).ToStringTicksToPeriod()));
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(10f, y + 34f, view.width - 20f, 22f),
                    "SettlementServices.Stash.Deadline".Translate(Deadline(stash.expiryTick)));
                int contentRows = Math.Max(1, (groups.Count + 1) / 2);
                float cellWidth = (view.width - 30f) / 2f;
                for (int i = 0; i < groups.Count; i++)
                {
                    InventoryGroup group = groups[i];
                    Thing item = group.Sample;
                    float cellX = 10f + (i % 2) * (cellWidth + 10f);
                    float cellY = y + 60f + (i / 2) * 31f;
                    Widgets.ThingIcon(new Rect(cellX, cellY, 27f, 27f), item);
                    Rect nameRect = new Rect(cellX + 34f, cellY + 3f, cellWidth - 38f, 24f);
                    Widgets.Label(nameRect, (item.LabelCapNoCount + " ×" + group.Available).Truncate(nameRect.width));
                    TooltipHandler.TipRegion(new Rect(cellX, cellY, cellWidth, 28f),
                        group.stacks.Count > 1 ? "SettlementServices.Stash.GroupedHeldTooltip".Translate(group.stacks.Count) : item.LabelCap);
                }
                float detailY = y + 62f + contentRows * 31f;
                float collectionMass = caravan.MassUsage + stash.items.Where(t => t != null && !t.Destroyed)
                    .Sum(t => t.GetStatValue(StatDefOf.Mass) * t.stackCount);
                Color oldColor = GUI.color;
                if (collectionMass > caravan.MassCapacity) GUI.color = ColorLibrary.RedReadable;
                Widgets.Label(new Rect(10f, detailY, view.width - 20f, 22f),
                    "SettlementServices.Stash.CollectionCapacity".Translate(collectionMass.ToString("F1"), caravan.MassCapacity.ToString("F1")));
                GUI.color = oldColor;
                float actionY = detailY + 28f;
                if (!extensionDays.TryGetValue(stash.stashId, out CountInput input))
                    extensionDays[stash.stashId] = input = new CountInput { count = 10, buffer = "10" };
                Widgets.Label(new Rect(10f, actionY + 4f, 75f, 26f), "SettlementServices.Stash.AddDays".Translate());
                Widgets.TextFieldNumeric(new Rect(85f, actionY, 62f, 29f), ref input.count, ref input.buffer, 1, StashPricing.MaximumPurchaseDays);
                bool quoted = StashPricing.TryQuote(stash.items.Where(t => t != null).Select(t => new StashSelection(t, t.stackCount)),
                    input.count, false, out StashQuote quote);
                Widgets.Label(new Rect(155f, actionY + 4f, view.width - 490f, 25f), quoted ?
                    "SettlementServices.Stash.ExtensionOffer".Translate(quote.total) : "SettlementServices.Stash.Error.Price".Translate());
                if (Widgets.ButtonText(new Rect(view.width - 325f, actionY, 150f, 30f), "SettlementServices.Stash.Extend".Translate()))
                {
                    int id = stash.stashId, added = input.count;
                    if (block != null || !quoted) Reject(block ?? "SettlementServices.Stash.Error.Price");
                    else Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "SettlementServices.Stash.ConfirmExtension".Translate(added, quote.total),
                        () =>
                        {
                            if (StashService.TryExtend(settlement, caravan, id, added, out int charged, out string error))
                                Messages.Message("SettlementServices.Stash.Extended".Translate(id, charged), MessageTypeDefOf.PositiveEvent, false);
                            else Reject(error);
                        }));
                }
                if (Widgets.ButtonText(new Rect(view.width - 165f, actionY, 155f, 30f), "SettlementServices.Stash.Collect".Translate()))
                {
                    int id = stash.stashId;
                    if (block != null) Reject(block);
                    else Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "SettlementServices.Stash.ConfirmCollect".Translate(id,
                            collectionMass.ToString("F1"), caravan.MassCapacity.ToString("F1")),
                        () =>
                        {
                            if (StashService.TryCollect(settlement, caravan, id, out string error))
                                Messages.Message("SettlementServices.Stash.Collected".Translate(id, caravan.MassUsage.ToString("F1"), caravan.MassCapacity.ToString("F1")),
                                    MessageTypeDefOf.PositiveEvent, false);
                            else Reject(error);
                        }));
                }
                y += height;
            }
            Widgets.EndScrollView();
        }

        private static float ActiveCardHeight(int displayItemCount) =>
            132f + Math.Max(1, (displayItemCount + 1) / 2) * 31f;

        private string Deadline(long expiryTick)
        {
            long absolute = GenTicks.TicksAbs + (long)expiryTick - Find.TickManager.TicksGame;
            return GenDate.DateFullStringWithHourAt(absolute, Find.WorldGrid.LongLatOf(settlement.Tile));
        }

        private static void Reject(string key) =>
            Messages.Message((key ?? "SettlementServices.Stash.Error.Invalid").Translate(), MessageTypeDefOf.RejectInput, false);
    }
}

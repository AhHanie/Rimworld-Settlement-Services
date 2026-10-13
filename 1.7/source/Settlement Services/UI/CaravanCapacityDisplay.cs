using UnityEngine;
using RimWorld;
using Verse;

namespace Settlement_Services.UI
{
    public static class CaravanCapacityDisplay
    {
        public const float Height = 56f;
        public const float CompactHeight = 36f;

        private const float LineHeight = 20f;
        private const float BarHeight = 8f;
        private const float BarGap = 3f;

        private static readonly Color BarFillColor = new Color(0.35f, 0.62f, 0.4f);
        private static readonly Color BarBackColor = new Color(0.12f, 0.12f, 0.12f);

        public static void Draw(Rect rect, CaravanCapacityPreview preview)
        {
            if (preview == null) return;

            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            bool prevWrap = Text.WordWrap;
            Color prevColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;

            float shownUsed = preview.hasProjection ? preview.projectedUsed : preview.currentUsed;
            float shownCapacity = preview.hasProjection ? preview.projectedCapacity : preview.currentCapacity;
            bool shownOver = shownUsed > shownCapacity;

            float y = rect.y;
            DrawLine(new Rect(rect.x, y, rect.width, LineHeight), CurrentText(preview), preview.currentUsed > preview.currentCapacity && !preview.hasProjection);
            y += LineHeight;

            if (preview.hasProjection)
            {
                DrawLine(new Rect(rect.x, y, rect.width, LineHeight), ProjectionText(preview), shownOver);
            }

            DrawBar(new Rect(rect.x, rect.yMax - BarHeight - BarGap, rect.width, BarHeight), shownUsed, shownCapacity, shownOver);

            TooltipHandler.TipRegion(rect, "SettlementServices.Label.CaravanCapacityTooltip".Translate());

            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
            Text.WordWrap = prevWrap;
            GUI.color = prevColor;
        }

        private static void DrawLine(Rect rect, string text, bool over)
        {
            Color prevColor = GUI.color;
            if (over) GUI.color = ColorLibrary.RedReadable;
            Widgets.Label(rect, text.Truncate(rect.width));
            GUI.color = prevColor;
        }

        private static void DrawBar(Rect rect, float used, float capacity, bool over)
        {
            Widgets.DrawBoxSolid(rect, BarBackColor);
            float fill = capacity > 0f ? Mathf.Clamp01(used / capacity) : (used > 0f ? 1f : 0f);
            if (fill > 0f)
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width * fill, rect.height), over ? ColorLibrary.RedReadable : BarFillColor);
            GUI.color = Color.gray;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
        }

        private static string CurrentText(CaravanCapacityPreview preview)
        {
            string text = "SettlementServices.Label.CaravanCapacityCurrent".Translate(Format(preview.currentUsed, preview.currentCapacity)).Resolve();
            if (!preview.hasProjection && preview.currentUsed > preview.currentCapacity)
                text += " — " + OverText(preview.currentUsed, preview.currentCapacity);
            return text;
        }

        private static string ProjectionText(CaravanCapacityPreview preview)
        {
            string key = preview.projectionIsEstimate
                ? "SettlementServices.Label.CaravanCapacityEstimatedAfterPurchase"
                : "SettlementServices.Label.CaravanCapacityAfterPurchase";
            string text = key.Translate(Format(preview.projectedUsed, preview.projectedCapacity)).Resolve();
            if (preview.projectedUsed > preview.projectedCapacity)
                text += " — " + OverText(preview.projectedUsed, preview.projectedCapacity);
            return text;
        }

        private static string OverText(float used, float capacity) =>
            "SettlementServices.Label.CaravanCapacityOver".Translate((used - capacity).ToStringEnsureThreshold(0f, 1) + " " + "kg".Translate()).Resolve();

        private static string Format(float used, float capacity) =>
            "MassUsageString".Translate(used.ToStringEnsureThreshold(capacity, 1), capacity.ToString("F1")).Resolve();
    }
}

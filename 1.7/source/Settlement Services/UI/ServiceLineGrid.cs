using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Settlement_Services.UI
{
    internal readonly struct ServiceLineGridCell
    {
        public readonly string text;
        public readonly Def iconDef;

        public ServiceLineGridCell(string text, Def iconDef = null)
        {
            this.text = text;
            this.iconDef = iconDef;
        }
    }

    internal static class ServiceLineGrid
    {
        private const float IconSize = 24f;
        private const float IconGap = 4f;

        public static void Draw(Listing_Standard listing, IReadOnlyList<ServiceLineGridCell> cells, float minCellWidth, int maxColumns, float rowHeight)
        {
            if (cells == null || cells.Count == 0) return;

            float gap = Listing.ColumnSpacing;
            int columns = Mathf.Clamp(Mathf.FloorToInt((listing.ColumnWidth + gap) / (minCellWidth + gap)), 1, maxColumns);
            int rows = Mathf.CeilToInt(cells.Count / (float)columns);
            Rect grid = listing.GetRect(rows * rowHeight);
            float cellWidth = (grid.width - gap * (columns - 1)) / columns;

            TextAnchor prevAnchor = Text.Anchor;
            Color prevColor = GUI.color;
            Text.Anchor = TextAnchor.MiddleLeft;

            for (int i = 0; i < cells.Count; i++)
            {
                int column = i / rows;
                int row = i % rows;
                Rect cellRect = new Rect(grid.x + column * (cellWidth + gap), grid.y + row * rowHeight, cellWidth, rowHeight);
                ServiceLineGridCell cell = cells[i];

                Rect labelRect = cellRect;
                if (cell.iconDef != null)
                {
                    Rect iconRect = new Rect(cellRect.x, cellRect.y + (rowHeight - IconSize) / 2f, IconSize, IconSize);
                    Widgets.DefIcon(iconRect, cell.iconDef, drawPlaceholder: true);
                    labelRect.xMin = iconRect.xMax + IconGap;
                }

                Widgets.LabelEllipses(labelRect, cell.text);
                TooltipHandler.TipRegion(cellRect, cell.text);
            }

            Text.Anchor = prevAnchor;
            GUI.color = prevColor;
            listing.Gap(4f);
        }
    }
}

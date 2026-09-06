using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Settlement_Services.Framework.Dto
{
    public class ServiceDisplayOption
    {
        public string key;
        public string label;
        public string description;

        public string groupKey;

        public string iconTexPath;
        public Color? iconColor;

        public int groupColumnCount = 1;

        public bool allowMultipleSelectionInGroup;

        public bool isOptional;

        public List<string> conflictingOptionKeys;

        public Pawn pawnPreview;
    }
}

using UnityEngine;

namespace Settlement_Services.UI
{
    internal static class ServiceWindowSizing
    {
        public const float MainWidth = 900f;
        public const float MainHeight = 760f;

        public static Vector2 Main => new Vector2(MainWidth, MainHeight);

        public static Vector2 MainClampedToScreen =>
            new Vector2(Mathf.Min(MainWidth, Verse.UI.screenWidth), Mathf.Min(MainHeight, Verse.UI.screenHeight));
    }
}

using HarmonyLib;
using Verse;

namespace Settlement_Services.UI.Audio
{
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    internal static class WindowStack_Add_ServiceSoundtrackPatch
    {
        private static void Postfix(Window window)
        {
            if (window is Dialog_SettlementServices dialog)
                ServiceSoundtrackController.OnDialogOpened(dialog);
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.TryRemove), new[] { typeof(Window), typeof(bool) })]
    internal static class WindowStack_TryRemove_ServiceSoundtrackPatch
    {
        private static void Postfix(Window window, bool __result)
        {
            if (__result && window is Dialog_SettlementServices dialog)
                ServiceSoundtrackController.OnDialogClosed(dialog);
        }
    }
}

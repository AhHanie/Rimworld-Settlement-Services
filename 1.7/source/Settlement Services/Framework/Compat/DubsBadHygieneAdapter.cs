using RimWorld;
using UnityEngine;
using Verse;

namespace Settlement_Services.Framework.Compat
{
    public static class DubsBadHygieneAdapter
    {
        public static bool TryRestoreHygiene(Pawn pawn, float amount) => TryRestore(pawn, "Hygiene", amount);

        public static bool TryRestoreBladder(Pawn pawn, float amount) => TryRestore(pawn, "Bladder", amount);

        private static bool TryRestore(Pawn pawn, string needDefName, float amount)
        {
            if (pawn?.needs == null || amount <= 0f) return false;
            NeedDef needDef = DefDatabase<NeedDef>.GetNamedSilentFail(needDefName);
            if (needDef == null) return false;
            if (!pawn.needs.TryGetNeed(needDef, out Need need)) return false;

            need.CurLevel = Mathf.Min(need.CurLevel + amount, need.MaxLevel);
            return true;
        }
    }
}

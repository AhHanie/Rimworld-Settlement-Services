using System.Collections.Generic;
using Verse;

namespace Settlement_Services.Framework.Compatibility
{
    public class CompatibilityWorldState : IExposable
    {
        private Dictionary<string, int> lastTickByScopeKey = new Dictionary<string, int>();

        public bool TryAcquireCooldown(string moduleId, string scopeKey, int cooldownTicks)
        {
            string key = CombineKey(moduleId, scopeKey);
            int now = Find.TickManager.TicksGame;
            if (lastTickByScopeKey.TryGetValue(key, out int lastTick) && now - lastTick < cooldownTicks)
                return false;

            lastTickByScopeKey[key] = now;
            return true;
        }

        public void PruneOlderThan(int retentionTicks)
        {
            if (lastTickByScopeKey.Count == 0) return;

            int now = Find.TickManager.TicksGame;
            List<string> stale = null;
            foreach (KeyValuePair<string, int> entry in lastTickByScopeKey)
            {
                if (now - entry.Value < retentionTicks) continue;
                (stale ?? (stale = new List<string>())).Add(entry.Key);
            }

            if (stale == null) return;
            foreach (string key in stale) lastTickByScopeKey.Remove(key);
        }

        private static string CombineKey(string moduleId, string scopeKey) => moduleId + ":" + scopeKey;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref lastTickByScopeKey, "lastTickByScopeKey", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && lastTickByScopeKey == null)
                lastTickByScopeKey = new Dictionary<string, int>();
        }
    }
}

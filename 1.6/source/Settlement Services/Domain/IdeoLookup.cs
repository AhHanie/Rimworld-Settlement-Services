using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Settlement_Services.Domain
{
    public static class IdeoLookup
    {
        public static Ideo ResolveIdeo(string loadId)
        {
            if (string.IsNullOrEmpty(loadId) || !ModsConfig.IdeologyActive) return null;

            List<Ideo> ideos = Find.IdeoManager.IdeosListForReading;
            for (int i = 0; i < ideos.Count; i++)
                if (ideos[i].GetUniqueLoadID() == loadId) return ideos[i];
            return null;
        }
    }
}

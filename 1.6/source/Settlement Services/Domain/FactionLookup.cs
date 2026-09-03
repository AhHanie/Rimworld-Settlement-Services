using System.Linq;
using RimWorld;
using Verse;

namespace Settlement_Services.Domain
{
    public static class FactionLookup
    {
        public static Faction ResolveFaction(string uniqueLoadId)
        {
            if (uniqueLoadId.NullOrEmpty()) return null;
            return Find.FactionManager.AllFactionsListForReading
                .FirstOrDefault(f => !f.defeated && f.GetUniqueLoadID() == uniqueLoadId);
        }
    }
}

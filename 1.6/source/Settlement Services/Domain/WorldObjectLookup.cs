using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Domain
{
    public static class WorldObjectLookup
    {
        public static Settlement ResolveSettlement(int settlementWorldObjectId)
        {
            List<Settlement> list = Find.WorldObjects.Settlements;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].ID == settlementWorldObjectId) return list[i];
            }
            return null;
        }
    }
}

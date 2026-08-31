using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Services.Genetics
{
    internal static class GeneOptionService
    {
        public const string XenogermGroupKey = "SettlementServices.Label.XenogermChoice";

        public static IEnumerable<Xenogerm> FindCarried(Caravan caravan)
        {
            if (caravan == null) yield break;
            foreach (Thing thing in CaravanInventoryUtility.AllInventoryItems(caravan))
                if (thing is Xenogerm xenogerm) yield return xenogerm;
        }

        public static string KeyFor(Xenogerm xenogerm) => xenogerm.thingIDNumber.ToString();

        public static Xenogerm ResolveFromCaravan(Caravan caravan, string key) =>
            FindCarried(caravan).FirstOrDefault(x => KeyFor(x) == key);
    }
}

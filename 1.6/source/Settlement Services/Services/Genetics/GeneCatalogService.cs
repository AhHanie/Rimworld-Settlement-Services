using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Settlement_Services.Services.Genetics
{
    internal static class GeneCatalogService
    {
        public const string XenotypeGroupKey = "SettlementServices.Label.XenotypeChoice";

        private static readonly string[] CuratedXenotypeDefNames =
        {
            "Neanderthal", "Genie", "Hussar", "Yttakin", "Impid", "Pigskin", "Waster", "Dirtmole",
        };

        public static IEnumerable<XenotypeDef> CuratedXenotypes() =>
            CuratedXenotypeDefNames
                .Select(DefDatabase<XenotypeDef>.GetNamedSilentFail)
                .Where(d => d != null);

        public static Xenogerm BuildSyntheticXenogerm(XenotypeDef xenotypeDef)
        {
            var genepack = (Genepack)ThingMaker.MakeThing(ThingDefOf.Genepack);
            genepack.Initialize(xenotypeDef.genes);

            var xenogerm = (Xenogerm)ThingMaker.MakeThing(ThingDefOf.Xenogerm);
            xenogerm.Initialize(new List<Genepack> { genepack }, xenotypeDef.label, XenotypeIconDefOf.Basic);
            return xenogerm;
        }
    }
}

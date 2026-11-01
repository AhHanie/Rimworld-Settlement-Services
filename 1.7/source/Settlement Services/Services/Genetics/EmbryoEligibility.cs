using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Services.Genetics
{
    internal enum EmbryoBlock
    {
        None,
        NotEmbryo,
        Gone,
        NoGenes,
        ImplantPending,
        PartialStack,
        NotInCaravan,
    }

    internal static class EmbryoEligibility
    {
        public static EmbryoBlock Check(Thing target, Caravan caravan, bool atStart)
        {
            if (!(target is HumanEmbryo embryo) || embryo.def != ThingDefOf.HumanEmbryo) return EmbryoBlock.NotEmbryo;
            if (embryo.Destroyed) return EmbryoBlock.Gone;
            if (embryo.GeneSet == null) return EmbryoBlock.NoGenes;
            if (embryo.implantTarget != null) return EmbryoBlock.ImplantPending;
            if (embryo.stackCount != 1) return EmbryoBlock.PartialStack;
            if (!atStart && caravan != null && !CaravanInventoryUtility.AllInventoryItems(caravan).Contains(embryo)) return EmbryoBlock.NotInCaravan;
            return EmbryoBlock.None;
        }
    }
}

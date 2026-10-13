using RimWorld;
using Verse;
using Settlement_Services.Domain;

namespace Settlement_Services.Framework.Dto
{
    public class ServiceTargetSelection
    {
        public TargetKind kind;
        public Thing thing;

        public static readonly ServiceTargetSelection None = new ServiceTargetSelection { kind = TargetKind.None };

        public TargetSnapshot ToSnapshot()
        {
            if (kind == TargetKind.None) return null;

            QualityCategory? quality = thing != null && thing.TryGetQuality(out QualityCategory q) ? q : (QualityCategory?)null;

            return new TargetSnapshot
            {
                kind = kind,
                liveThing = thing,
                snapshotLabel = thing?.LabelCap,
                snapshotDefName = thing?.def?.defName,
                snapshotQuality = quality,
            };
        }
    }
}

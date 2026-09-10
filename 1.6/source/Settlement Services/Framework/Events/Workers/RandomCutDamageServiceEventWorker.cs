using RimWorld;
using Verse;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events.Workers
{
    public class RandomCutDamageServiceEventWorker : ServiceEventWorker
    {
        public override bool CanApply(ServiceJobContext ctx) => IsUsable(ctx.ResolvePrimaryPawn());

        public override void Apply(ServiceJobContext ctx)
        {
            Pawn pawn = ctx.ResolvePrimaryPawn();
            if (!IsUsable(pawn)) return;

            int damage = Rand.RangeInclusive(3, 7);
            pawn.TakeDamage(new DamageInfo(DamageDefOf.Cut, damage));
        }

        private static bool IsUsable(Pawn pawn) => pawn != null && !pawn.Dead && pawn.health != null;
    }
}

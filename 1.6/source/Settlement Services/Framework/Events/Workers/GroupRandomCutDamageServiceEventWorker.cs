using System.Collections.Generic;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events.Workers
{
    public class GroupRandomCutDamageServiceEventWorker : ServiceEventWorker
    {
        public override bool CanApply(ServiceJobContext ctx) =>
            AnimalGroupServiceEventUtility.HasAtLeastTwo(AnimalGroupServiceEventUtility.ResolveLiveAnimals(ctx));

        public override void Apply(ServiceJobContext ctx)
        {
            List<Pawn> eligible = AnimalGroupServiceEventUtility.ResolveLiveAnimals(ctx);
            List<Pawn> affected = AnimalGroupServiceEventUtility.RandomSubsetAtLeastTwo(eligible);
            if (affected.Count < 2) return;

            foreach (Pawn pawn in affected)
            {
                int damage = Rand.RangeInclusive(3, 7);
                pawn.TakeDamage(new DamageInfo(DamageDefOf.Cut, damage));
            }
        }
    }
}

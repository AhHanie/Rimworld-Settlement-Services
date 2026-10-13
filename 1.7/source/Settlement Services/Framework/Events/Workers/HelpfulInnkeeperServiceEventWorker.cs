using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events.Workers
{
    public class HelpfulInnkeeperServiceEventWorker : ServiceEventWorker
    {
        public override bool CanApply(ServiceJobContext ctx) =>
            ctx.ResolvePrimaryPawn() != null && ServiceQuestHookEffect.CanFireRandomQuest(ctx);

        public override void Apply(ServiceJobContext ctx) => ServiceQuestHookEffect.TryFireRandomQuest(ctx);
    }
}

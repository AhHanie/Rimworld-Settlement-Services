using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events
{
    internal static class ServiceQuestHookEffect
    {
        public static void TryFire(string questScriptDefName, ServiceJobContext ctx)
        {
            QuestScriptDef questDef = DefDatabase<QuestScriptDef>.GetNamedSilentFail(questScriptDefName);
            if (questDef == null)
            {
                Settlement_Services.SupportLog.Info($"Service event quest hook '{questScriptDefName}' did not resolve to a QuestScriptDef; skipped.");
                return;
            }

            Settlement settlement = ctx.ResolveSettlement();
            var slate = new Slate();
            if (settlement?.Faction != null) slate.Set("faction", settlement.Faction);

            Quest quest = QuestGen.Generate(questDef, slate);
            if (quest != null) QuestUtility.SendLetterQuestAvailable(quest);
        }
    }
}

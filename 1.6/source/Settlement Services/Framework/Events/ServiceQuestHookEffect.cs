using System.Linq;
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

            Map homeMap = Find.AnyPlayerHomeMap;
            float points = homeMap != null ? StorytellerUtility.DefaultThreatPointsNow(homeMap) : 0f;

            Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(questDef, BuildSlate(ctx, homeMap, points));
            if (quest != null && !quest.hidden && quest.root.sendAvailableLetter)
                QuestUtility.SendLetterQuestAvailable(quest);
        }

        public static bool CanFireRandomQuest(ServiceJobContext ctx)
        {
            Map homeMap = Find.AnyPlayerHomeMap;
            if (homeMap == null) return false;

            float points = StorytellerUtility.DefaultThreatPointsNow(homeMap);
            return DefDatabase<QuestScriptDef>.AllDefs.Any(q =>
                q.IsRootRandomSelected &&
                q.CanRun(points, homeMap) &&
                NaturalRandomQuestChooser.GetNaturalRandomSelectionWeight(q, points, homeMap.StoryState) > 0f);
        }

        public static bool TryFireRandomQuest(ServiceJobContext ctx)
        {
            Map homeMap = Find.AnyPlayerHomeMap;
            if (homeMap == null) return false;

            float points = StorytellerUtility.DefaultThreatPointsNow(homeMap);
            QuestScriptDef questDef = NaturalRandomQuestChooser.ChooseNaturalRandomQuest(points, homeMap);
            if (questDef == null) return false;

            Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(questDef, BuildSlate(ctx, homeMap, points));
            if (quest == null) return false;

            if (!quest.hidden && quest.root.sendAvailableLetter)
                QuestUtility.SendLetterQuestAvailable(quest, "SettlementServices.Event.HelpfulInnkeeperDiscovery".Translate());
            return true;
        }

        private static Slate BuildSlate(ServiceJobContext ctx, Map homeMap, float points)
        {
            var slate = new Slate();
            slate.Set("points", points);
            if (homeMap != null) slate.Set("map", homeMap);

            Faction faction = ctx.ResolveSettlement()?.Faction;
            if (faction != null) slate.Set("faction", faction);

            return slate;
        }
    }
}

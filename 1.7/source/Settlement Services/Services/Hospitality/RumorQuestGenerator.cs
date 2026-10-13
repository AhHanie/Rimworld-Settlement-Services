using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace Settlement_Services.Services.Hospitality
{
    internal static class RumorQuestGenerator
    {
        private const string DiscoveryKey = "SettlementServices.Event.RumorDiscovery";
        private const string NoLeadsErrorKey = "SettlementServices.Error.NoRumorLeadsAvailable";

        private static int cachedTick = -1;
        private static int cachedMapId = -1;
        private static float cachedPoints;
        private static bool cachedResult;

        public static bool CanFindCandidate(bool forceFresh)
        {
            Map homeMap = Find.AnyPlayerHomeMap;
            if (homeMap == null) return false;

            float points = StorytellerUtility.DefaultThreatPointsNow(homeMap);
            int tick = Find.TickManager.TicksGame;
            if (!forceFresh && cachedTick == tick && cachedMapId == homeMap.uniqueID && cachedPoints == points)
                return cachedResult;

            bool result = EligibleRoots(points, homeMap).Any();
            cachedTick = tick;
            cachedMapId = homeMap.uniqueID;
            cachedPoints = points;
            cachedResult = result;
            return result;
        }

        public static bool TryGenerate(Settlement settlement, out string errorKey)
        {
            errorKey = NoLeadsErrorKey;

            Map homeMap = Find.AnyPlayerHomeMap;
            if (homeMap == null) return false;

            float points = StorytellerUtility.DefaultThreatPointsNow(homeMap);
            QuestScriptDef questDef = ChooseRoot(points, homeMap);
            if (questDef == null) return false;

            var slate = new Slate();
            slate.Set("points", points);
            slate.Set("map", homeMap);

            Quest quest = QuestGen.Generate(questDef, slate);
            if (quest == null || quest.hidden || quest.hiddenInUI)
            {
                Settlement_Services.SupportLog.Info($"Rumor quest generation for '{questDef.defName}' produced no visible quest; no lead was granted.");
                return false;
            }

            Find.QuestManager.Add(quest);
            string discovery = settlement != null ? DiscoveryKey.Translate(settlement.Label).Resolve() : DiscoveryKey.Translate("").Resolve();
            QuestUtility.SendLetterQuestAvailable(quest, discovery);

            cachedTick = -1;
            errorKey = null;
            return true;
        }

        private static IEnumerable<QuestScriptDef> EligibleRoots(float points, Map homeMap) =>
            DefDatabase<QuestScriptDef>.AllDefs.Where(q =>
                q.IsRootRandomSelected &&
                !q.defaultHidden &&
                q.sendAvailableLetter &&
                !q.autoAccept &&
                q.CanRun(points, homeMap) &&
                NaturalRandomQuestChooser.GetNaturalRandomSelectionWeight(q, points, homeMap.StoryState) > 0f);

        private static QuestScriptDef ChooseRoot(float points, Map homeMap)
        {
            List<QuestScriptDef> roots = EligibleRoots(points, homeMap).ToList();
            if (roots.Count == 0) return null;

            bool increasePopulation = Rand.Chance(NaturalRandomQuestChooser.PopulationIncreasingQuestChance());
            List<QuestScriptDef> bucket = roots.Where(q => q.rootIncreasesPopulation == increasePopulation).ToList();
            if (bucket.Count == 0) bucket = roots.Where(q => q.rootIncreasesPopulation != increasePopulation).ToList();

            return bucket.TryRandomElementByWeight(q => NaturalRandomQuestChooser.GetNaturalRandomSelectionWeight(q, points, homeMap.StoryState), out QuestScriptDef chosen)
                ? chosen
                : null;
        }
    }
}

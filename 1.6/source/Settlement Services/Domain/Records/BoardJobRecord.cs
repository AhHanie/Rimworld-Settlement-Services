using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Domain.Records
{
    public class BoardJobRecord : IExposable
    {
        public int boardJobId = -1;
        public string defName;

        public int settlementWorldObjectId = -1;
        public PlanetTile settlementTile = PlanetTile.Invalid;
        public string providerFactionLoadId;

        public string title;
        public string flavor;
        public float difficulty;
        public int partySize = 1;
        public List<BoardSkillRequirement> requirements = new List<BoardSkillRequirement>();

        public int workTicks;
        public int createdTick;
        public int expiryTick;
        public int startTick = -1;
        public int completionTick = -1;

        public float rewardBudget;
        public BoardRewardRecord reward = new BoardRewardRecord();

        public List<Pawn> assignedPawns = new List<Pawn>();
        public int sourceCaravanId = -1;
        public PlanetTile originTile = PlanetTile.Invalid;
        public bool pawnsReturned;

        public BoardJobStatus status = BoardJobStatus.Available;
        public BoardJobStatus returnTarget = BoardJobStatus.Completed;
        public int statusChangedTick;
        public string failureKey;
        public bool resultNotified;

        public bool IsTerminal =>
            status == BoardJobStatus.Completed || status == BoardJobStatus.Cancelled
            || status == BoardJobStatus.Failed || status == BoardJobStatus.Expired;

        public bool HoldsWorkers => status == BoardJobStatus.Active || status == BoardJobStatus.ReturnPending;

        public void ExposeData()
        {
            Scribe_Values.Look(ref boardJobId, "boardJobId", -1);
            Scribe_Values.Look(ref defName, "defName");
            Scribe_Values.Look(ref settlementWorldObjectId, "settlementWorldObjectId", -1);
            Scribe_Values.Look(ref settlementTile, "settlementTile", PlanetTile.Invalid);
            Scribe_Values.Look(ref providerFactionLoadId, "providerFactionLoadId");
            Scribe_Values.Look(ref title, "title");
            Scribe_Values.Look(ref flavor, "flavor");
            Scribe_Values.Look(ref difficulty, "difficulty");
            Scribe_Values.Look(ref partySize, "partySize", 1);
            Scribe_Collections.Look(ref requirements, "requirements", LookMode.Deep);
            Scribe_Values.Look(ref workTicks, "workTicks");
            Scribe_Values.Look(ref createdTick, "createdTick");
            Scribe_Values.Look(ref expiryTick, "expiryTick");
            Scribe_Values.Look(ref startTick, "startTick", -1);
            Scribe_Values.Look(ref completionTick, "completionTick", -1);
            Scribe_Values.Look(ref rewardBudget, "rewardBudget");
            Scribe_Deep.Look(ref reward, "reward");
            Scribe_Collections.Look(ref assignedPawns, "assignedPawns", LookMode.Reference);
            Scribe_Values.Look(ref sourceCaravanId, "sourceCaravanId", -1);
            Scribe_Values.Look(ref originTile, "originTile", PlanetTile.Invalid);
            Scribe_Values.Look(ref pawnsReturned, "pawnsReturned");
            Scribe_Values.Look(ref status, "status");
            Scribe_Values.Look(ref returnTarget, "returnTarget", BoardJobStatus.Completed);
            Scribe_Values.Look(ref statusChangedTick, "statusChangedTick");
            Scribe_Values.Look(ref failureKey, "failureKey");
            Scribe_Values.Look(ref resultNotified, "resultNotified");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (requirements == null) requirements = new List<BoardSkillRequirement>();
                requirements.RemoveAll(r => r == null);
                if (reward == null) reward = new BoardRewardRecord();
                if (assignedPawns == null) assignedPawns = new List<Pawn>();
            }
        }
    }
}

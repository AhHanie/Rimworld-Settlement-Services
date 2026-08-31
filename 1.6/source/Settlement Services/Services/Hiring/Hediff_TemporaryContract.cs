using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Services.Hiring
{
    public class Hediff_TemporaryContract : Hediff
    {
        public int contractExpiryTick;
        public int originSettlementWorldObjectId = -1;
        public string originFactionLoadId;

        public override void Tick()
        {
            base.Tick();
            if (pawn.Dead || !pawn.IsHashIntervalTick(250)) return;

            if (Find.TickManager.TicksGame >= contractExpiryTick) { ForceDeparture("SettlementServices.Message.ContractExpired"); return; }
            if (OriginFactionTurnedHostile()) ForceDeparture("SettlementServices.Message.ContractVoidedHostile");
        }

        private bool OriginFactionTurnedHostile()
        {
            Faction origin = Find.FactionManager.AllFactionsListForReading.Find(f => f.GetUniqueLoadID() == originFactionLoadId);
            return origin != null && origin.HostileTo(Faction.OfPlayer);
        }

        private void ForceDeparture(string messageKey)
        {
            string pawnLabel = pawn.LabelShortCap;

            Caravan caravan = pawn.GetCaravan();
            if (caravan != null)
            {
                caravan.RemovePawn(pawn);
                Find.WorldPawns.PassToWorld(pawn);
            }
            else if (pawn.Spawned)
            {
                pawn.DeSpawn(DestroyMode.Vanish);
                Find.WorldPawns.PassToWorld(pawn);
            }

            pawn.health.RemoveHediff(this);
            Messages.Message(messageKey.Translate(pawnLabel), MessageTypeDefOf.NeutralEvent);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref contractExpiryTick, "contractExpiryTick");
            Scribe_Values.Look(ref originSettlementWorldObjectId, "originSettlementWorldObjectId", -1);
            Scribe_Values.Look(ref originFactionLoadId, "originFactionLoadId");
        }
    }
}

using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

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

            if (Find.TickManager.TicksGame >= contractExpiryTick)
            { ForceDeparture("SettlementServices.Message.ContractExpired", "SettlementServices.Message.ContractExpiredCaravan"); return; }
            if (OriginFactionTurnedHostile()) ForceDeparture("SettlementServices.Message.ContractVoidedHostile", "SettlementServices.Message.ContractVoidedHostileCaravan");
        }

        private bool OriginFactionTurnedHostile()
        {
            Faction origin = Find.FactionManager.AllFactionsListForReading.Find(f => f.GetUniqueLoadID() == originFactionLoadId);
            return origin != null && origin.HostileTo(Faction.OfPlayer);
        }

        private void ForceDeparture(string mapMessageKey, string caravanMessageKey)
        {
            if (pawn.Dead || pawn.Destroyed)
            {
                pawn.health?.RemoveHediff(this);
                return;
            }

            string pawnLabel = pawn.LabelShortCap;
            Faction originFaction = Find.FactionManager.AllFactionsListForReading.Find(f => f.GetUniqueLoadID() == originFactionLoadId);

            Caravan caravan = pawn.GetCaravan();
            if (caravan != null)
            {
                if (originFaction != null) pawn.SetFaction(originFaction);
                caravan.RemovePawn(pawn);
                Find.WorldPawns.PassToWorld(pawn);
                pawn.health.RemoveHediff(this);
                Messages.Message(caravanMessageKey.Translate(pawnLabel), MessageTypeDefOf.NeutralEvent);
                return;
            }

            if (!pawn.Spawned)
            {
                if (originFaction != null) pawn.SetFaction(originFaction);
                if (!Find.WorldPawns.Contains(pawn)) Find.WorldPawns.PassToWorld(pawn);
                pawn.health.RemoveHediff(this);
                Messages.Message(caravanMessageKey.Translate(pawnLabel), MessageTypeDefOf.NeutralEvent);
                return;
            }

            if (originFaction == null)
            {
                Settlement_Services.SupportLog.Warning($"Could not resolve origin faction '{originFactionLoadId}' for departing contractor {pawnLabel}; despawning instead of routing them to a map edge.");
                pawn.DeSpawn(DestroyMode.Vanish);
                Find.WorldPawns.PassToWorld(pawn);
                pawn.health.RemoveHediff(this);
                Messages.Message(caravanMessageKey.Translate(pawnLabel), MessageTypeDefOf.NeutralEvent);
                return;
            }

            pawn.SetFaction(originFaction);
            LordMaker.MakeNewLord(originFaction, new LordJob_ExitMapBest(LocomotionUrgency.Jog), pawn.Map, new[] { pawn });

            pawn.health.RemoveHediff(this);
            Messages.Message(mapMessageKey.Translate(pawnLabel), MessageTypeDefOf.NeutralEvent);
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

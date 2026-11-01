using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;

namespace Settlement_Services.Services.Genetics
{
    internal static class SurrogacyBirthService
    {
        public static bool TryBirth(SettlementServicesWorldComponent domain, ServiceJobRecord job, HumanEmbryo embryo)
        {
            EmbryoBirthResult birth = EmbryoBirthGeneration.TryGenerate(embryo);
            if (birth == null) return false;

            if (!domain.CommitSurrogacyBirth(job.jobId, embryo, birth.Stillborn ? null : birth.Newborn, birth.Corpse))
            {
                EmbryoBirthGeneration.Discard(birth.Newborn);
                return false;
            }

            return true;
        }

        public static void AnnounceOnce(SettlementServicesWorldComponent domain, ServiceJobRecord job)
        {
            SurrogacyJobState state = job.surrogacy;
            if (state == null || !state.birthCommitted || state.birthNotificationSent) return;

            Thing baby = job.Targets.Select(t => t?.liveThing).FirstOrDefault(t => t != null && !t.Destroyed);
            Pawn newborn = baby is Corpse corpse ? corpse.InnerPawn : baby as Pawn;
            if (newborn == null) return;

            domain.MarkSurrogacyNotificationSent(job.jobId);

            bool stillborn = state.stillborn;
            bool infantIllness = !stillborn && newborn.health.hediffSet.HasHediff(HediffDefOf.InfantIllness);
            Pawn mother = EmbryoBirthGeneration.UsableParent(newborn.GetMother());
            Pawn father = EmbryoBirthGeneration.UsableParent(newborn.GetFather());

            EmbryoBirthGeneration.GiveParentMemories(newborn, mother, father, stillborn, infantIllness);
            Find.QuestManager.Notify_PawnBorn(baby, baby, mother, father);

            bool noParents = mother == null || father == null;
            string textKey = stillborn ? "Stillborn" : infantIllness ? "InfantIllness" : "HealthyBaby";
            TaggedString text = noParents
                ? $"SettlementServices.Letter.Surrogacy{textKey}NoParents".Translate()
                : $"SettlementServices.Letter.Surrogacy{textKey}".Translate(mother, father);

            text += "\n\n" + EmbryoBirthGeneration.NamingText(newborn, stillborn);
            if (stillborn) text += "\n\n" + "SettlementServices.Letter.SurrogacyStillbirthCollect".Translate();

            var letter = (ChoiceLetter_BabyBirth)LetterMaker.MakeLetter($"SettlementServices.Letter.SurrogacyLabel{textKey}".Translate(), text, LetterDefOf.BabyBirth, newborn);
            letter.Start();
            Find.LetterStack.ReceiveLetter(letter);
        }
    }
}

using RimWorld;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;

namespace Settlement_Services.Services.Genetics
{
    internal static class GrowthVatBirthService
    {
        public static bool TryBirth(SettlementServicesWorldComponent domain, ServiceJobRecord job, HumanEmbryo embryo, GrowthVatMilestone milestone)
        {
            EmbryoBirthResult birth = EmbryoBirthGeneration.TryGenerate(embryo);
            if (birth == null) return false;

            if (!domain.CommitGrowthVatBirth(job.jobId, embryo, birth.Stillborn ? null : birth.Newborn, birth.Corpse))
            {
                EmbryoBirthGeneration.Discard(birth.Newborn);
                return false;
            }

            AnnounceBirth(job, birth, milestone);
            return true;
        }

        private static void AnnounceBirth(ServiceJobRecord job, EmbryoBirthResult birth, GrowthVatMilestone milestone)
        {
            Pawn newborn = birth.Newborn;
            Pawn mother = birth.Mother;
            Pawn father = birth.Father;
            bool stillborn = birth.Stillborn;

            EmbryoBirthGeneration.GiveParentMemories(newborn, mother, father, stillborn, birth.InfantIllness);

            Thing baby = stillborn ? birth.Corpse : newborn;
            Thing birther = (Thing)job.growthVat?.vat ?? birth.Corpse;
            Find.QuestManager.Notify_PawnBorn(baby, birther, mother, father);

            bool noParents = mother == null || father == null;
            TaggedString text;
            if (stillborn) text = noParents ? "LetterVatSillbornNoParents".Translate() : "LetterVatStillborn".Translate(mother, father);
            else if (birth.InfantIllness) text = noParents ? "LetterVatInfantIllnessNoParents".Translate() : "LetterVatInfantIllness".Translate(mother, father);
            else text = noParents ? "LetterVatHealthyBabyNoParents".Translate() : "LetterVatHealthyBaby".Translate(mother, father);

            text += "\n\n" + EmbryoBirthGeneration.NamingText(newborn, stillborn);

            if (stillborn)
                text += "\n\n" + "SettlementServices.Letter.GrowthVatStillbirthRefund".Translate();

            if (!stillborn && !milestone.IsBirth)
                text += "\n\n" + "SettlementServices.Letter.GrowthVatBirthGrowing".Translate(newborn.LabelShort, milestone.Label);

            var letter = (ChoiceLetter_BabyBirth)LetterMaker.MakeLetter("LetterVatBirth".Translate(birth.Outcome.label), text, LetterDefOf.BabyBirth, newborn);
            letter.Start();
            Find.LetterStack.ReceiveLetter(letter);
        }
    }
}

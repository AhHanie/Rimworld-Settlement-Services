using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Settlement_Services.Services.Genetics
{
    internal sealed class EmbryoBirthResult
    {
        public RitualOutcomePossibility Outcome;
        public Pawn Newborn;
        public Thing Corpse;
        public Pawn Mother;
        public Pawn Father;
        public bool Stillborn;
        public bool InfantIllness;
    }

    internal static class EmbryoBirthGeneration
    {
        public const float ClinicalQuality = 0.7f;

        private static readonly MethodInfo TryGetInheritedXenotypeMethod = AccessTools.Method(typeof(PregnancyUtility), "TryGetInheritedXenotype");
        private static readonly MethodInfo ShouldByHybridMethod = AccessTools.Method(typeof(PregnancyUtility), "ShouldByHybrid");

        public static bool BabiesAreHealthy => Find.Storyteller.difficulty.babiesAreHealthy;

        public static EmbryoBirthResult TryGenerate(HumanEmbryo embryo)
        {
            Pawn mother = UsableParent(embryo.Mother);
            Pawn father = UsableParent(embryo.Father);

            var worker = (RitualOutcomeEffectWorker_ChildBirth)RitualOutcomeEffectDefOf.ChildBirth.GetInstance();
            RitualOutcomePossibility outcome = worker.GetOutcome(ClinicalQuality, null);
            int positivityIndex = outcome.positivityIndex;
            bool stillborn = positivityIndex < 0;
            bool infantIllness = positivityIndex == 0;

            Pawn newborn = GenerateNewborn(embryo, mother, father, stillborn);
            if (newborn == null) return null;

            Thing corpse = null;
            if (stillborn)
            {
                Hediff culprit = newborn.health.AddHediff(HediffDefOf.Stillborn);
                newborn.babyNamingDeadline = Find.TickManager.TicksGame + 1;
                Find.BattleLog.Add(new BattleLogEntry_StateTransition(newborn, newborn.RaceProps.DeathActionWorker.DeathRules, null, culprit, null));
                corpse = newborn.Corpse ?? newborn.MakeCorpse(null, null);
                if (corpse == null)
                {
                    Discard(newborn);
                    return null;
                }
            }
            else if (infantIllness)
            {
                newborn.health.AddHediff(HediffDefOf.InfantIllness);
            }

            return new EmbryoBirthResult
            {
                Outcome = outcome,
                Newborn = newborn,
                Corpse = corpse,
                Mother = mother,
                Father = father,
                Stillborn = stillborn,
                InfantIllness = infantIllness,
            };
        }

        public static Pawn UsableParent(Pawn parent) => parent != null && !parent.Destroyed && !parent.Discarded ? parent : null;

        public static void Discard(Pawn newborn)
        {
            if (newborn == null || newborn.Destroyed) return;
            if (Find.WorldPawns.Contains(newborn)) Find.WorldPawns.RemovePawn(newborn);
            Find.WorldPawns.PassToWorld(newborn, PawnDiscardDecideMode.Discard);
        }

        public static void GiveParentMemories(Pawn newborn, Pawn mother, Pawn father, bool stillborn, bool infantIllness)
        {
            if (stillborn)
            {
                mother?.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDefOf.Stillbirth);
                father?.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDefOf.Stillbirth);
            }
            else if (!infantIllness)
            {
                father?.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDefOf.BabyBorn, newborn);
                mother?.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDefOf.BabyBorn, newborn);
            }
        }

        public static TaggedString NamingText(Pawn newborn, bool stillborn)
        {
            TaggedString text = "";
            if (newborn.genes != null && newborn.genes.HasActiveGene(GeneDefOf.Inbred)) text += "InbredBabyBorn".Translate() + "\n\n";

            text += "LetterPartTempBabyName".Translate(newborn) + " ";
            text += stillborn
                ? "LetterPartStillbirthNameDeadline".Translate()
                : "LetterPartLiveBirthNameDeadline".Translate(60000.ToStringTicksToPeriod());
            return text;
        }

        private static Pawn GenerateNewborn(HumanEmbryo embryo, Pawn mother, Pawn father, bool forceDead)
        {
            PawnKindDef kind = mother?.kindDef ?? PawnKindDefOf.Colonist;
            if (kind is CreepJoinerFormKindDef) kind = PawnKindDefOf.Colonist;

            List<GeneDef> embryoGenes = embryo.GeneSet?.GenesListForReading;
            List<GeneDef> forcedEndogenes = embryoGenes ?? PregnancyUtility.GetInheritedGenes(father, mother);
            string fixedLastName = RandomLastName(mother, father);
            XenotypeDef baseliner = XenotypeDefOf.Baseliner;

            var request = new PawnGenerationRequest(kind, Faction.OfPlayer, PawnGenerationContext.NonPlayer, null, forceGenerateNewPawn: false, allowDead: false, allowDowned: true, canGeneratePawnRelations: true, mustBeCapableOfViolence: false, 1f, forceAddFreeWarmLayerIfNeeded: false, allowGay: true, allowPregnant: false, allowFood: true, allowAddictions: true, inhabitant: false, certainlyBeenInCryptosleep: false, forceRedressWorldPawnIfFormerColonist: false, worldPawnFactionDoesntMatter: false, 0f, 0f, null, 1f, null, null, null, null, null, null, null, null, fixedLastName, null, null, null, forceNoIdeo: true, forceNoBackstory: false, forbidAnyTitle: false, forceDead, null, forcedEndogenes, baseliner, null, null, 0f, DevelopmentalStage.Newborn);
            request.DontGivePreArrivalPathway = true;

            Pawn pawn = PawnGenerator.GeneratePawn(request);
            if (pawn == null) return null;
            if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);

            ApplyInheritedXenotype(pawn, mother, father);

            if (pawn.RaceProps.IsFlesh)
            {
                if (mother != null) pawn.relations.AddDirectRelation(PawnRelationDefOf.Parent, mother);
                if (father != null) pawn.relations.AddDirectRelation(PawnRelationDefOf.Parent, father);
            }

            return pawn;
        }

        private static string RandomLastName(Pawn mother, Pawn father)
        {
            var names = new List<string>();
            if (mother != null) names.Add(PawnNamingUtility.GetLastName(mother));
            if (father != null) names.Add(PawnNamingUtility.GetLastName(father));
            names.RemoveAll(n => n.NullOrEmpty());
            return names.Count == 0 ? null : names.RandomElement();
        }

        private static void ApplyInheritedXenotype(Pawn pawn, Pawn mother, Pawn father)
        {
            if (pawn.genes == null) return;

            if (GeneUtility.SameHeritableXenotype(mother, father) && mother.genes.UniqueXenotype)
            {
                pawn.genes.xenotypeName = mother.genes.xenotypeName;
                pawn.genes.iconDef = mother.genes.iconDef;
            }

            if (TryGetInheritedXenotype(mother, father, out XenotypeDef xenotype))
            {
                pawn.genes.SetXenotypeDirect(xenotype);
            }
            else if (ShouldBeHybrid(mother, father))
            {
                pawn.genes.hybrid = true;
                pawn.genes.xenotypeName = "Hybrid".Translate();
            }
        }

        private static bool TryGetInheritedXenotype(Pawn mother, Pawn father, out XenotypeDef xenotype)
        {
            xenotype = null;
            if (TryGetInheritedXenotypeMethod == null) return false;

            object[] args = { mother, father, null };
            bool found = (bool)TryGetInheritedXenotypeMethod.Invoke(null, args);
            xenotype = args[2] as XenotypeDef;
            return found && xenotype != null;
        }

        private static bool ShouldBeHybrid(Pawn mother, Pawn father) =>
            ShouldByHybridMethod != null && (bool)ShouldByHybridMethod.Invoke(null, new object[] { mother, father });
    }
}

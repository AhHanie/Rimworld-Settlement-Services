using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Custody;

namespace Settlement_Services.Domain
{
    public partial class SettlementServicesWorldComponent
    {
        public GrowthVatJobState GetGrowthVatState(int jobId) => GetJob(jobId)?.growthVat;

        public bool AttachGrowthVatState(int jobId, GrowthVatJobState state)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job == null || state == null || job.growthVat != null) return false;

            job.growthVat = state;
            return true;
        }

        public void ClearGrowthVatState(int jobId)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job?.growthVat == null) return;

            ReleaseGrowthVat(jobId);
            job.growthVat = null;
        }

        public void RecordGrowthVatProgress(int jobId, int processedTick, int gestationTicksLeft, float nutritionConsumedDelta)
        {
            GrowthVatJobState state = GetGrowthVatState(jobId);
            if (state == null) return;

            state.lastProcessedTick = processedTick;
            state.gestationTicksLeft = gestationTicksLeft < 0 ? 0 : gestationTicksLeft;
            if (nutritionConsumedDelta > 0f) state.consumedNutrition += nutritionConsumedDelta;
        }

        public void MarkGrowthVatTargetReached(int jobId)
        {
            GrowthVatJobState state = GetGrowthVatState(jobId);
            if (state != null) state.targetReached = true;
        }

        public void MarkGrowthVatCompleted(int jobId)
        {
            GrowthVatJobState state = GetGrowthVatState(jobId);
            if (state != null) state.completed = true;
        }

        public int TakeGrowthVatRefund(int jobId, int amount)
        {
            GrowthVatJobState state = GetGrowthVatState(jobId);
            if (state == null || state.refundIssued) return 0;

            state.refundIssued = true;
            return amount > 0 ? amount : 0;
        }

        public void SetJobExpectedCompletionTick(int jobId, int tick)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job != null && job.status == ServiceJobStatus.Active) job.expectedCompletionTick = tick;
        }

        public bool TryEnterGrowthVat(int jobId, Pawn pawn)
        {
            GrowthVatJobState state = GetGrowthVatState(jobId);
            if (state == null || pawn == null || pawn.Destroyed || pawn.Dead) return false;
            if (state.vat != null) return state.vat.innerContainer.Contains(pawn);

            if (!(ThingMaker.MakeThing(ThingDefOf.GrowthVat) is Building_GrowthVat vat)) return false;
            vat.SetFactionDirect(Faction.OfPlayer);
            if (!growthVatCustody.TryAdd(vat, canMergeWithExistingStacks: false))
            {
                vat.Destroy();
                return false;
            }

            if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
            if (!vat.innerContainer.TryAdd(pawn, canMergeWithExistingStacks: false))
            {
                growthVatCustody.Remove(vat);
                vat.Destroy();
                TargetCustodyService.ParkPawnInWorld(pawn);
                return false;
            }

            vat.SelectedPawn = pawn;
            state.vat = vat;
            EnsureVatHediffs(pawn);
            return true;
        }

        public Pawn GrowthVatOccupant(int jobId)
        {
            Building_GrowthVat vat = GetGrowthVatState(jobId)?.vat;
            if (vat == null) return null;

            foreach (Thing thing in (IEnumerable<Thing>)vat.innerContainer)
                if (thing is Pawn pawn) return pawn;
            return null;
        }

        public void ReleaseGrowthVat(int jobId)
        {
            GrowthVatJobState state = GetGrowthVatState(jobId);
            if (state == null) return;

            Building_GrowthVat vat = state.vat;
            state.vat = null;
            if (vat == null) return;

            foreach (Thing thing in vat.innerContainer.ToList())
            {
                vat.innerContainer.Remove(thing);
                if (thing is Pawn pawn)
                {
                    StripVatHediffs(pawn);
                    TargetCustodyService.ParkPawnInWorld(pawn);
                }
                else
                {
                    TryTakeItemCustody(thing);
                }
            }

            vat.SelectedPawn = null;
            if (growthVatCustody.Contains(vat)) growthVatCustody.Remove(vat);
            if (!vat.Destroyed) vat.Destroy();
        }

        public bool CommitGrowthVatBirth(int jobId, HumanEmbryo embryo, Pawn newborn, Thing stillbornCorpse)
        {
            ServiceJobRecord job = GetJob(jobId);
            GrowthVatJobState state = job?.growthVat;
            if (state == null || state.birthCommitted || embryo == null || embryo.Destroyed) return false;

            TargetSnapshot snapshot;
            if (stillbornCorpse != null)
            {
                if (!TryTakeItemCustody(stillbornCorpse)) return false;
                snapshot = new TargetSnapshot
                {
                    kind = TargetKind.Item,
                    liveThing = stillbornCorpse,
                    snapshotLabel = stillbornCorpse.LabelCap,
                    snapshotDefName = stillbornCorpse.def?.defName,
                };
                state.stillborn = true;
            }
            else
            {
                if (newborn == null || !TryEnterGrowthVat(jobId, newborn)) return false;
                snapshot = new TargetSnapshot
                {
                    kind = TargetKind.Pawn,
                    liveThing = newborn,
                    snapshotLabel = newborn.LabelShort,
                    snapshotDefName = newborn.def?.defName,
                };
            }

            ReleaseItemCustody(embryo);
            ReplaceJobTarget(job, embryo, snapshot);
            state.birthCommitted = true;
            embryo.Destroy();
            return true;
        }

        private static void ReplaceJobTarget(ServiceJobRecord job, Thing oldThing, TargetSnapshot replacement)
        {
            int index = job.targets.FindIndex(t => t?.liveThing == oldThing);
            if (index < 0) return;

            TargetSnapshot previous = job.targets[index];
            job.targets[index] = replacement;
            if (job.target == previous) job.target = replacement;
        }

        private static void EnsureVatHediffs(Pawn pawn)
        {
            if (pawn.health == null) return;
            if (!pawn.health.hediffSet.HasHediff(HediffDefOf.VatLearning)) pawn.health.AddHediff(HediffDefOf.VatLearning);
            if (!pawn.health.hediffSet.HasHediff(HediffDefOf.VatGrowing)) pawn.health.AddHediff(HediffDefOf.VatGrowing);
        }

        private static void StripVatHediffs(Pawn pawn)
        {
            if (pawn.health == null) return;
            foreach (HediffDef def in new[] { HediffDefOf.VatLearning, HediffDefOf.VatGrowing })
            {
                Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(def);
                if (hediff != null) pawn.health.RemoveHediff(hediff);
            }
        }

        private void ReconcileGrowthVats()
        {
            var referenced = new HashSet<Thing>();
            foreach (ServiceJobRecord job in jobs)
                if (job?.growthVat?.vat != null) referenced.Add(job.growthVat.vat);

            foreach (Thing thing in growthVatCustody.ToList())
            {
                if (referenced.Contains(thing)) continue;

                if (thing is Building_GrowthVat orphan)
                {
                    foreach (Thing occupant in orphan.innerContainer.ToList())
                    {
                        orphan.innerContainer.Remove(occupant);
                        if (occupant is Pawn pawn)
                        {
                            StripVatHediffs(pawn);
                            TargetCustodyService.ParkPawnInWorld(pawn);
                            QueueHomeDelivery(new TargetSnapshot { kind = TargetKind.Pawn, liveThing = pawn, snapshotLabel = pawn.LabelShort });
                        }
                        else if (TryTakeItemCustody(occupant))
                        {
                            QueueHomeDelivery(new TargetSnapshot { kind = TargetKind.Item, liveThing = occupant, snapshotLabel = occupant.LabelCap });
                        }
                    }
                }

                growthVatCustody.Remove(thing);
                if (!thing.Destroyed) thing.Destroy();
            }
        }
    }
}

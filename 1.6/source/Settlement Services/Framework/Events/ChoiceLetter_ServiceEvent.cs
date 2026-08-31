using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Events
{
    public class ChoiceLetter_ServiceEvent : ChoiceLetter
    {
        public int jobId;
        public string eventDefName;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref jobId, "jobId");
            Scribe_Values.Look(ref eventDefName, "eventDefName");
        }

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                ServiceJobRecord job = SettlementServicesWorldComponent.Current?.GetJob(jobId);
                ServiceEventDef eventDef = eventDefName != null ? DefDatabase<ServiceEventDef>.GetNamedSilentFail(eventDefName) : null;

                if (job?.eventOutcome == null || job.eventOutcome.applied || eventDef?.choices == null)
                {
                    yield return Option_Close;
                    yield break;
                }

                for (int i = 0; i < eventDef.choices.Count; i++)
                {
                    int index = i;
                    ServiceEventChoice choice = eventDef.choices[i];
                    var option = new DiaOption(choice.labelKey.Translate())
                    {
                        resolveTree = true,
                        action = () => Resolve(job, choice, index),
                    };
                    yield return option;
                }
            }
        }

        private void Resolve(ServiceJobRecord job, ServiceEventChoice choice, int index)
        {
            var ctx = new ServiceJobContext(SettlementServicesWorldComponent.Current, job).ForUnitIndex(job.eventTargetIndex);
            ServiceEventEffectApplier.Apply(choice.effects, job, ctx);
            job.eventOutcome.applied = true;
            job.eventOutcome.choiceIndexSelected = index;
            Find.LetterStack.RemoveLetter(this);
        }

        public static void Send(ServiceJobRecord job, ServiceEventDef eventDef, ServiceJobContext ctx)
        {
            Settlement settlement = ctx.ResolveSettlement();
            LookTargets lookTargets = settlement != null ? new LookTargets(settlement) : LookTargets.Invalid;
            LetterDef letterDef = DefDatabase<LetterDef>.GetNamed("SettlementService_EventChoice");

            var letter = (ChoiceLetter_ServiceEvent)LetterMaker.MakeLetter(
                "SettlementServices.Letter.ServiceEventChoiceLabel".Translate(eventDef.LabelCap),
                "SettlementServices.Letter.ServiceEventChoiceText".Translate(eventDef.LabelCap, eventDef.description),
                letterDef, lookTargets, settlement?.Faction);
            letter.jobId = job.jobId;
            letter.eventDefName = eventDef.defName;
            Find.LetterStack.ReceiveLetter(letter);
        }
    }
}

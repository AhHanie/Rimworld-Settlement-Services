using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework
{
    internal static class SettlementServiceNotifier
    {
        public static void NotifyReadyForCollection(ServiceJobRecord job) =>
            Send(job, "SettlementServices.Letter.ReadyForCollectionLabel", "SettlementServices.Letter.ReadyForCollectionText", LetterDefOf.PositiveEvent);

        public static void NotifyCompleted(ServiceJobRecord job) =>
            Messages.Message("SettlementServices.Message.Completed".Translate(), MessageTypeDefOf.PositiveEvent, historical: false);

        public static void NotifyInterrupted(ServiceJobRecord job) =>
            Send(job, "SettlementServices.Letter.InterruptedLabel", "SettlementServices.Letter.InterruptedText", LetterDefOf.NeutralEvent);

        public static void NotifyDeliveredHome(ServiceJobRecord job) =>
            Messages.Message("SettlementServices.Message.DeliveredHome".Translate(), MessageTypeDefOf.NeutralEvent, historical: false);

        public static void NotifyFailed(ServiceJobRecord job) =>
            Send(job, "SettlementServices.Letter.FailedLabel", "SettlementServices.Letter.FailedText", LetterDefOf.NegativeEvent);

        public static void NotifyShuttleMergeSkipped(ServiceJobRecord job) =>
            Messages.Message("SettlementServices.Message.ShuttleMergeSkipped".Translate(), MessageTypeDefOf.NeutralEvent, historical: false);

        public static void NotifyEvent(ServiceJobRecord job, ServiceEventDef eventDef)
        {
            LetterDef tone = eventDef.weightClass == ServiceEventWeightClass.Complication
                ? LetterDefOf.NegativeEvent
                : eventDef.weightClass == ServiceEventWeightClass.RareExceptional || eventDef.weightClass == ServiceEventWeightClass.MinorPositive
                    ? LetterDefOf.PositiveEvent
                    : LetterDefOf.NeutralEvent;

            Send(job, "SettlementServices.Letter.ServiceEventLabel", "SettlementServices.Letter.ServiceEventText", tone, eventDef);
        }

        private static void Send(ServiceJobRecord job, string labelKey, string textKey, LetterDef def, ServiceEventDef eventDef = null)
        {
            Settlement settlement = new ServiceJobContext(SettlementServicesWorldComponent.Current, job).ResolveSettlement();
            LookTargets lookTargets = settlement != null ? new LookTargets(settlement) : LookTargets.Invalid;

            TaggedString label = eventDef != null ? labelKey.Translate(eventDef.LabelCap, eventDef.description) : labelKey.Translate();
            TaggedString text = eventDef != null ? textKey.Translate(eventDef.LabelCap, eventDef.description) : textKey.Translate();
            Find.LetterStack.ReceiveLetter(label, text, def, lookTargets);
        }
    }
}

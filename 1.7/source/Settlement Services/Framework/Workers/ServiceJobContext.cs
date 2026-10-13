using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;

namespace Settlement_Services.Framework.Workers
{
    public struct ServiceJobContext
    {
        public SettlementServicesWorldComponent Domain { get; }
        public ServiceJobRecord Job { get; }

        public TargetSnapshot CurrentTarget { get; }
        public int UnitIndex { get; }

        public ServiceJobContext(SettlementServicesWorldComponent domain, ServiceJobRecord job)
            : this(domain, job, job?.target, 0)
        {
        }

        private ServiceJobContext(SettlementServicesWorldComponent domain, ServiceJobRecord job, TargetSnapshot currentTarget, int unitIndex)
        {
            Domain = domain;
            Job = job;
            CurrentTarget = currentTarget;
            UnitIndex = unitIndex;
        }

        public ServiceJobContext ForTarget(TargetSnapshot snapshot, int unitIndex = 0) =>
            new ServiceJobContext(Domain, Job, snapshot, unitIndex);

        public ServiceJobContext ForUnitIndex(int index)
        {
            var all = Job.Targets;
            TargetSnapshot snapshot = index >= 0 && index < all.Count ? all[index] : null;
            return new ServiceJobContext(Domain, Job, snapshot, index);
        }

        public Thing CurrentTargetThing => CurrentTarget?.liveThing;

        public System.Collections.Generic.IReadOnlyList<string> SelectedOptionKeys => Job.OptionKeysForTarget(UnitIndex);

        public System.Collections.Generic.IReadOnlyList<TargetSnapshot> ResolveTargets() => Job.Targets;

        public Settlement ResolveSettlement() => WorldObjectLookup.ResolveSettlement(Job.settlementWorldObjectId);

        public Pawn ResolvePrimaryPawn()
        {
            TargetSnapshot candidate = CurrentTarget;
            if (candidate == null)
            {
                var all = Job.Targets;
                candidate = Job.eventTargetIndex >= 0 && Job.eventTargetIndex < all.Count
                    ? all[Job.eventTargetIndex]
                    : all.FirstOrDefault(t => t?.liveThing is Pawn);
            }

            if (candidate?.liveThing is Pawn targetPawn && !targetPawn.Destroyed)
                return targetPawn;

            int requesterCaravanId = Job.requesterCaravanId;
            if (requesterCaravanId < 0) return null;
            Caravan caravan = Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == requesterCaravanId);
            return caravan?.PawnsListForReading.FirstOrDefault(p => p.IsColonist);
        }
    }
}

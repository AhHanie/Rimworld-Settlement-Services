using RimWorld;
using Verse;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Custody;

namespace Settlement_Services.Domain
{
    public partial class SettlementServicesWorldComponent
    {
        public SurrogacyJobState GetSurrogacyState(int jobId) => GetJob(jobId)?.surrogacy;

        public bool AttachSurrogacyState(int jobId, SurrogacyJobState state)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job == null || state == null || job.surrogacy != null) return false;

            job.surrogacy = state;
            return true;
        }

        public void ClearSurrogacyState(int jobId)
        {
            ServiceJobRecord job = GetJob(jobId);
            if (job?.surrogacy != null && !job.surrogacy.birthCommitted) job.surrogacy = null;
        }

        public void MarkSurrogacyNotificationSent(int jobId)
        {
            SurrogacyJobState state = GetSurrogacyState(jobId);
            if (state != null) state.birthNotificationSent = true;
        }

        public void MarkSurrogacyCompleted(int jobId)
        {
            SurrogacyJobState state = GetSurrogacyState(jobId);
            if (state != null) state.completed = true;
        }

        public int TakeSurrogacyRefund(int jobId, int amount)
        {
            SurrogacyJobState state = GetSurrogacyState(jobId);
            if (state == null || state.refundIssued || state.birthCommitted) return 0;

            state.refundIssued = true;
            return amount > 0 ? amount : 0;
        }

        public bool CommitSurrogacyBirth(int jobId, HumanEmbryo embryo, Pawn newborn, Thing stillbornCorpse)
        {
            ServiceJobRecord job = GetJob(jobId);
            SurrogacyJobState state = job?.surrogacy;
            if (state == null || state.birthCommitted || state.refundIssued || embryo == null || embryo.Destroyed) return false;
            if (job.targets.FindIndex(t => t?.liveThing == embryo) < 0) return false;

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
                if (newborn == null || newborn.Destroyed) return false;
                TargetCustodyService.ParkPawnInWorld(newborn);
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
    }
}

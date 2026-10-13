using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;

namespace Settlement_Services.UI.JobBoard
{
    internal static class BoardJobFormatting
    {
        internal static string RoleLabel(BoardSkillRequirement requirement)
        {
            switch (requirement.roleKind)
            {
                case BoardRoleKind.Crew: return "SettlementServices.JobBoard.Role.Crew".Translate();
                case BoardRoleKind.Specialist: return "SettlementServices.JobBoard.Role.Specialist".Translate();
                case BoardRoleKind.SpecialistWithSupport:
                    return requirement.requiredCount == 1
                        ? "SettlementServices.JobBoard.Role.Specialist".Translate()
                        : "SettlementServices.JobBoard.Role.Support".Translate();
                default: return "SettlementServices.JobBoard.Role.Expert".Translate();
            }
        }

        internal static string SkillLabel(BoardSkillRequirement requirement) =>
            requirement.skill != null ? requirement.skill.skillLabel.CapitalizeFirst() : "?";

        internal static string RequirementLine(BoardSkillRequirement requirement) =>
            "SettlementServices.JobBoard.Role.Line".Translate(RoleLabel(requirement), SkillLabel(requirement), requirement.minLevel, requirement.requiredCount).Resolve();

        internal static string RolesSummary(BoardJobRecord job) =>
            string.Join(", ", job.requirements.Select(r =>
                "SettlementServices.JobBoard.Role.Short".Translate(SkillLabel(r), r.minLevel, r.requiredCount).Resolve()));

        internal static string WorkDuration(BoardJobRecord job) => job.workTicks.ToStringTicksToPeriod();

        internal static string ExpiresIn(BoardJobRecord job) =>
            (job.expiryTick - Find.TickManager.TicksGame).ToStringTicksToPeriod();

        internal static string RemainingTime(BoardJobRecord job) =>
            (job.completionTick - Find.TickManager.TicksGame).ToStringTicksToPeriod();

        internal static string RewardValue(BoardJobRecord job) => job.reward.marketValue.ToStringMoney();

        internal static string WorkerNames(BoardJobRecord job)
        {
            IEnumerable<string> names = job.assignedPawns.Where(p => p != null).Select(p => p.LabelShortCap);
            return string.Join(", ", names);
        }

        internal static string StatusLabel(BoardJobRecord job)
        {
            switch (job.status)
            {
                case BoardJobStatus.Available: return "SettlementServices.JobBoard.Status.Available".Translate();
                case BoardJobStatus.Active: return "SettlementServices.JobBoard.Status.Active".Translate(RemainingTime(job));
                case BoardJobStatus.ReturnPending: return "SettlementServices.JobBoard.Status.ReturnPending".Translate();
                case BoardJobStatus.Completed: return "SettlementServices.JobBoard.Status.Completed".Translate();
                case BoardJobStatus.Cancelled: return "SettlementServices.JobBoard.Status.Cancelled".Translate();
                case BoardJobStatus.Failed: return "SettlementServices.JobBoard.Status.Failed".Translate();
                default: return "SettlementServices.JobBoard.Status.Expired".Translate();
            }
        }
    }
}

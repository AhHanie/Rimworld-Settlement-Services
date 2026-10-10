using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Defs;
using Settlement_Services.Framework.Investment;
using Settlement_Services.Framework.Specialty;
using Verse;

namespace Settlement_Services.UI.Interaction
{
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.GetInspectString))]
    internal static class Settlement_GetInspectString_ServicesPatch
    {
        private static void Postfix(Settlement __instance, ref string __result)
        {
            SettlementServicesWorldComponent domain = SettlementServicesWorldComponent.Current;
            if (domain == null) return;

            var lines = new List<string>();

            Faction faction = __instance.Faction;
            IReadOnlyList<SettlementSpecialtyDef> specialties = faction != null && !faction.IsPlayer
                ? SettlementSpecialtyService.GetSpecialties(__instance)
                : null;
            IReadOnlyList<DiscoveryRecord> discoveries = domain.DiscoveriesForSettlement(__instance.ID);
            lines.AddRange(SettlementInspectServiceSummary.BuildLines(specialties, discoveries));

            string jobSummary = BuildJobSummaryLine(domain, __instance);
            string investmentLine = BuildInvestmentLine(domain, __instance);
            if (jobSummary != null) lines.Add(jobSummary);
            if (investmentLine != null) lines.Add(investmentLine);
            if (lines.Count == 0) return;

            string appended = string.Join("\n", lines);
            __result = __result.NullOrEmpty() ? appended : __result + "\n" + appended;
        }

        private static string BuildJobSummaryLine(SettlementServicesWorldComponent domain, Settlement settlement)
        {
            IReadOnlyList<ServiceJobRecord> jobs = domain.JobsForSettlement(settlement.ID);
            if (jobs.Count == 0) return null;

            string joined = string.Join(", ", jobs
                .Select(j => Overview.ServiceOverviewFormatting.StatusLabel(j.status))
                .GroupBy(label => label)
                .OrderBy(g => g.Key)
                .Select(g => g.Count() == 1 ? g.Key : $"{g.Key} x{g.Count()}"));

            return joined.NullOrEmpty() ? null : "SettlementServices.Label.YourServices".Translate(joined);
        }

        private static string BuildInvestmentLine(SettlementServicesWorldComponent domain, Settlement settlement)
        {
            if (domain.GetInvestment(settlement.ID) == null) return null;

            float pct = SettlementInvestmentService.CurrentDiscountPct(settlement);
            if (pct <= 0f) return "SettlementServices.Label.InvestmentStatusExpired".Translate();

            int daysRemaining = SettlementInvestmentService.TicksRemaining(settlement) / GenDate.TicksPerDay;
            return "SettlementServices.Label.InvestmentStatusActive".Translate(pct.ToStringPercent(), daysRemaining);
        }
    }
}

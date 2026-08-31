using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Investment;
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

            string knownServices = BuildKnownServicesLine(domain, __instance);
            string jobSummary = BuildJobSummaryLine(domain, __instance);
            string investmentLine = BuildInvestmentLine(domain, __instance);
            if (knownServices == null && jobSummary == null && investmentLine == null) return;

            var sb = new StringBuilder(__result);
            if (knownServices != null) { sb.AppendLine(); sb.Append(knownServices); }
            if (jobSummary != null) { sb.AppendLine(); sb.Append(jobSummary); }
            if (investmentLine != null) { sb.AppendLine(); sb.Append(investmentLine); }
            __result = sb.ToString();
        }

        private static string BuildKnownServicesLine(SettlementServicesWorldComponent domain, Settlement settlement)
        {
            IReadOnlyList<DiscoveryRecord> discoveries = domain.DiscoveriesForSettlement(settlement.ID);
            if (discoveries.Count == 0) return null;

            (string inPerson, string remote) = ServiceDiscoveryFormatting.SplitByChannel(discoveries);

            var lines = new List<string>();
            if (inPerson != null) lines.Add("SettlementServices.Label.KnownServicesInPerson".Translate(inPerson));
            if (remote != null) lines.Add("SettlementServices.Label.KnownServicesRemote".Translate(remote));
            return lines.Count == 0 ? null : string.Join("\n", lines);
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

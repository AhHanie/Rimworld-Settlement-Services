using RimWorld;
using UnityEngine;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Settlement_Services.Framework.Compatibility;

namespace Settlement_Services.Framework.Compat.RimPacts
{
    internal sealed class RimPactsCompletionObserver : ICompatibilityCompletionObserver
    {
        private const int TrustRewardAmount = 1;
        private const string TrustReasonKey = "SettlementServices.RimPactsTrustReason.ServiceCompleted";
        private const string TrustCooldownScopePrefix = "serviceTrust:";

        public void OnCompleted(CompatibilityCompletionContext context)
        {
            CompatibilitySettingsStore settings = ModSettings.Current.compatibilitySettings;
            if (!settings.GetBool(RimPactsSettingsSection.TrustRewardsEnabledKey, true)) return;

            ServiceJobRecord job = context.job;
            if (job == null || job.status == ServiceJobStatus.Failed || job.acceptedQuote == null) return;
            if (job.providerFactionLoadId.NullOrEmpty()) return;

            int cooldownDays = Mathf.Clamp(settings.GetInt(RimPactsSettingsSection.TrustRewardCooldownDaysKey, 10), 1, 60);
            int cooldownTicks = cooldownDays * GenDate.TicksPerDay;

            string scopeKey = TrustCooldownScopePrefix + job.providerFactionLoadId;
            if (!context.domain.CompatibilityWorldState.TryAcquireCooldown(RimPactsCompatibilityModule.Id, scopeKey, cooldownTicks)) return;

            Faction provider = FactionLookup.ResolveFaction(job.providerFactionLoadId);
            if (provider == null) return;

            RimPactsAdapter.TryAwardServiceTrust(provider, TrustRewardAmount, TrustReasonKey);
        }
    }
}

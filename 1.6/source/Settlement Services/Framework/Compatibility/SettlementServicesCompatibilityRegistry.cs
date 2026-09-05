using System;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Compatibility
{
    internal static class SettlementServicesCompatibilityRegistry
    {
        private struct QuoteModifierEntry
        {
            public string moduleId;
            public ICompatibilityQuoteModifier modifier;
        }

        private struct CompletionObserverEntry
        {
            public string moduleId;
            public ICompatibilityCompletionObserver observer;
        }

        private static bool initialized;

        private static ICompatibilityAvailabilityRule[] availabilityRules = Array.Empty<ICompatibilityAvailabilityRule>();
        private static QuoteModifierEntry[] quoteModifiers = Array.Empty<QuoteModifierEntry>();
        private static CompletionObserverEntry[] completionObservers = Array.Empty<CompletionObserverEntry>();
        private static ICompatibilitySettingsSection[] settingsSections = Array.Empty<ICompatibilitySettingsSection>();
        private static ICompatibilityCustodyLifecycle[] custodyLifecycles = Array.Empty<ICompatibilityCustodyLifecycle>();

        internal static bool HasSettingsSections => settingsSections.Length > 0;

        internal static void Initialize()
        {
            if (initialized) return;
            initialized = true;

            var availabilityRuleList = new List<ICompatibilityAvailabilityRule>();
            var quoteModifierList = new List<QuoteModifierEntry>();
            var completionObserverList = new List<CompletionObserverEntry>();
            var settingsSectionList = new List<ICompatibilitySettingsSection>();
            var custodyLifecycleList = new List<ICompatibilityCustodyLifecycle>();

            foreach (ISettlementServicesCompatibilityModule module in CompatibilityModuleCatalog.CreateModules())
            {
                if (!module.TryInitialize()) continue;

                if (module.AvailabilityRule != null) availabilityRuleList.Add(module.AvailabilityRule);
                if (module.QuoteModifier != null) quoteModifierList.Add(new QuoteModifierEntry { moduleId = module.ModuleId, modifier = module.QuoteModifier });
                if (module.CompletionObserver != null) completionObserverList.Add(new CompletionObserverEntry { moduleId = module.ModuleId, observer = module.CompletionObserver });
                if (module.SettingsSection != null) settingsSectionList.Add(module.SettingsSection);
                if (module.CustodyLifecycle != null) custodyLifecycleList.Add(module.CustodyLifecycle);
            }

            quoteModifierList.Sort((a, b) =>
            {
                int orderCompare = a.modifier.Order.CompareTo(b.modifier.Order);
                return orderCompare != 0 ? orderCompare : string.CompareOrdinal(a.moduleId, b.moduleId);
            });

            availabilityRules = availabilityRuleList.ToArray();
            quoteModifiers = quoteModifierList.ToArray();
            completionObservers = completionObserverList.ToArray();
            settingsSections = settingsSectionList.ToArray();
            custodyLifecycles = custodyLifecycleList.ToArray();
        }

        internal static bool TryGetCustodyLifecycle(ServiceJobContext context, Thing thing, out ICompatibilityCustodyLifecycle lifecycle)
        {
            for (int i = 0; i < custodyLifecycles.Length; i++)
            {
                if (custodyLifecycles[i].Handles(context, thing))
                {
                    lifecycle = custodyLifecycles[i];
                    return true;
                }
            }
            lifecycle = null;
            return false;
        }

        internal static string GetRequestBlockReason(Settlement settlement)
        {
            for (int i = 0; i < availabilityRules.Length; i++)
            {
                string reason = availabilityRules[i].GetBlockReason(settlement);
                if (reason != null) return reason;
            }
            return null;
        }

        internal static void ModifyQuote(CompatibilityQuoteContext context)
        {
            for (int i = 0; i < quoteModifiers.Length; i++)
            {
                quoteModifiers[i].modifier.Modify(context);
                context.totalCost = Mathf.Max(context.def.minimumCost, context.totalCost);
            }
        }

        internal static void NotifyCompleted(CompatibilityCompletionContext context)
        {
            for (int i = 0; i < completionObservers.Length; i++)
            {
                CompletionObserverEntry entry = completionObservers[i];
                if (!context.job.TryMarkCompatibilityCompletionProcessed(entry.moduleId)) continue;
                entry.observer.OnCompleted(context);
            }
        }

        internal static void DrawSettingsSections(Listing_Standard listing)
        {
            CompatibilitySettingsStore settings = Settlement_Services.ModSettings.Current.compatibilitySettings;
            for (int i = 0; i < settingsSections.Length; i++)
                settingsSections[i].Draw(listing, settings);
        }
    }
}

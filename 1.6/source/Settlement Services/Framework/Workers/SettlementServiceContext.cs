using System;
using System.Collections.Generic;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;

namespace Settlement_Services.Framework.Workers
{
    public struct SettlementServiceContext
    {
        public SettlementServicesWorldComponent Domain { get; }
        public Settlement Settlement { get; }
        public ServiceJobRecord Job { get; }

        public Thing SelectedTarget { get; }

        public Caravan RequestingCaravan { get; }

        public IReadOnlyList<string> SelectedOptionKeys { get; }

        public string SelectedTierKey { get; }

        public SettlementServiceContext(SettlementServicesWorldComponent domain, Settlement settlement, ServiceJobRecord job, Thing selectedTarget = null, Caravan requestingCaravan = null, IReadOnlyList<string> selectedOptionKeys = null, string selectedTierKey = null)
        {
            Domain = domain;
            Settlement = settlement;
            Job = job;
            SelectedTarget = selectedTarget;
            RequestingCaravan = requestingCaravan;
            SelectedOptionKeys = selectedOptionKeys ?? Array.Empty<string>();
            SelectedTierKey = selectedTierKey;
        }
    }
}

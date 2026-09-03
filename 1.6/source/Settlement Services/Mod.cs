using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Settlement_Services.Framework.Compatibility;
using Settlement_Services.Framework.Registry;
using Settlement_Services.Services.Hospitality;

namespace Settlement_Services
{
    public class Mod : Verse.Mod
    {
        public Mod(ModContentPack content) : base(content)
        {
            LongEventHandler.QueueLongEvent(Init, "SettlementServices.LoadingLabel", doAsynchronously: true, null);
        }

        private void Init()
        {
            GetSettings<ModSettings>();
            new Harmony("sk.settlementservices").PatchAll();
            SettlementServiceRegistry.ValidateAll();
            Settlement_Services.Framework.Events.ServiceEventRegistry.ValidateAll();
            HospitalityPackageRegistry.ValidateAll();
            LongEventHandler.ExecuteWhenFinished(SettlementServicesCompatibilityRegistry.Initialize);
        }

        public override string SettingsCategory()
        {
            return "SettlementServices.SettingsTitle".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            ModSettingsWindow.Draw(inRect);
            base.DoSettingsWindowContents(inRect);
        }
    }
}

using UnityEngine;
using Verse;

namespace Settlement_Services.UI
{
    [StaticConstructorOnStartup]
    internal static class ServiceUITextures
    {
        public static readonly Texture2D ServicesCommand = ContentFinder<Texture2D>.Get("SettlementServices/UI/Icons/Commands/trade");
        public static readonly Texture2D InvestCommand = ContentFinder<Texture2D>.Get("SettlementServices/UI/Icons/Commands/invest");
        public static readonly Texture2D CollectCommand = ContentFinder<Texture2D>.Get("UI/Commands/FulfillTradeRequest");
        public static readonly Texture2D ViewMap = ContentFinder<Texture2D>.Get("UI/Commands/ShowMap");
        public static readonly Texture2D LandExit = ContentFinder<Texture2D>.Get("SettlementServices/UI/Icons/exit");

        public static Texture2D Resolve(string iconTexPath) =>
            iconTexPath.NullOrEmpty() ? null : ContentFinder<Texture2D>.Get(iconTexPath, reportFailure: false);
    }
}

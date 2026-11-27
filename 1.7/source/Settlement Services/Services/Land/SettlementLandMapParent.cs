using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain;
using Settlement_Services.Domain.Records;
using Verse;

namespace Settlement_Services.Services.Land
{
    public sealed class SettlementLandMapParent : PocketMapParent
    {
        internal int plotId;
        internal int hostId = -1;
        internal int transfers;
        internal bool terrainGenerated;
        internal PlanetTile departureTile = PlanetTile.Invalid;
        private bool occupancyDirty = true;
        private bool occupied;
        private int nextOccupancyCheck;
        private int vacantSince = -1;

        internal LandPlotRecord Contract => SettlementServicesWorldComponent.Current?.LandPlotForParent(this);

        internal static SettlementLandMapParent For(Map map) => map?.Parent as SettlementLandMapParent;
        internal static bool IsPlot(Map map) => For(map) != null;

        internal bool MatchesPlot(LandPlotRecord plot) => plotId == plot.id && (hostId < 0 || hostId == plot.hostId);

        internal bool MatchesPlotMap(LandPlotRecord plot, Map map) => MatchesPlot(plot)
            && map is { Disposed: false, IsPocketMap: true } && map.Parent == this
            && map.Size.x == plot.side && map.Size.z == plot.side;

        internal static bool Frozen(Map map)
        {
            var parent = For(map);
            return parent != null && Current.ProgramState == ProgramState.Playing
                && parent.transfers == 0 && !parent.Occupied(map);
        }

        internal static bool RecoveryOrdersOnly(Map map)
        {
            var parent = For(map);
            if (parent == null) return false;
            LandPlotRecord plot = parent.Contract;
            return plot == null || plot.recoveryError || plot.RecoveryOnly(LandPlotPolicy.Now) || plot.Due(LandPlotPolicy.Now)
                || LandPlotService.HostOrdersBlocked(plot);
        }

        internal void InvalidateOccupancy()
        {
            occupancyDirty = true;
            if (Current.ProgramState == ProgramState.Playing && Find.UIRoot is UIRoot_Play root)
                root.mapUI?.colonistBar?.MarkColonistsDirty();
        }

        internal int PlantClock(int now) => vacantSince < 0 ? now : vacantSince;

        private bool Occupied(Map map)
        {
            int now = Find.TickManager.TicksGame;
            if (occupancyDirty || now >= nextOccupancyCheck)
            {
                occupied = LivingPawns(map).Any();
                if (occupied && vacantSince >= 0)
                {
                    LandPlotPatches.ResumeLeaflessPlants(map, vacantSince, now);
                    vacantSince = -1;
                }
                else if (!occupied && vacantSince < 0) vacantSince = now;
                occupancyDirty = false;
                nextOccupancyCheck = now + 250;
            }
            return occupied;
        }

        internal static IEnumerable<Pawn> LivingPawns(Map map) =>
            map.mapPawns.AllPawns.Where(p => p != null && !p.Dead && !p.Destroyed);

        internal static CellRect ArrivalArea(Map map) => CellRect.CenteredOn(map.Center, 1);

        public override bool ShouldRemoveMapNow(out bool alsoRemoveWorldObject)
        {
            alsoRemoveWorldObject = false;
            return false;
        }

        public override AcceptanceReport CanBeSettled => false;

        public override IEnumerable<IncidentTargetTagDef> IncidentTargetTags() { yield break; }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref plotId, "settlementLandPlotId");
            Scribe_Values.Look(ref hostId, "settlementLandHostId", -1);
            Scribe_Values.Look(ref departureTile, "settlementLandDepartureTile", PlanetTile.Invalid);
            Scribe_Values.Look(ref vacantSince, "settlementLandVacantSince", -1);
        }
    }
}

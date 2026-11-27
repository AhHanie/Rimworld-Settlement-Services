using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Settlement_Services.Domain.Records;
using Settlement_Services.Services.Land;
using Settlement_Services.UI;
using Verse;

namespace Settlement_Services.Domain
{
    public partial class SettlementServicesWorldComponent
    {
        private int nextLandPlotId = 1;
        private List<LandPlotRecord> landPlots = new();
        private readonly Dictionary<int, LandPlotRecord> landPlotsByHost = new();
        private readonly Dictionary<SettlementLandMapParent, LandPlotRecord> landPlotsByParent = new();
        private long nextLandTransition = long.MaxValue;

        internal bool LandPlotsReady => EnsureLoadReconciled();
        internal LandPlotRecord LandPlotAtHost(int hostId) =>
            LandPlotsReady && landPlotsByHost.TryGetValue(hostId, out var plot) ? plot : null;
        internal LandPlotRecord LandPlotForParent(SettlementLandMapParent parent)
        {
            if (!LandPlotsReady) return null;
            if (landPlotsByParent.TryGetValue(parent, out var plot)) return plot;
            // First entry attaches a new parent after the entitlement was already registered.
            plot = landPlots.FirstOrDefault(candidate => candidate.parent == parent && parent.MatchesPlot(candidate));
            if (plot != null) landPlotsByParent.Add(parent, plot);
            return plot;
        }

        internal IEnumerable<LandPlotRecord> LandPlotsForVisit(PlanetTile tile) =>
            !LandPlotsReady ? Enumerable.Empty<LandPlotRecord>() : landPlots.Where(plot =>
                (plot.recoveryError || !plot.Due(LandPlotPolicy.Now)) && tile == LandPlotTravel.RecoveryTile(plot.hostTile));

        private void ExposeLandPlots()
        {
            Scribe_Values.Look(ref nextLandPlotId, "nextLandPlotId", 1);
            Scribe_Collections.Look(ref landPlots, "landPlots", LookMode.Deep);
        }

        internal LandPlotRecord AddLandPlot(Settlement host, int side, bool bunkerInterior, bool rented, long paidThrough)
        {
            if (nextLandPlotId <= 0 || nextLandPlotId == int.MaxValue) throw new OverflowException("Land plot ID exhausted");
            var plot = new LandPlotRecord
            {
                id = nextLandPlotId++, hostId = host.ID, hostLabel = host.LabelCap, hostTile = host.Tile,
                side = side, bunkerInterior = bunkerInterior, rented = rented, createdTick = LandPlotPolicy.Now,
                paidThrough = paidThrough, graceThrough = rented ? LandPlotPolicy.GraceThrough(paidThrough) : 0,
                warned = rented && paidThrough - LandPlotPolicy.Now <= GenDate.TicksPerDay
            };
            landPlotsByHost.Add(host.ID, plot);
            landPlots.Add(plot);
            RescheduleLandPlots();
            return plot;
        }

        internal void SetLandPlotTerm(LandPlotRecord plot, long paidThrough)
        {
            plot.paidThrough = paidThrough;
            plot.graceThrough = LandPlotPolicy.GraceThrough(paidThrough);
            plot.warned = paidThrough - LandPlotPolicy.Now <= GenDate.TicksPerDay;
            plot.expiredNotice = false;
            RescheduleLandPlots();
        }

        internal void RemoveLandPlot(LandPlotRecord plot)
        {
            landPlots.Remove(plot);
            if (landPlotsByHost.TryGetValue(plot.hostId, out var indexed) && indexed == plot) landPlotsByHost.Remove(plot.hostId);
            if (plot.parent != null) landPlotsByParent.Remove(plot.parent);
            RescheduleLandPlots();
        }

        internal void RelinquishLandPlot(LandPlotRecord plot)
        {
            if (LandPlotAtHost(plot.hostId) != plot || plot.recoveryError || !plot.hostLost || plot.rented) return;
            plot.relinquished = true;
            RescheduleLandPlots();
        }

        internal void NotifyLandHostDestroyed(int hostId)
        {
            LandPlotRecord plot = LandPlotAtHost(hostId);
            if (plot == null || plot.hostLost) return;
            plot.hostLost = true;
            SendLandLetter(plot, "HostLost", LetterDefOf.NeutralEvent);
        }

        private void ReconcileLandPlots()
        {
            landPlots ??= new List<LandPlotRecord>();
            landPlots.RemoveAll(plot => plot == null);
            landPlotsByHost.Clear();
            landPlotsByParent.Clear();
            var registered = Find.World.pocketMaps.OfType<SettlementLandMapParent>().ToHashSet();
            var parents = registered.Concat(Find.Maps.Select(SettlementLandMapParent.For).OfType<SettlementLandMapParent>()).ToHashSet();
            var byId = landPlots.ToLookup(plot => plot.id);
            var byHost = landPlots.ToLookup(plot => plot.hostId);
            var parentsById = parents.ToLookup(parent => parent.plotId);

            foreach (LandPlotRecord plot in landPlots)
            {
                Settlement host = WorldObjectLookup.ResolveSettlement(plot.hostId);
                if (host != null)
                {
                    if (string.IsNullOrEmpty(plot.hostLabel)) plot.hostLabel = host.LabelCap;
                    if (!plot.hostTile.Valid) plot.hostTile = host.Tile;
                }
                else if (!plot.hostLost)
                {
                    plot.hostLost = true;
                    plot.hostLabel ??= (string)(LandPlotService.Key + "UnknownHost").Translate();
                    SendLandLetter(plot, "HostLost", LetterDefOf.NeutralEvent);
                }
                plot.hostLabel ??= (string)(LandPlotService.Key + "UnknownHost").Translate();
                if (plot.rented && plot.paidThrough > 0 && plot.graceThrough == 0)
                    plot.graceThrough = LandPlotPolicy.GraceThrough(plot.paidThrough);

                bool linked = plot.parent?.MatchesPlot(plot) == true;
                bool validMap = linked && parentsById[plot.id].Count() == 1
                    && registered.Contains(plot.parent) && plot.parent.MatchesPlotMap(plot, plot.Map);
                if (validMap) plot.generated = true;
                if (plot.id <= 0 || plot.hostId < 0 || !LandPlotPolicy.ValidSide(plot.side)
                    || byId[plot.id].Count() != 1 || byHost[plot.hostId].Count() != 1
                    || (plot.rented && (plot.paidThrough <= 0 || plot.graceThrough <= plot.paidThrough))
                    || ((plot.parent != null || plot.generated || parentsById.Contains(plot.id)) && !validMap))
                {
                    plot.recoveryError = true;
                    SupportLog.Error($"Land contract {plot.id}, host {plot.hostId}, parent {plot.parent?.ID}, map {plot.Map?.uniqueID} requires recovery.");
                }

                landPlotsByHost.TryAdd(plot.hostId, plot);
                if (linked)
                {
                    landPlotsByParent.TryAdd(plot.parent, plot);
                    if (plot.parent.hostId < 0) plot.parent.hostId = plot.hostId;
                    if (!plot.parent.departureTile.Valid) plot.parent.departureTile = plot.hostTile;
                }
                else if (parentsById[plot.id].Count() == 1 && byId[plot.id].Count() == 1)
                {
                    SettlementLandMapParent identifiable = parentsById[plot.id].Single();
                    if (!identifiable.departureTile.Valid) identifiable.departureTile = plot.hostTile;
                }
            }

            foreach (SettlementLandMapParent parent in parents)
            {
                parent.InvalidateOccupancy();
                if (!registered.Contains(parent) && !parent.Spawned) Find.World.pocketMaps.Add(parent);
                if (!landPlotsByParent.ContainsKey(parent))
                    SupportLog.Error($"Orphan settlement land parent {parent.ID}, map {parent.Map?.uniqueID}, plot {parent.plotId}, host {parent.hostId}, tile {parent.departureTile}.");
            }
            int highest = landPlots.Select(plot => plot.id).Concat(parents.Select(parent => parent.plotId)).DefaultIfEmpty().Max();
            nextLandPlotId = Math.Max(Math.Max(1, nextLandPlotId), highest == int.MaxValue ? int.MaxValue : highest + 1);
            RescheduleLandPlots();
        }

        internal void RescheduleLandPlots()
        {
            nextLandTransition = long.MaxValue;
            long now = LandPlotPolicy.Now;
            foreach (LandPlotRecord plot in landPlots)
            {
                if (plot.recoveryError) continue;
                if (plot.Due(now))
                    nextLandTransition = Math.Min(nextLandTransition, now);
                else if (plot.rented)
                {
                    long transition = !plot.warned && now < plot.paidThrough ? plot.paidThrough - GenDate.TicksPerDay
                        : !plot.expiredNotice ? plot.paidThrough : plot.graceThrough;
                    nextLandTransition = Math.Min(nextLandTransition, transition);
                }
            }
        }

        // Run after all map and thing tick lists for this tick have finished.
        internal void ProcessLandTransitions()
        {
            if (!LandPlotsReady) return;
            long now = LandPlotPolicy.Now;
            if (now < nextLandTransition) return;
            bool retryReclamation = false;
            foreach (LandPlotRecord plot in landPlots.ToList())
            {
                if (plot.recoveryError) continue;
                if (plot.Due(now))
                {
                    if (Dialog_SettlementLand.IsManaging(plot) || LandPlotPatches.IsPacking(plot.Map) || (plot.parent?.transfers ?? 0) > 0)
                    {
                        retryReclamation = true;
                        continue;
                    }
                    try
                    {
                        if (plot.parent != null && !LandPlotTravel.Evacuate(plot.parent))
                        {
                            retryReclamation = true;
                            continue;
                        }
                        Map map = plot.Map;
                        int stacks = map?.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).Count ?? 0;
                        int buildings = map?.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial).Count ?? 0;
                        if (plot.parent != null && !LandPlotTravel.TryRemoveMap(plot.parent))
                        {
                            retryReclamation = true;
                            continue;
                        }
                        RemoveLandPlot(plot);
                        Find.LetterStack.ReceiveLetter((LandPlotService.Key + "ReclaimedTitle").Translate(),
                            (LandPlotService.Key + "ReclaimedBody").Translate(plot.id, plot.hostLabel, stacks, buildings),
                            LetterDefOf.NegativeEvent);
                    }
                    catch (Exception ex)
                    {
                        SupportLog.Error($"Land plot {plot.id} reclamation failed: {ex}");
                        retryReclamation = true;
                    }
                    continue;
                }
                if (!plot.rented) continue;
                if (!plot.expiredNotice && now >= plot.paidThrough)
                {
                    plot.expiredNotice = true;
                    SendLandLetter(plot, "Expired", LetterDefOf.NegativeEvent);
                }
                else if (!plot.warned && now < plot.paidThrough && now >= plot.paidThrough - GenDate.TicksPerDay)
                {
                    plot.warned = true;
                    SendLandLetter(plot, "Warning", LetterDefOf.NeutralEvent);
                }
            }
            RescheduleLandPlots();
            if (retryReclamation && nextLandTransition <= now) nextLandTransition = now + 60;
        }

        private static void SendLandLetter(LandPlotRecord plot, string notice, LetterDef letter)
        {
            string key = LandPlotService.Key + notice;
            long deadline = notice == "Warning" ? plot.paidThrough : plot.graceThrough;
            Find.LetterStack.ReceiveLetter((key + "Title").Translate(),
                (key + "Body").Translate(plot.id, plot.hostLabel, LandPlotPolicy.LocalTime(deadline, plot.hostTile)), letter);
        }
    }
}

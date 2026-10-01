using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CAD2Revit.Core;
using Settings = CAD2Revit.Core.Settings;
using FamilyInstanceCreationData = Autodesk.Revit.Creation.FamilyInstanceCreationData;

namespace CAD2Revit.Revit
{
    /// <summary>
    /// Places Revit family instances at CAD block locations.
    ///
    /// Everything happens inside ONE Transaction (one Ctrl+Z undoes the whole run, Cancel
    /// rolls it back). Preview runs exactly the same code and rolls back at the end, so its
    /// counts match what Run will do.
    ///
    /// Performance (see the Timings table in the result window):
    /// - family types are activated once, with one regeneration, before the loop;
    /// - hosts are looked up in face/wall indexes built once (HostFinder), not by one ray per block;
    /// - the duplicate check uses one spatial grid of the existing instances;
    /// - level-based rows are created in batches with NewFamilyInstances2 (rotation in the
    ///   creation data); other rows are created one by one, each in its own SubTransaction so
    ///   one bad block is rolled back on its own;
    /// - Comments / level / elevation parameters are set in one pass at the end;
    /// - no Regenerate inside the loop, and warnings are deleted by a failures preprocessor.
    /// </summary>
    public class Placer
    {
        const double MmPerFoot = 304.8;
        static double Ft(double mm) => mm / MmPerFoot;

        readonly Document _doc;
        readonly Settings _settings;
        SlabOptions _slab = new SlabOptions();   // Slab (above) range / fallback, from the mapping
        PhaseTimer _timer = new PhaseTimer();

        /// <summary>false = "Place anyway": elements already in the model are ignored (blocks
        /// repeated at the same spot within this run are still placed once).</summary>
        public bool CheckDuplicates = true;

        public Placer(Document doc, Settings settings)
        {
            _doc = doc;
            _settings = settings;
        }

        /// <summary>Case-insensitive (family, type) -> FamilySymbol lookup for every mapped row.
        /// Returns error messages for rows whose family/type is not loaded.</summary>
        public static Dictionary<MapRow, FamilySymbol> ResolveSymbols(Document doc, MappingResult mapping, List<string> errors)
        {
            var loaded = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
                loaded[s.FamilyName.Trim() + "\u0001" + s.Name.Trim()] = s;
            var result = new Dictionary<MapRow, FamilySymbol>();
            foreach (var row in mapping.Rows.Values.OrderBy(r => r.Line))
            {
                if (loaded.TryGetValue(row.Family + "\u0001" + row.TypeName, out var sym))
                    result[row] = sym;
                else
                    errors.Add($"Row {row.Line}: family '{row.Family}' / type '{row.TypeName}' is not loaded in the project");
            }
            return result;
        }

        /// <summary>Per-level helpers (a run can place rows on several levels).</summary>
        class LevelContext
        {
            public Level Level;
            public double MaxUp;
            public double? NextZ;       // elevation of the level above (null = top level)
            public double SlabDown;     // Slab (below) search distance
            public double BandMin, BandMax;   // duplicate-check height band of this level
            public LevelPlanes LevelPlanes;
            public VerticalPlanes VerticalPlanes;
        }

        /// <summary>What step 1 (host detection) decided for one block.</summary>
        class Plan
        {
            public PlacementResult Failed;
            public HostHit Hit;
            public bool OnVertical, OnLevelPlane;
            public double PlaneElevMm;
            public Facing PlaneFacing;
            public List<string> Notes;
            public double Angle;
            public string FinalHost;     // what the placed instance is hosted on (DebugHosting)
            public double? LevelElevMm;  // level-based: Elevation From Level instead of the row's (Slab above)
            public bool SlabFallback;    // Slab (above): no slab in range, fallback plane / height used
            public SlabHost SlabHost;    // Slab (above): what the block ends up on
            public List<string> Review = new List<string>();   // Needs Review reasons (walls)
            public PlacementResult Fail(PlacementResult r)
            {
                Failed = r;
                return r;
            }
        }

        /// <summary>A level-based block waiting for batch creation.</summary>
        class Pending
        {
            public BlockRef Block;
            public PlacementResult Result;
            public XYZ Base;     // on the level
            public double Angle;
            public double? Offset;   // feet; overrides the row's Elevation From Level (Slab above)
        }

        /// <summary>An instance whose parameters are set in the final pass.</summary>
        class Created
        {
            public ElementId Id;
            public Level Level;
            public string BlockName;
            public double? Offset;          // level-based: Elevation From Level (feet)
            public PlacementResult Result;
            public XYZ CadPoint;            // the CAD block insertion point (final distance check)
        }

        /// <summary>Places every mapped block. Each row goes on its own Level (MapRow.LevelName,
        /// or <paramref name="defaultLevel"/> when empty). dryRun = true rolls everything back.
        /// <paramref name="progress"/> (done, total) returns false to cancel: everything is rolled
        /// back and OperationCanceledException is thrown.</summary>
        public List<PlacementResult> PlaceAll(List<BlockRef> blocks, MappingResult mapping,
                                              Dictionary<MapRow, FamilySymbol> symbols, Level defaultLevel,
                                              bool dryRun, Func<int, int, bool> progress = null, double[] dwgExtents = null,
                                              PhaseTimer timer = null)
        {
            _timer = timer ?? new PhaseTimer();
            _slab = mapping.Slab ?? new SlabOptions();
            var results = new List<PlacementResult>();
            var mapped = new List<(BlockRef, MapRow)>();
            var unmapped = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in blocks)
            {
                if (mapping.Rows.TryGetValue(b.Name, out var row)) mapped.Add((b, row));
                else unmapped[b.Name] = (unmapped.TryGetValue(b.Name, out var n) ? n : 0) + 1;
            }
            foreach (var kv in unmapped)
                results.Add(new PlacementResult
                {
                    BlockName = kv.Key, Status = Status.Unmapped, Message = "not mapped (Skip)",
                    Count = kv.Value, HasBlock = false,
                });

            var levelsByName = new Dictionary<string, Level>(StringComparer.OrdinalIgnoreCase);
            var levelZs = new List<double>();
            foreach (var l in new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>())
            {
                if (!levelsByName.ContainsKey(l.Name)) levelsByName[l.Name] = l;
                levelZs.Add(l.ProjectElevation);
            }

            using (var t = new Transaction(_doc, "CAD2Revit: Place families"))
            {
                var opts = t.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(new WarningSwallower());
                opts.SetClearAfterRollback(true);
                opts.SetForcedModalHandling(false);   // never block on a failure dialog
                opts.SetDelayedMiniWarnings(true);
                t.SetFailureHandlingOptions(opts);
                t.Start();
                try
                {
                    // Activate every family type needed, once, then regenerate once.
                    using (_timer.Time(Phases.Activate))
                    {
                        bool activated = false;
                        foreach (var sym in mapped.Select(m => symbols.TryGetValue(m.Item2, out var s) ? s : null)
                                                  .Where(s => s != null).Distinct())
                            if (!sym.IsActive)
                            {
                                sym.Activate();
                                activated = true;
                            }
                        if (activated) _doc.Regenerate();
                    }

                    // The temporary 3D view (ray-cast fallback, reference planes without a section
                    // view) is always created here, outside the per-block sub-transactions: a view
                    // created inside one would be deleted again if that block were rolled back.
                    View3D view = null;
                    View3D TempView() => view != null && view.IsValidObject ? view : (view = HostFinder.CreateTempView(_doc));
                    var modes = mapped.Select(m => m.Item2.Host).Distinct().ToList();
                    HostFinder finder = null;
                    if (modes.Any(m => m == HostMode.Ceiling || m == HostMode.Face || m == HostMode.Wall ||
                                       m == HostMode.SlabAbove || m == HostMode.SlabBelow))
                    {
                        using (_timer.Time(Phases.HostIndex)) TempView();
                        finder = new HostFinder(_doc, view, _settings.SearchRevitLinks, _timer);
                        finder.Prepare(modes);
                    }
                    var section = LevelPlanes.SectionView(_doc);
                    if (section == null && modes.Contains(HostMode.RefPlane)) TempView();
                    var extents = dwgExtents ?? BlockExtents(blocks);
                    bool createdLevelPlanes = false;

                    // Duplicate check: one grid of all existing instances, built once.
                    ExistingIndex dups;
                    var familyOfType = new Dictionary<long, long>();
                    using (_timer.Time(Phases.DupIndex))
                        dups = CheckDuplicates ? BuildExisting(familyOfType) : new ExistingIndex(Ft(_settings.DuplicateToleranceMm));

                    var contexts = new Dictionary<long, LevelContext>();
                    LevelContext ContextFor(Level level)
                    {
                        long key = Compat.IdValue(level.Id);
                        if (contexts.TryGetValue(key, out var c)) return c;
                        double levelZ = level.ProjectElevation;
                        var above = levelZs.Where(z => z > levelZ + 0.01).ToList();
                        double? top = above.Count > 0 ? above.Min() : (double?)null;
                        double maxUp = Ft(_settings.HostSearchDistanceMm);
                        if (top.HasValue) maxUp = Math.Min(maxUp, top.Value - levelZ);
                        c = new LevelContext
                        {
                            Level = level,
                            MaxUp = maxUp,
                            NextZ = top,
                            SlabDown = SlabSearch.BelowDistanceFt(_settings.SlabSearchToleranceMm),
                            BandMin = BandBottom(levelZ),
                            BandMax = BandTop(levelZ, top, maxUp),
                            LevelPlanes = new LevelPlanes(_doc, level, extents, () => { createdLevelPlanes = true; return section ?? (View)TempView(); }),
                            VerticalPlanes = new VerticalPlanes(_doc, level),
                        };
                        contexts[key] = c;
                        return c;
                    }

                    var batches = new Dictionary<(MapRow, long), List<Pending>>();
                    var batchLevel = new Dictionary<(MapRow, long), (Level, FamilySymbol)>();
                    var created = new List<Created>();

                    for (int i = 0; i < mapped.Count; i++)
                    {
                        if (progress != null && !progress(i, mapped.Count)) throw new OperationCanceledException();
                        var (b, row) = mapped[i];
                        var level = defaultLevel;
                        string levelNote = null;
                        if (!string.IsNullOrWhiteSpace(row.LevelName))
                        {
                            if (levelsByName.TryGetValue(row.LevelName.Trim(), out var rowLevel)) level = rowLevel;
                            else levelNote = $"level '{row.LevelName}' not found - placed on {defaultLevel.Name}";
                        }
                        PlacementResult res;
                        if (!symbols.TryGetValue(row, out var sym))
                        {
                            res = Result(b, row, Status.Skipped, "family/type not loaded", b.Point, b.Rotation);
                        }
                        else
                        {
                            var ctx = ContextFor(level);
                            res = PlaceOne(b, row, sym, level, finder, ctx, dups, familyOfType, batches, batchLevel, created);
                        }
                        res.Level = level.Name;
                        if (levelNote != null) res.Message = res.Message.Length > 0 ? levelNote + "; " + res.Message : levelNote;
                        results.Add(res);
                    }
                    progress?.Invoke(mapped.Count, mapped.Count);

                    // Level-based rows: one NewFamilyInstances2 call per (row, level).
                    foreach (var kv in batches)
                    {
                        var (lvl, sym) = batchLevel[kv.Key];
                        CreateBatch(kv.Value, sym, lvl, kv.Key.Item1, created);
                    }

                    // Parameters, in one pass.
                    using (_timer.Time(Phases.Params))
                        foreach (var c in created)
                        {
                            var inst = _doc.GetElement(c.Id);
                            if (inst == null) continue;
                            SetParam(inst, BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM, c.Level.Id);
                            if (c.Result.SlabFallback ||   // findable later with a filter or schedule on Comments
                                (c.Result.Review ?? "").StartsWith("No wall", StringComparison.Ordinal))
                                SetParam(inst, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,
                                         NeedsReview.CommentText + (_settings.WriteBlockNameToComments ? " | CAD: " + c.BlockName : ""));
                            else if (_settings.WriteBlockNameToComments)
                                SetParam(inst, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "CAD: " + c.BlockName);
                            if (c.Offset.HasValue && !SetOffset(inst, c.Offset.Value) && Math.Abs(c.Offset.Value) > 1e-9)
                                c.Result.Message = (c.Result.Message.Length > 0 ? c.Result.Message + "; " : "") + "could not set offset";
                        }

                    // Final check, every host type: an element placed farther than the review distance
                    // (50 mm by default, in plan) from its CAD block goes to Needs Review.
                    using (_timer.Time(Phases.Verify)) CheckDistances(created);

                    if (view != null && view.IsValidObject)
                    {
                        // Keep the temporary view only if a reference plane was created in it
                        // (deleting a view could take view-owned elements with it).
                        bool owned = createdLevelPlanes && new FilteredElementCollector(_doc).OfClass(typeof(ReferencePlane))
                            .Any(e => e.OwnerViewId == view.Id);
                        if (!owned) _doc.Delete(view.Id);
                    }
                    if (dryRun)
                    {
                        using (_timer.Time(Phases.Rollback)) t.RollBack();
                        foreach (var r in results) r.ElementId = null;
                    }
                    else
                    {
                        TransactionStatus status;
                        using (_timer.Time(Phases.Commit)) status = t.Commit();
                        if (status != TransactionStatus.Committed)
                            throw new InvalidOperationException($"Revit did not commit the transaction ({status}).");
                    }
                }
                catch (Exception)
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    throw;
                }
            }
            return results;
        }

        /// <summary>Every existing instance with a point location: family key, Comments and point.</summary>
        ExistingIndex BuildExisting(Dictionary<long, long> familyOfType)
        {
            var index = new ExistingIndex(Ft(_settings.DuplicateToleranceMm));
            foreach (var fi in new FilteredElementCollector(_doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
                if (fi.Location is LocationPoint lp)
                {
                    var comments = fi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
                    index.Add(DupKey(fi.GetTypeId(), familyOfType), comments, lp.Point.X, lp.Point.Y, lp.Point.Z);
                }
            return index;
        }

        static double BandBottom(double levelZ) => levelZ - Ft(300);

        /// <summary>Top of a level's duplicate-check band: up to the next level, and at least the
        /// Slab (above) / Ceiling range and fallback plane, so re-runs still see those instances.</summary>
        double BandTop(double levelZ, double? nextZ, double maxUp) =>
            Math.Max(nextZ ?? levelZ + Math.Max(maxUp, Ft(3000)),
                     levelZ + Math.Max(SlabSearch.RangeFt(_slab.SearchRangeMm), Ft(_slab.FallbackPlaneMm)) + Ft(1));

        /// <summary>
        /// Before Run: how many mapped blocks already have an instance at their location (same
        /// family, or Comments "CAD: &lt;block&gt;"), using the same tolerance and height band as the
        /// duplicate check. Read-only, no transaction.
        /// </summary>
        public int CountExisting(List<BlockRef> blocks, MappingResult mapping,
                                 Dictionary<MapRow, FamilySymbol> symbols, Level defaultLevel)
        {
            _slab = mapping.Slab ?? new SlabOptions();
            var levels = new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            var levelZs = levels.Select(l => l.ProjectElevation).ToList();
            var byName = new Dictionary<string, Level>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in levels) if (!byName.ContainsKey(l.Name)) byName[l.Name] = l;
            var familyOfType = new Dictionary<long, long>();
            var index = BuildExisting(familyOfType);
            if (index.Count == 0) return 0;
            var bands = new Dictionary<long, (double, double)>();
            int n = 0;
            foreach (var b in blocks)
            {
                if (!mapping.Rows.TryGetValue(b.Name, out var row) || !symbols.TryGetValue(row, out var sym)) continue;
                var level = !string.IsNullOrWhiteSpace(row.LevelName) && byName.TryGetValue(row.LevelName.Trim(), out var rl) ? rl : defaultLevel;
                long lk = Compat.IdValue(level.Id);
                if (!bands.TryGetValue(lk, out var band))
                {
                    double z = level.ProjectElevation;
                    var above = levelZs.Where(v => v > z + 0.01).ToList();
                    double? top = above.Count > 0 ? above.Min() : (double?)null;
                    double maxUp = Ft(_settings.HostSearchDistanceMm);
                    if (top.HasValue) maxUp = Math.Min(maxUp, top.Value - z);
                    bands[lk] = band = (BandBottom(z), BandTop(z, top, maxUp));
                }
                if (index.Contains(DupKey(sym.Id, familyOfType), b.Name, b.Point.X, b.Point.Y, band.Item1, band.Item2)) n++;
            }
            return n;
        }

        /// <summary>Duplicate-grid key: the family (or, with DuplicateSameTypeOnly, the type).</summary>
        long DupKey(ElementId typeId, Dictionary<long, long> familyOfType)
        {
            if (typeId == null || typeId == ElementId.InvalidElementId) return 0;
            long t = Compat.IdValue(typeId);
            if (_settings.DuplicateSameTypeOnly) return t;
            if (!familyOfType.TryGetValue(t, out var f))
            {
                f = _doc.GetElement(typeId) is FamilySymbol fs ? Compat.IdValue(fs.Family.Id) : t;
                familyOfType[t] = f;
            }
            return f;
        }

        static double[] BlockExtents(List<BlockRef> blocks) =>
            blocks.Count == 0 ? new[] { 0.0, 0, 0, 0 } : new[]
            {
                blocks.Min(b => b.Point.X), blocks.Min(b => b.Point.Y),
                blocks.Max(b => b.Point.X), blocks.Max(b => b.Point.Y),
            };

        static PlacementResult Result(BlockRef b, MapRow row, Status status, string message, XYZ point, double? rotation,
                                      ElementId id = null, string host = "")
        {
            var msg = message ?? "";
            if (b.Mirrored) msg = (msg.Length > 0 ? msg + "; " : "") + "CAD block is mirrored - check orientation";
            return new PlacementResult
            {
                BlockName = b.Name, Row = row, Status = status, Message = msg,
                ElementId = id != null ? Compat.IdValue(id) : (long?)null,
                Point = point != null ? new[] { point.X, point.Y, point.Z } : null,
                Rotation = rotation, Host = host ?? "",
                ScaleX = b.ScaleX, ScaleY = b.ScaleY, Mirrored = b.Mirrored,
            };
        }

        /// <summary>Step 1 - host detection for one block.</summary>
        Plan Decide(BlockRef b, MapRow row, FamilySymbol sym, Level level, HostFinder finder, LevelContext ctx)
        {
            var plan = new Plan();
            double maxUp = ctx.MaxUp;
            var ptype = sym.Family.FamilyPlacementType;
            double levelZ = level.ProjectElevation;
            double angle = b.Rotation + row.RotationDeg * Math.PI / 180.0;
            double offset = Ft(row.OffsetMm);
            double x = b.Point.X, y = b.Point.Y;
            var notes = new List<string>();
            plan.Notes = notes;
            plan.Angle = angle;
            string Notes(string extra = null)
            {
                if (!string.IsNullOrEmpty(extra)) notes.Add(extra);
                return string.Join("; ", notes);
            }

            // 1. Find a host if the row asks for one.
            HostHit hit = null;
            bool onVertical = false;   // face-based family on a vertical work plane (no wall)
            bool onLevelPlane = false; // face/work-plane based family on a horizontal reference plane
            double planeElevMm = row.OffsetMm;   // plane height above the level, and which way it faces
            var planeFacing = row.Facing;
            if (row.Host == HostMode.SlabAbove || row.Host == HostMode.Ceiling)
            {
                // Shared for Slab (above) and Ceiling: the nearest bottom face above the block
                // (Slab: floors, roofs, beams; Ceiling: ceilings), from the level up to the search
                // range - never higher, even if the level above is. Nothing in range: one reference
                // plane per level at the fallback height, facing down (level-based families: that
                // Elevation From Level).
                bool ceiling = row.Host == HostMode.Ceiling;
                double rangeMm = _slab.SearchRangeMm, planeMm = _slab.FallbackPlaneMm;
                hit = finder.FindUnderside(row.Host, x, y, levelZ, SlabSearch.RangeFt(rangeMm));
                string what = ceiling ? "ceiling"
                            : hit != null && Compat.IsCategory(hit.Element, BuiltInCategory.OST_StructuralFraming) ? "beam" : "slab";
                if (hit != null && hit.IsLinked && ptype == FamilyPlacementType.OneLevelBasedHosted)
                    { plan.Fail(Result(b, row, Status.Failed,
                        Notes($"{what} is in a Revit link; legacy hosted families can only be hosted in this model - " +
                              "use a face-based family"), b.Point, angle)); return plan; }
                if (hit != null) plan.SlabHost = ceiling ? SlabHost.Ceiling : what == "beam" ? SlabHost.Beam : SlabHost.Slab;
                if (hit != null && ptype == FamilyPlacementType.OneLevelBased)
                {
                    // A level-based family cannot sit on the face: same height, level-based.
                    plan.LevelElevMm = Math.Round((hit.Point.Z - levelZ) * MmPerFoot, 1);
                    plan.SlabHost = SlabHost.LevelBased;
                    notes.Add($"family is not face-based - placed level-based at the {what} underside ({plan.LevelElevMm:0} mm)");
                    hit = null;
                }
                else if (hit == null)
                {
                    if (ptype == FamilyPlacementType.OneLevelBasedHosted)
                        { plan.Fail(Result(b, row, Status.Failed, Notes(SlabSearch.FallbackReason(rangeMm, row.Host) + " - " +
                                                                       $"legacy hosted family needs a real {(ceiling ? "ceiling" : "slab")}"), b.Point, angle)); return plan; }
                    plan.SlabFallback = true;
                    bool onPlane = ptype == FamilyPlacementType.WorkPlaneBased;
                    if (onPlane)
                    {
                        planeElevMm = planeMm;
                        planeFacing = Facing.Down;
                        onLevelPlane = true;
                    }
                    else plan.LevelElevMm = planeMm;
                    plan.SlabHost = onPlane ? SlabHost.Plane : SlabHost.LevelBased;
                    notes.Add(SlabSearch.FallbackMessage(rangeMm, planeMm, levelBased: !onPlane, row.Host));
                }
            }
            else if (row.Host == HostMode.SlabBelow)
            {
                double maxDist = ctx.SlabDown;
                if (ptype == FamilyPlacementType.OneLevelBased)
                {
                    plan.Fail(Result(b, row, Status.Failed, Notes(HostCheck.NotFaceBased(ptype.ToString(), "slab")), b.Point, angle));
                    return plan;
                }
                hit = finder.FindSlab(false, x, y, levelZ, maxDist);
                if (hit != null && hit.IsLinked && ptype == FamilyPlacementType.OneLevelBasedHosted)
                    { plan.Fail(Result(b, row, Status.Failed,
                        Notes("slab is in a Revit link; legacy hosted families can only be hosted in this model - " +
                              "use a face-based family"), b.Point, angle)); return plan; }
                if (hit == null)
                {
                    string why = $"no slab below this point within {maxDist * MmPerFoot:0} mm (slab opening or no slab)";
                    if (ptype != FamilyPlacementType.WorkPlaneBased)
                        { plan.Fail(Result(b, row, Status.Failed, Notes(why + " - legacy hosted family needs a real slab"), b.Point, angle)); return plan; }
                    // Fallback: a reference plane at the top of this level's slab (or the level itself).
                    var slabZ = finder.FallbackSlabZ(false, levelZ, maxDist);
                    string source = slabZ.HasValue ? "top of the slab at this level" : "level (no slab found for this level)";
                    double z = slabZ ?? levelZ;
                    planeElevMm = Math.Round((z - levelZ) * MmPerFoot, 1);
                    planeFacing = Facing.Up;
                    onLevelPlane = true;
                    notes.Add($"WARNING: {why} - hosted on a reference plane at {planeElevMm:0} mm ({source})");
                }
            }
            else if (row.Host == HostMode.RefPlane)
            {
                if (ptype == FamilyPlacementType.WorkPlaneBased) onLevelPlane = true;
                else if (ptype == FamilyPlacementType.OneLevelBased)
                    notes.Add("WARNING: family is not face-based/work-plane-based, so it cannot be hosted on a " +
                              "reference plane - placed level-based at the elevation");
                else if (ptype == FamilyPlacementType.OneLevelBasedHosted)
                    { plan.Fail(Result(b, row, Status.Failed, Notes("WARNING: legacy wall/ceiling-hosted family cannot be hosted on a " +
                                                                "reference plane or placed level-based - use a face-based family"), b.Point, angle)); return plan; }
            }
            else if (row.Host == HostMode.Vertical)
            {
                if (ptype == FamilyPlacementType.WorkPlaneBased) onVertical = true;
                else if (ptype == FamilyPlacementType.OneLevelBased)
                    notes.Add("family is not face-based - placed level-based");
                else if (ptype == FamilyPlacementType.OneLevelBasedHosted)
                    { plan.Fail(Result(b, row, Status.Failed, Notes("legacy wall-hosted family needs a real wall - use Host Type " +
                                                                "'wall' or a face-based family"), b.Point, angle)); return plan; }
            }
            else if (row.Host != HostMode.None)
            {
                string hostWord = row.Host.ToString().ToLowerInvariant();
                if (ptype == FamilyPlacementType.OneLevelBased && row.Host != HostMode.Wall)
                {
                    // Ceiling / face: only a face-based family can sit on the face.
                    plan.Fail(Result(b, row, Status.Failed, Notes(HostCheck.NotFaceBased(ptype.ToString(), HostCheck.What(row.Host))),
                                     b.Point, angle));
                    return plan;
                }
                if (ptype == FamilyPlacementType.OneLevelBased)
                {
                    notes.Add("family is not face/wall-hosted - placed level-based");
                }
                else
                {
                    string where;
                    if (row.Host == HostMode.Wall)
                    {
                        // Walls that exist at the target height; the family goes at level + elevation.
                        double searchZ = levelZ + Math.Max(offset, Ft(10));
                        double wallMm = _slab.WallSearchMm;
                        hit = finder.FindWall(x, y, searchZ, levelZ + offset, Ft(wallMm), b.Rotation, () => DwgReader.SymbolCentre(b));
                        where = $"within {wallMm:0} mm";
                        if (hit == null) plan.Review.Add(WallPlacement.NoWallReason(wallMm));
                        else
                        {
                            if (hit.MovedFt * MmPerFoot > WallPlacement.MoveReviewMm)
                            {
                                plan.Review.Add(WallPlacement.MovedReason);
                                notes.Add($"moved {hit.MovedFt * MmPerFoot:0} mm to reach the wall face");
                            }
                            if (hit.IsLinked) plan.Review.Add(WallPlacement.LinkedReason);
                            if (hit.InsideWall) notes.Add("CAD point is inside the wall - side taken from the block symbol");
                        }
                    }
                    else
                    {
                        hit = finder.FindAbove(row.Host, x, y, levelZ, maxUp);
                        where = $"within {maxUp * MmPerFoot:0} mm above the level";
                    }
                    if (hit != null && hit.IsLinked && ptype == FamilyPlacementType.OneLevelBasedHosted)
                        { plan.Fail(Result(b, row, Status.Failed,
                            Notes("host is in a Revit link; legacy wall/ceiling-hosted families can only be hosted " +
                                  "in this model - use a face-based family"), b.Point, angle)); return plan; }
                    if (hit == null)
                    {
                        var msg = $"no {hostWord} found {where}";
                        if (!_settings.FallbackToUnhosted || ptype == FamilyPlacementType.OneLevelBasedHosted)
                            { plan.Fail(Result(b, row, Status.Failed, Notes(msg), b.Point, angle)); return plan; }
                        if (row.Host == HostMode.Wall && ptype == FamilyPlacementType.WorkPlaneBased)
                        {
                            onVertical = true;   // stand it up on a vertical plane instead of lying flat
                            notes.Add(msg + " - placed on a vertical plane");
                        }
                        else notes.Add(msg + " - placed unhosted");
                    }
                }
            }


            plan.Hit = hit;
            plan.OnVertical = onVertical;
            plan.OnLevelPlane = onLevelPlane;
            plan.PlaneElevMm = planeElevMm;
            plan.PlaneFacing = planeFacing;
            return plan;
        }

        PlacementResult PlaceOne(BlockRef b, MapRow row, FamilySymbol sym, Level level, HostFinder finder, LevelContext ctx,
                                 ExistingIndex dups, Dictionary<long, long> familyOfType,
                                 Dictionary<(MapRow, long), List<Pending>> batches,
                                 Dictionary<(MapRow, long), (Level, FamilySymbol)> batchLevel, List<Created> created)
        {
            Plan plan;
            using (_timer.Time(Phases.HostQuery))
                plan = Decide(b, row, sym, level, finder, ctx);
            if (plan.Failed != null) return WithDebug(plan.Failed, plan);
            var res0 = PlaceOne(b, row, sym, level, finder, ctx, dups, familyOfType, batches, batchLevel, created, plan);
            if (plan.SlabFallback) res0.SlabFallback = true;
            if (res0.Status == Status.Placed && plan.Review.Count > 0) res0.Review = string.Join("; ", plan.Review.Distinct());
            if (res0.Status == Status.Placed) res0.SlabHost = plan.SlabHost;
            return res0;
        }

        PlacementResult PlaceOne(BlockRef b, MapRow row, FamilySymbol sym, Level level, HostFinder finder, LevelContext ctx,
                                 ExistingIndex dups, Dictionary<long, long> familyOfType,
                                 Dictionary<(MapRow, long), List<Pending>> batches,
                                 Dictionary<(MapRow, long), (Level, FamilySymbol)> batchLevel, List<Created> created, Plan plan)
        {
            var ptype = sym.Family.FamilyPlacementType;
            double levelZ = level.ProjectElevation;
            double offset = plan.LevelElevMm.HasValue ? Ft(plan.LevelElevMm.Value) : Ft(row.OffsetMm);
            double x = b.Point.X, y = b.Point.Y;
            double angle = plan.Angle;
            var notes = plan.Notes;
            var hit = plan.Hit;
            string Notes(string extra = null)
            {
                if (!string.IsNullOrEmpty(extra)) notes.Add(extra);
                return string.Join("; ", notes);
            }

            // 2. Target point, then duplicate check at that point.
            var target = hit != null ? hit.Point
                       : plan.OnLevelPlane ? new XYZ(x, y, levelZ + Ft(plan.PlaneElevMm))
                       : new XYZ(x, y, levelZ + offset);
            long dupKey = DupKey(sym.Id, familyOfType);
            using (_timer.Time(Phases.DupQuery))
                if (dups.Contains(dupKey, b.Name, target.X, target.Y, ctx.BandMin, ctx.BandMax))
                    return WithDebug(Result(b, row, Status.Duplicate, Notes("already in the model here (same family, or Comments \"CAD: " + b.Name + "\")"), target, angle), plan);

            // 3a. Level-based without a host: batched (created after the loop).
            if (hit == null && !plan.OnLevelPlane && !plan.OnVertical && ptype == FamilyPlacementType.OneLevelBased)
            {
                var key = (row, Compat.IdValue(level.Id));
                if (!batches.TryGetValue(key, out var list))
                {
                    batches[key] = list = new List<Pending>();
                    batchLevel[key] = (level, sym);
                }
                var pending = Result(b, row, Status.Placed, Notes(), target, angle);
                list.Add(new Pending { Block = b, Result = pending, Base = new XYZ(x, y, levelZ), Angle = angle,
                                       Offset = plan.LevelElevMm.HasValue ? offset : (double?)null });
                dups.Add(dupKey, "CAD: " + b.Name, target.X, target.Y, target.Z);
                plan.FinalHost = "Level " + level.Name + " (level-based, no host)";
                return WithDebug(pending, plan);
            }

            // 3b. Everything else: one by one, each in its own sub-transaction.
            PlacementResult res;
            using (var st = new SubTransaction(_doc))
            {
                st.Start();
                try
                {
                    res = CreateOne(b, row, sym, level, finder, ctx, plan, ref target, out var inst, out var instOffset);
                    if (res.Status == Status.Placed)
                    {
                        st.Commit();
                        dups.Add(dupKey, "CAD: " + b.Name, target.X, target.Y, target.Z);
                        created.Add(new Created { Id = inst.Id, Level = level, BlockName = b.Name, Offset = instOffset, Result = res, CadPoint = b.Point });
                    }
                    else st.RollBack();
                }
                catch (Exception ex)
                {
                    if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                    res = Result(b, row, Status.Failed, Notes(ex.Message.Trim()), b.Point, angle);
                }
            }
            return WithDebug(res, plan);
        }

        /// <summary>DebugHosting: appends one DEBUG line (linked yes/no, link, host element id and
        /// category, face normal, final Host) to the block's log message.</summary>
        PlacementResult WithDebug(PlacementResult res, Plan plan)
        {
            if (!_settings.DebugHosting || res == null) return res;
            var h = plan.Hit;
            long? id = null;
            string cat = null;
            try
            {
                if (h?.Element != null)
                {
                    id = Compat.IdValue(h.Element.Id);
                    cat = h.Element.Category?.Name;
                }
            }
            catch (Exception) { }
            var n = h?.FaceNormal;
            string final = res.Status == Status.Placed ? plan.FinalHost ?? res.Host
                         : "not placed" + (plan.FinalHost != null ? " (Revit hosted it on: " + plan.FinalHost + ")" : "");
            var line = HostCheck.DebugLine(h != null, h?.IsLinked ?? false, h?.LinkName, id, cat,
                                           n != null ? new[] { n.X, n.Y, n.Z } : null, final);
            res.Message = res.Message.Length > 0 ? res.Message + "; " + line : line;
            return res;
        }

        /// <summary>Step 3 - create one instance according to the plan.</summary>
        PlacementResult CreateOne(BlockRef b, MapRow row, FamilySymbol sym, Level level, HostFinder finder, LevelContext ctx, Plan plan,
                                  ref XYZ target, out FamilyInstance inst, out double? instOffset)
        {
            var ptype = sym.Family.FamilyPlacementType;
            double levelZ = level.ProjectElevation;
            double offset = plan.LevelElevMm.HasValue ? Ft(plan.LevelElevMm.Value) : Ft(row.OffsetMm);
            double x = b.Point.X, y = b.Point.Y;
            double angle = plan.Angle;
            var notes = plan.Notes;
            var hit = plan.Hit;
            inst = null;
            instOffset = null;
            string Notes(string extra = null)
            {
                if (!string.IsNullOrEmpty(extra)) notes.Add(extra);
                return string.Join("; ", notes);
            }
            var cadDir = new XYZ(Math.Cos(angle), Math.Sin(angle), 0);
            string hostText = hit?.Describe() ?? "";

            if (plan.OnLevelPlane)
            {
                // Horizontal plane at level + elevation; the CAD rotation is the reference direction.
                ReferencePlane rp;
                using (_timer.Time(Phases.Planes)) rp = ctx.LevelPlanes.Get(plan.PlaneElevMm, plan.PlaneFacing);
                using (_timer.Time(Phases.Create))
                    inst = _doc.Create.NewFamilyInstance(rp.GetReference(), target, cadDir, sym);
                // Face the requested side. The plane's normal is known, so no regeneration is
                // needed to decide (a reused plane may point the other way).
                if (rp.Normal.DotProduct(LevelPlanes.Normal(plan.PlaneFacing)) < 0)
                {
                    if (inst.CanFlipWorkPlane) inst.IsWorkPlaneFlipped = !inst.IsWorkPlaneFlipped;
                    else notes.Add("WARNING: could not flip the family to face " + plan.PlaneFacing.ToString().ToLowerInvariant());
                }
                hostText = "Reference plane " + rp.Name;
                plan.FinalHost = "Reference Plane " + rp.Name + " (no face host)";
            }
            else if (plan.OnVertical)
            {
                // Device faces the CAD block's local +Y axis (turned by the row's Rotation):
                // blocks drawn with the wall along X and the room on +Y face into the room.
                // It goes on the CAD insertion point (at the row's elevation) projected onto the
                // plane, reference direction along the plane (upright, not mirrored).
                var f = VerticalPlacement.Facing(angle);
                var facing = new XYZ(f.X, f.Y, 0);
                // The plane passes through the block point (or is colinear with it, < 5 mm); the
                // family goes on the block point at level + Elevation From Level, reference
                // direction = the plane direction (block X).
                Reference planeRef;
                XYZ onPlane, normal;
                string planeName;
                using (_timer.Time(Phases.Planes)) planeRef = ctx.VerticalPlanes.Get(target, facing, out onPlane, out normal, out planeName);
                var along = XYZ.BasisZ.CrossProduct(normal).Normalize();
                using (_timer.Time(Phases.Create))
                    inst = _doc.Create.NewFamilyInstance(planeRef, onPlane, along, sym);
                target = onPlane;
                hostText = "Vertical plane " + planeName;
                plan.FinalHost = "Reference Plane " + planeName + " (vertical)";
                using (_timer.Time(Phases.Verify))
                {
                    double leftMm = SnapToPoint(inst, onPlane, normal, along, facing, notes);
                    if (leftMm > VerticalPlacement.ToleranceMm)
                        plan.Review.Add(VerticalPlacement.PlacedAwayReason(leftMm));
                }
            }
            else if (hit != null && ptype == FamilyPlacementType.WorkPlaneBased)
            {
                XYZ RefDir(HostHit h)
                {
                    var n = h.FaceNormal;
                    // Walls: the wall direction (the CAD rotation is ignored), family upright, flat on
                    // the face. Other faces: the CAD angle, in the face plane.
                    var d = row.Host == HostMode.Wall
                        ? h.RefDir ?? XYZ.BasisZ.CrossProduct(n)
                        : cadDir.Subtract(n.Multiply(cadDir.DotProduct(n)));
                    return d.Normalize();
                }
                FamilyInstance Place(HostHit h)
                {
                    using (_timer.Time(Phases.Create))
                        return _doc.Create.NewFamilyInstance(h.Reference, h.Point, RefDir(h), sym);
                }
                // Place, then check the instance really is on the face found (for a linked
                // slab: Host = the Revit link, HostFace set - not a reference plane or level).
                Exception error = null;
                string problem = null;
                try
                {
                    inst = Place(hit);
                    problem = HostProblem(inst, hit, row.Host, plan);
                }
                catch (Exception ex) { error = ex; }
                if ((error != null || problem != null) && hit.FromIndex && finder != null)
                {
                    // The indexed face could not host: find it again by ray (stable link reference).
                    var z = row.Host == HostMode.Wall ? levelZ + Math.Max(offset, Ft(10)) : levelZ;
                    double dist = row.Host == HostMode.Wall ? Ft(_slab.WallSearchMm) : Math.Max(hit.Distance + 1, 1);
                    var again = finder.Recast(row.Host, x, y, levelZ, z, dist, b.Rotation);
                    if (again != null && row.Host == HostMode.Wall)
                        again.Point = new XYZ(again.Point.X, again.Point.Y, levelZ + offset);   // ray height -> placement height
                    if (again != null)
                    {
                        if (inst != null && inst.IsValidObject) _doc.Delete(inst.Id);
                        inst = null;
                        error = null;
                        problem = null;
                        hit = plan.Hit = again;
                        target = hit.Point;
                        hostText = hit.Describe();
                        try
                        {
                            inst = Place(hit);
                            problem = HostProblem(inst, hit, row.Host, plan);
                        }
                        catch (Exception ex) { error = ex; }
                    }
                }
                if (error != null) throw error;
                if (problem != null) return Result(b, row, Status.Failed, Notes(problem), target, angle, null, hostText);
                if (row.Host == HostMode.Wall) CheckWallFacing(inst, hit, notes);
            }
            else if (hit != null && ptype == FamilyPlacementType.OneLevelBasedHosted)
            {
                using (_timer.Time(Phases.Create))
                    inst = _doc.Create.NewFamilyInstance(hit.Point, sym, hit.Element, level, StructuralType.NonStructural);
                if (row.Host == HostMode.Wall)
                {
                    SetOffset(inst, offset);
                    // Face out towards the CAD block; undo a mirror (hand) if Revit made one.
                    if (inst.CanFlipFacing && inst.FacingOrientation.DotProduct(hit.RoomNormal) < 0)
                        inst.flipFacing();
                    if (inst.CanFlipHand && inst.HandOrientation.CrossProduct(inst.FacingOrientation).Z < 0)
                        inst.flipHand();
                }
                else if (Math.Abs(angle) > 1e-9)
                {
                    using (_timer.Time(Phases.Rotate))
                        ElementTransformUtils.RotateElement(_doc, inst.Id,
                            Line.CreateBound(hit.Point, hit.Point.Add(XYZ.BasisZ)), angle);
                }
            }
            else if (ptype == FamilyPlacementType.WorkPlaneBased)
            {
                // Face/work-plane based family without a host: put it on the level plane.
                using (_timer.Time(Phases.Create))
                    inst = _doc.Create.NewFamilyInstance(level.GetPlaneReference(), new XYZ(x, y, levelZ), cadDir, sym);
                instOffset = offset;
                plan.FinalHost = "Level " + level.Name + " (no face host)";
            }
            else if (ptype == FamilyPlacementType.OneLevelBased)
            {
                // Normally batched; used when a batch fails.
                inst = CreateLevelBased(sym, level, new XYZ(x, y, levelZ), angle);
                instOffset = offset;
            }
            else if (ptype == FamilyPlacementType.OneLevelBasedHosted)
            {
                return Result(b, row, Status.Failed, Notes("family needs a host (wall/ceiling) - set Host_Type"), b.Point, angle);
            }
            else
            {
                return Result(b, row, Status.Failed, Notes($"placement type '{ptype}' is not supported"), b.Point, angle);
            }
            return Result(b, row, Status.Placed, Notes(), target, angle, inst.Id, hostText);
        }

        /// <summary>Null if <paramref name="inst"/> is hosted on the face of <paramref name="hit"/>
        /// (Host = the Revit link for a linked face, HostFace set), else why not. Also records
        /// the final host in plan.FinalHost.</summary>
        string HostProblem(FamilyInstance inst, HostHit hit, HostMode mode, Plan plan)
        {
            string Check()
            {
                Element host = null;
                Reference face = null;
                try { host = inst.Host; } catch (Exception) { }
                try { face = inst.HostFace; } catch (Exception) { }
                var kind = host == null ? HostKind.None
                         : host is ReferencePlane ? HostKind.ReferencePlane
                         : host is Level ? HostKind.Level
                         : host is RevitLinkInstance ? HostKind.LinkInstance
                         : HostKind.Element;
                string name = "";
                try
                {
                    name = host is RevitLinkInstance link ? _doc.GetElement(link.GetTypeId())?.Name ?? link.Name
                         : host is ReferencePlane || host is Level ? host.Name
                         : host != null ? $"{host.Category?.Name} {Compat.IdValue(host.Id)}" : "";
                }
                catch (Exception) { }
                plan.FinalHost = HostCheck.KindText(kind) + (name.Length > 0 && kind != HostKind.Element ? " " + name : "")
                               + (kind == HostKind.Element ? " (" + name + ")" : "")
                               + ", host face " + (face != null ? "ok" : "none");
                return HostCheck.Problem(kind, hit.IsLinked, face != null, HostCheck.What(mode));
            }
            var problem = Check();
            if (problem != null)
            {
                // Host / HostFace can be filled in only by a regeneration: check once more
                // before calling it a failure (rare, so the cost does not matter).
                _doc.Regenerate();
                problem = Check();
            }
            return problem;
        }

        static V3 V(XYZ p) => new V3(p.X, p.Y, p.Z);

        /// <summary>Plan distance from each placed element's location point to its CAD block; more
        /// than the review distance adds "Placed N mm away from CAD block" to Needs Review (with its
        /// Element ID) and a warning to the result window. One regeneration for the whole run.</summary>
        void CheckDistances(List<Created> created)
        {
            if (created.Count == 0) return;
            _doc.Regenerate();
            double limitMm = _slab.ReviewDistanceMm > 0 ? _slab.ReviewDistanceMm : VerticalPlacement.DefaultReviewDistanceMm;
            foreach (var c in created)
            {
                if (c.CadPoint == null || c.Result == null) continue;
                if (!(_doc.GetElement(c.Id) is FamilyInstance fi) || !(fi.Location is LocationPoint lp)) continue;
                double mm = VerticalPlacement.PlanDistanceMm(V(lp.Point), V(c.CadPoint));
                if (mm <= limitMm) continue;
                var parts = (c.Result.Review ?? "").Split(new[] { "; " }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => !VerticalPlacement.IsPlacedAwayReason(t)).ToList();
                parts.Add(VerticalPlacement.PlacedAwayReason(mm));
                c.Result.Review = string.Join("; ", parts);
                var warn = $"WARNING: placed {mm:0} mm away from its CAD block (more than {limitMm:0} mm) - see Needs Review";
                c.Result.Message = c.Result.Message.Length > 0 ? c.Result.Message + "; " + warn : warn;
            }
        }

        /// <summary>Face-based family on a wall: it must face out of the wall towards the CAD block
        /// (its Z axis = the face normal on the block side). If not, flip its work plane; if that is
        /// not possible, log a warning.</summary>
        void CheckWallFacing(FamilyInstance inst, HostHit hit, List<string> notes)
        {
            try
            {
                if (inst.GetTransform().BasisZ.DotProduct(hit.FaceNormal) >= 0) return;
                if (inst.CanFlipWorkPlane) inst.IsWorkPlaneFlipped = !inst.IsWorkPlaneFlipped;
                else if (inst.CanFlipFacing) inst.flipFacing();
                else notes.Add("WARNING: the family faces into the wall and cannot be flipped - check it");
            }
            catch (Exception) { }
        }


        /// <summary>
        /// Vertical planes: makes sure the family really sits on <paramref name="point"/> (the CAD
        /// insertion point on the plane). Revit can put a face-based family off the point (on the
        /// back of the plane, or offset by the family's own origin), so after placing:
        /// 1. if it faces away from the plane normal, its work plane is flipped back;
        /// 2. with CenterGeometryOnCadPoint, a family whose origin is more than 10 mm from its
        ///    geometric centre (along the plane) is shifted so its centre is on the point;
        /// 3. if its origin landed more than 10 mm from the point, it is moved back onto it and a
        ///    WARNING is logged (shown in the result window).
        /// </summary>
        double SnapToPoint(FamilyInstance inst, XYZ point, XYZ normal, XYZ along, XYZ facing, List<string> notes)
        {
            _doc.Regenerate();
            // Facing: the family must face the CAD block's direction. Its Z axis must be the plane
            // normal (not the back of the plane), and a horizontal FacingOrientation must not point
            // the opposite way of the block.
            try
            {
                if (inst.GetTransform().BasisZ.DotProduct(normal) < -0.5 && inst.CanFlipWorkPlane)
                {
                    inst.IsWorkPlaneFlipped = !inst.IsWorkPlaneFlipped;
                    _doc.Regenerate();
                }
                var fo = inst.FacingOrientation;
                if (fo != null && Math.Abs(fo.Z) < 0.5 && fo.DotProduct(facing) < -0.5)
                {
                    if (inst.CanFlipFacing) inst.flipFacing();
                    else if (inst.CanFlipWorkPlane) inst.IsWorkPlaneFlipped = !inst.IsWorkPlaneFlipped;
                    else notes.Add("WARNING: the family faces away from the CAD block and cannot be flipped");
                    _doc.Regenerate();
                }
            }
            catch (Exception) { }
            if (!(inst.Location is LocationPoint lp)) return 0;
            var target = V(point);
            var dir = V(along);
            var loc = V(lp.Point);
            double devMm = VerticalPlacement.DeviationMm(loc, target);
            double centre = 0;
            if (_settings.CenterGeometryOnCadPoint)
            {
                BoundingBoxXYZ bb = null;
                try { bb = inst.get_BoundingBox(null); } catch (Exception) { }
                if (bb != null)
                    centre = VerticalPlacement.CenterOffset(loc, V(bb.Min.Add(bb.Max).Multiply(0.5)), dir);
            }
            var move = target - VerticalPlacement.Anchor(loc, dir, centre);
            if (move.Length * VerticalPlacement.MmPerFoot > 0.5)
            {
                ElementTransformUtils.MoveElement(_doc, inst.Id, new XYZ(move.X, move.Y, move.Z));
                _doc.Regenerate();
            }
            if (devMm > VerticalPlacement.ToleranceMm)
                notes.Add($"WARNING: family landed {devMm:0} mm from the CAD point - moved back onto it");
            if (centre != 0)
                notes.Add($"family origin is {Math.Abs(centre) * VerticalPlacement.MmPerFoot:0} mm from its centre along the plane - " +
                          "shifted so its centre is on the CAD point");
            if (inst.Location is LocationPoint after)
            {
                double leftMm = VerticalPlacement.DeviationMm(VerticalPlacement.Anchor(V(after.Point), dir, centre), target);
                if (leftMm > VerticalPlacement.ToleranceMm)
                    notes.Add($"WARNING: family is still {leftMm:0} mm from the CAD point after moving it - check it");
                return leftMm;
            }
            return 0;
        }

        FamilyInstance CreateLevelBased(FamilySymbol sym, Level level, XYZ basePt, double angle)
        {
            FamilyInstance inst;
            using (_timer.Time(Phases.Create))
                inst = _doc.Create.NewFamilyInstance(basePt, sym, level, StructuralType.NonStructural);
            if (Math.Abs(angle) > 1e-9)
                using (_timer.Time(Phases.Rotate))
                    ElementTransformUtils.RotateElement(_doc, inst.Id, Line.CreateBound(basePt, basePt.Add(XYZ.BasisZ)), angle);
            return inst;
        }

        /// <summary>Creates all pending level-based instances of one (row, level) with a single
        /// NewFamilyInstances2 call, rotation included in the creation data. If the batch call
        /// fails, the instances are created one by one instead.</summary>
        void CreateBatch(List<Pending> items, FamilySymbol sym, Level level, MapRow row, List<Created> created)
        {
            double offset = Ft(row.OffsetMm);
            var data = new List<FamilyInstanceCreationData>(items.Count);
            foreach (var it in items)
            {
                var d = new FamilyInstanceCreationData(it.Base, sym, level, StructuralType.NonStructural);
                if (Math.Abs(it.Angle) > 1e-9)
                {
                    d.RotateAngle = it.Angle;
                    d.Axis = Line.CreateBound(it.Base, it.Base.Add(XYZ.BasisZ));
                }
                data.Add(d);
            }
            using (var st = new SubTransaction(_doc))
            {
                st.Start();
                try
                {
                    ICollection<ElementId> ids;
                    using (_timer.Time(Phases.CreateBatch))
                        ids = _doc.Create.NewFamilyInstances2(data);
                    if (ids == null || ids.Count != items.Count)
                        throw new InvalidOperationException($"batch created {ids?.Count ?? 0} of {items.Count} instances");
                    st.Commit();
                    AssignIds(items, ids, level, offset, created);
                    return;
                }
                catch (Exception)
                {
                    if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                }
            }
            // Fallback: one by one, each in its own sub-transaction.
            foreach (var it in items)
                using (var st = new SubTransaction(_doc))
                {
                    st.Start();
                    try
                    {
                        var inst = CreateLevelBased(sym, level, it.Base, it.Angle);
                        st.Commit();
                        it.Result.ElementId = Compat.IdValue(inst.Id);
                        created.Add(new Created { Id = inst.Id, Level = level, BlockName = it.Block.Name, Offset = it.Offset ?? offset, Result = it.Result, CadPoint = it.Block.Point });
                    }
                    catch (Exception ex)
                    {
                        if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                        it.Result.Status = Status.Failed;
                        it.Result.Message = (it.Result.Message.Length > 0 ? it.Result.Message + "; " : "") + ex.Message.Trim();
                    }
                }
        }

        /// <summary>Match the new element ids to their blocks by location (order is used for any
        /// that cannot be matched).</summary>
        void AssignIds(List<Pending> items, ICollection<ElementId> ids, Level level, double offset, List<Created> created)
        {
            var byKey = new Dictionary<(long, long), Queue<Pending>>();
            (long, long) Key(XYZ p) => ((long)Math.Round(p.X * 1000), (long)Math.Round(p.Y * 1000));
            foreach (var it in items)
            {
                var k = Key(it.Base);
                if (!byKey.TryGetValue(k, out var q)) byKey[k] = q = new Queue<Pending>();
                q.Enqueue(it);
            }
            var unmatchedIds = new List<ElementId>();
            var done = new HashSet<Pending>();
            foreach (var id in ids)
            {
                Pending it = null;
                if (_doc.GetElement(id)?.Location is LocationPoint lp && byKey.TryGetValue(Key(lp.Point), out var q) && q.Count > 0)
                    it = q.Dequeue();
                if (it == null) { unmatchedIds.Add(id); continue; }
                done.Add(it);
                Attach(it, id);
            }
            var rest = items.Where(i => !done.Contains(i)).ToList();
            for (int i = 0; i < rest.Count && i < unmatchedIds.Count; i++) Attach(rest[i], unmatchedIds[i]);

            void Attach(Pending it, ElementId id)
            {
                it.Result.ElementId = Compat.IdValue(id);
                created.Add(new Created { Id = id, Level = level, BlockName = it.Block.Name, Offset = it.Offset ?? offset, Result = it.Result, CadPoint = it.Block.Point });
            }
        }

        /// <summary>'Elevation from Level' (level-based / wall-hosted) or 'Offset from Host'
        /// (work-plane based placed on the level plane).</summary>
        static bool SetOffset(Element inst, double offsetFt) =>
            SetParam(inst, BuiltInParameter.INSTANCE_ELEVATION_PARAM, offsetFt) ||
            SetParam(inst, BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM, offsetFt);

        static bool SetParam(Element e, BuiltInParameter bip, object value)
        {
            var p = e.get_Parameter(bip);
            if (p == null || p.IsReadOnly) return false;
            try
            {
                switch (value)
                {
                    case double d: return p.Set(d);
                    case string s: return p.Set(s);
                    case ElementId id: return p.Set(id);
                    default: return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        double? NextLevelZ(Level level)
        {
            var zs = new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>()
                .Select(l => l.ProjectElevation).Where(z => z > level.ProjectElevation + 0.01).ToList();
            return zs.Count > 0 ? zs.Min() : (double?)null;
        }
    }

    /// <summary>
    /// Deletes Revit warnings (e.g. "There are identical instances in the same place",
    /// "Instance doesn't intersect its host") during the transaction, so they neither pop up
    /// hundreds of times nor slow down the commit. Errors are left to Revit (the block's
    /// sub-transaction is rolled back).
    /// </summary>
    public class WarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            bool hasWarnings = false;
            foreach (var f in accessor.GetFailureMessages())
                if (f.GetSeverity() == FailureSeverity.Warning) { hasWarnings = true; break; }
            if (hasWarnings) accessor.DeleteAllWarnings();
            return FailureProcessingResult.Continue;
        }
    }
}

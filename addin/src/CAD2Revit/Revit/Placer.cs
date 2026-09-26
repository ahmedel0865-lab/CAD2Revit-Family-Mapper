using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CAD2Revit.Core;
using Settings = CAD2Revit.Core.Settings;

namespace CAD2Revit.Revit
{
    /// <summary>
    /// Places Revit family instances at CAD block locations.
    ///
    /// Everything happens inside ONE Transaction (one Ctrl+Z undoes the whole
    /// run). Each block is placed inside its own SubTransaction, so one bad block
    /// is rolled back on its own and does not stop the rest. Preview runs exactly
    /// the same code and rolls the transaction back at the end, so its counts
    /// (including hosts found and failures) match what Run will do.
    /// </summary>
    public class Placer
    {
        const double MmPerFoot = 304.8;
        static double Ft(double mm) => mm / MmPerFoot;

        readonly Document _doc;
        readonly Settings _settings;

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
            public DuplicateIndex Dups;
            public LevelPlanes LevelPlanes;
            public VerticalPlanes VerticalPlanes;
        }

        /// <summary>Places every mapped block. Each row goes on its own Level (MapRow.LevelName,
        /// or <paramref name="defaultLevel"/> when empty). dryRun = true rolls everything back.</summary>
        public List<PlacementResult> PlaceAll(List<BlockRef> blocks, MappingResult mapping,
                                              Dictionary<MapRow, FamilySymbol> symbols, Level defaultLevel,
                                              bool dryRun, Action<int, int> progress = null, double[] dwgExtents = null)
        {
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
            foreach (var l in new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>())
                if (!levelsByName.ContainsKey(l.Name)) levelsByName[l.Name] = l;

            using (var t = new Transaction(_doc, "CAD2Revit: Place families"))
            {
                var opts = t.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(new WarningSwallower());
                opts.SetClearAfterRollback(true);
                t.SetFailureHandlingOptions(opts);
                t.Start();

                View3D view = null;
                View3D TempView() => view ?? (view = HostFinder.CreateTempView(_doc));
                HostFinder finder = null;
                if (mapped.Any(m => m.Item2.Host == HostMode.Ceiling || m.Item2.Host == HostMode.Face || m.Item2.Host == HostMode.Wall))
                    finder = new HostFinder(_doc, TempView(), _settings.SearchRevitLinks);
                var section = LevelPlanes.SectionView(_doc);
                var extents = dwgExtents ?? BlockExtents(blocks);
                bool createdLevelPlanes = false;

                var contexts = new Dictionary<long, LevelContext>();
                LevelContext ContextFor(Level level)
                {
                    long key = Compat.IdValue(level.Id);
                    if (contexts.TryGetValue(key, out var c)) return c;
                    double levelZ = level.ProjectElevation;
                    double? top = NextLevelZ(level);
                    double maxUp = Ft(_settings.HostSearchDistanceMm);
                    if (top.HasValue) maxUp = Math.Min(maxUp, top.Value - levelZ);
                    double bandTop = top ?? levelZ + Math.Max(maxUp, Ft(3000));
                    c = new LevelContext
                    {
                        Level = level,
                        MaxUp = maxUp,
                        Dups = new DuplicateIndex(_doc, Ft(_settings.DuplicateToleranceMm),
                                                  levelZ - Ft(300), bandTop, _settings.DuplicateSameTypeOnly),
                        LevelPlanes = new LevelPlanes(_doc, level, extents, () => { createdLevelPlanes = true; return section ?? (View)TempView(); }),
                        VerticalPlanes = new VerticalPlanes(_doc, level),
                    };
                    contexts[key] = c;
                    return c;
                }

                for (int i = 0; i < mapped.Count; i++)
                {
                    progress?.Invoke(i, mapped.Count);
                    var (b, row) = mapped[i];
                    var level = defaultLevel;
                    string levelNote = null;
                    if (!string.IsNullOrWhiteSpace(row.LevelName))
                    {
                        if (levelsByName.TryGetValue(row.LevelName.Trim(), out var rowLevel)) level = rowLevel;
                        else levelNote = $"level '{row.LevelName}' not found - placed on {defaultLevel.Name}";
                    }
                    if (!symbols.TryGetValue(row, out var sym))
                    {
                        var skipped = Result(b, row, Status.Skipped, "family/type not loaded", b.Point, b.Rotation);
                        skipped.Level = level.Name;
                        results.Add(skipped);
                        continue;
                    }
                    var ctx = ContextFor(level);
                    PlacementResult res;
                    using (var st = new SubTransaction(_doc))
                    {
                        st.Start();
                        try
                        {
                            res = PlaceOne(b, row, sym, level, finder, ctx.VerticalPlanes, ctx.LevelPlanes, ctx.Dups, ctx.MaxUp);
                            if (res.Status == Status.Placed) st.Commit();
                            else st.RollBack();
                        }
                        catch (Exception ex)
                        {
                            if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                            res = Result(b, row, Status.Failed, ex.Message.Trim(), b.Point, b.Rotation);
                        }
                    }
                    res.Level = level.Name;
                    if (levelNote != null) res.Message = res.Message.Length > 0 ? levelNote + "; " + res.Message : levelNote;
                    results.Add(res);
                }

                if (view != null)
                {
                    // Keep the temporary view only if a reference plane was created in it
                    // (deleting a view could take view-owned elements with it).
                    bool owned = createdLevelPlanes && new FilteredElementCollector(_doc).OfClass(typeof(ReferencePlane))
                        .Any(e => e.OwnerViewId == view.Id);
                    if (!owned) _doc.Delete(view.Id);
                }
                if (dryRun)
                {
                    t.RollBack();
                    foreach (var r in results) r.ElementId = null;
                }
                else
                {
                    var status = t.Commit();
                    if (status != TransactionStatus.Committed)
                        throw new InvalidOperationException($"Revit did not commit the transaction ({status}).");
                }
            }
            return results;
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

        PlacementResult PlaceOne(BlockRef b, MapRow row, FamilySymbol sym, Level level,
                                 HostFinder finder, VerticalPlanes planes, LevelPlanes levelPlanes,
                                 DuplicateIndex dups, double maxUp)
        {
            var ptype = sym.Family.FamilyPlacementType;
            double levelZ = level.ProjectElevation;
            double angle = b.Rotation + row.RotationDeg * Math.PI / 180.0;
            double offset = Ft(row.OffsetMm);
            double x = b.Point.X, y = b.Point.Y;
            var notes = new List<string>();
            string Notes(string extra = null)
            {
                if (!string.IsNullOrEmpty(extra)) notes.Add(extra);
                return string.Join("; ", notes);
            }

            // 1. Find a host if the row asks for one.
            HostHit hit = null;
            bool onVertical = false;   // face-based family on a vertical work plane (no wall)
            bool onLevelPlane = false; // face/work-plane based family on a horizontal reference plane
            if (row.Host == HostMode.RefPlane)
            {
                if (ptype == FamilyPlacementType.WorkPlaneBased) onLevelPlane = true;
                else if (ptype == FamilyPlacementType.OneLevelBased)
                    notes.Add("WARNING: family is not face-based/work-plane-based, so it cannot be hosted on a " +
                              "reference plane - placed level-based at the elevation");
                else if (ptype == FamilyPlacementType.OneLevelBasedHosted)
                    return Result(b, row, Status.Failed, Notes("WARNING: legacy wall/ceiling-hosted family cannot be hosted on a " +
                                                                "reference plane or placed level-based - use a face-based family"), b.Point, angle);
            }
            else if (row.Host == HostMode.Vertical)
            {
                if (ptype == FamilyPlacementType.WorkPlaneBased) onVertical = true;
                else if (ptype == FamilyPlacementType.OneLevelBased)
                    notes.Add("family is not face-based - placed level-based");
                else if (ptype == FamilyPlacementType.OneLevelBasedHosted)
                    return Result(b, row, Status.Failed, Notes("legacy wall-hosted family needs a real wall - use Host Type " +
                                                                "'wall' or a face-based family"), b.Point, angle);
            }
            else if (row.Host != HostMode.None)
            {
                string hostWord = row.Host.ToString().ToLowerInvariant();
                if (ptype == FamilyPlacementType.OneLevelBased)
                {
                    notes.Add("family is not face/wall-hosted - placed level-based");
                }
                else
                {
                    string where;
                    if (row.Host == HostMode.Wall)
                    {
                        double z = levelZ + Math.Max(offset, Ft(10));
                        hit = finder.FindWall(x, y, z, Ft(_settings.WallSearchDistanceMm), b.Rotation);
                        where = $"within {_settings.WallSearchDistanceMm:0} mm";
                    }
                    else
                    {
                        hit = finder.FindAbove(row.Host, x, y, levelZ, maxUp);
                        where = $"within {maxUp * MmPerFoot:0} mm above the level";
                    }
                    if (hit != null && hit.IsLinked && ptype == FamilyPlacementType.OneLevelBasedHosted)
                        return Result(b, row, Status.Failed,
                            Notes("host is in a Revit link; legacy wall/ceiling-hosted families can only be hosted " +
                                  "in this model - use a face-based family"), b.Point, angle);
                    if (hit == null)
                    {
                        var msg = $"no {hostWord} found {where}";
                        if (!_settings.FallbackToUnhosted || ptype == FamilyPlacementType.OneLevelBasedHosted)
                            return Result(b, row, Status.Failed, Notes(msg), b.Point, angle);
                        if (row.Host == HostMode.Wall && ptype == FamilyPlacementType.WorkPlaneBased)
                        {
                            onVertical = true;   // stand it up on a vertical plane instead of lying flat
                            notes.Add(msg + " - placed on a vertical plane");
                        }
                        else notes.Add(msg + " - placed unhosted");
                    }
                }
            }

            // 2. Target point, then duplicate check at that point.
            var target = hit != null ? hit.Point : new XYZ(x, y, levelZ + offset);
            if (dups.Contains(sym, target))
                return Result(b, row, Status.Duplicate, Notes("an instance of this family already exists here"), target, angle);

            if (!sym.IsActive)
            {
                sym.Activate();
                _doc.Regenerate();
            }
            var cadDir = new XYZ(Math.Cos(angle), Math.Sin(angle), 0);
            FamilyInstance inst;
            string hostText = hit?.Describe() ?? "";

            // 3. Create the instance according to the family's placement type.
            if (onLevelPlane)
            {
                // Horizontal plane at level + elevation; the CAD rotation is the reference direction.
                var rp = levelPlanes.Get(row.OffsetMm, row.Facing);
                inst = _doc.Create.NewFamilyInstance(rp.GetReference(), target, cadDir, sym);
                // Make sure the family faces the requested side (a reused plane may point the other way).
                _doc.Regenerate();
                var want = LevelPlanes.Normal(row.Facing);
                if (inst.GetTransform().BasisZ.DotProduct(want) < 0)
                {
                    if (inst.CanFlipWorkPlane) inst.IsWorkPlaneFlipped = !inst.IsWorkPlaneFlipped;
                    else notes.Add("WARNING: could not flip the family to face " + row.Facing.ToString().ToLowerInvariant());
                }
                hostText = "Reference plane " + rp.Name;
            }
            else if (onVertical)
            {
                // Device faces the CAD block's local +Y axis (turned by the row's Rotation):
                // blocks drawn with the wall along X and the room on +Y face into the room.
                var facing = new XYZ(-Math.Sin(angle), Math.Cos(angle), 0);
                var planeRef = planes.Get(target, facing, out var onPlane);
                inst = _doc.Create.NewFamilyInstance(planeRef, onPlane, XYZ.BasisZ.CrossProduct(facing).Normalize(), sym);
                target = onPlane;
                hostText = "Vertical plane";
            }
            else if (hit != null && ptype == FamilyPlacementType.WorkPlaneBased)
            {
                var n = hit.FaceNormal;
                XYZ refDir = row.Host == HostMode.Wall
                    ? XYZ.BasisZ.CrossProduct(n)                               // family "up" = project up
                    : cadDir.Subtract(n.Multiply(cadDir.DotProduct(n)));       // CAD angle, in the face plane
                inst = _doc.Create.NewFamilyInstance(hit.Reference, hit.Point, refDir.Normalize(), sym);
            }
            else if (hit != null && ptype == FamilyPlacementType.OneLevelBasedHosted)
            {
                inst = _doc.Create.NewFamilyInstance(hit.Point, sym, hit.Element, level, StructuralType.NonStructural);
                if (row.Host == HostMode.Wall)
                {
                    SetOffset(inst, offset);
                    if (inst.CanFlipFacing && inst.FacingOrientation.DotProduct(hit.RoomNormal) < 0)
                        inst.flipFacing();
                }
                else if (Math.Abs(angle) > 1e-9)
                {
                    ElementTransformUtils.RotateElement(_doc, inst.Id,
                        Line.CreateBound(hit.Point, hit.Point.Add(XYZ.BasisZ)), angle);
                }
            }
            else if (ptype == FamilyPlacementType.WorkPlaneBased)
            {
                // Face/work-plane based family without a host: put it on the level plane.
                inst = _doc.Create.NewFamilyInstance(level.GetPlaneReference(), new XYZ(x, y, levelZ), cadDir, sym);
                if (!SetOffset(inst, offset) && Math.Abs(offset) > 1e-9) notes.Add("could not set offset");
            }
            else if (ptype == FamilyPlacementType.OneLevelBased)
            {
                var basePt = new XYZ(x, y, levelZ);
                inst = _doc.Create.NewFamilyInstance(basePt, sym, level, StructuralType.NonStructural);
                if (!SetOffset(inst, offset) && Math.Abs(offset) > 1e-9) notes.Add("could not set offset");
                if (Math.Abs(angle) > 1e-9)
                    ElementTransformUtils.RotateElement(_doc, inst.Id, Line.CreateBound(basePt, basePt.Add(XYZ.BasisZ)), angle);
            }
            else if (ptype == FamilyPlacementType.OneLevelBasedHosted)
            {
                return Result(b, row, Status.Failed, Notes("family needs a host (wall/ceiling) - set Host_Type"), b.Point, angle);
            }
            else
            {
                return Result(b, row, Status.Failed, Notes($"placement type '{ptype}' is not supported"), b.Point, angle);
            }

            // 4. Parameters.
            SetParam(inst, BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM, level.Id);
            if (_settings.WriteBlockNameToComments)
                SetParam(inst, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "CAD: " + b.Name);

            dups.Add(sym, target);
            return Result(b, row, Status.Placed, Notes(), target, angle, inst.Id, hostText);
        }

        /// <summary>'Elevation from Level' (level-based / wall-hosted) or 'Offset from Host'
        /// (work-plane based placed on the level plane).</summary>
        static bool SetOffset(FamilyInstance inst, double offsetFt) =>
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

    /// <summary>Spatial hash of existing family instances within a Z band (the target
    /// level up to the next level), keyed by family (or by type).</summary>
    public class DuplicateIndex
    {
        readonly double _tol, _zMin, _zMax;
        readonly bool _byType;
        readonly Dictionary<(long, long, long), List<XYZ>> _cells = new Dictionary<(long, long, long), List<XYZ>>();

        public DuplicateIndex(Document doc, double tolFt, double zMin, double zMax, bool byType)
        {
            _tol = tolFt;
            _zMin = zMin;
            _zMax = zMax;
            _byType = byType;
            foreach (var fi in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
                if (fi.Location is LocationPoint lp && fi.Symbol != null)
                    Add(fi.Symbol, lp.Point);
        }

        long Key(FamilySymbol s) => Compat.IdValue(_byType ? s.Id : s.Family.Id);
        long Cell(double v) => (long)Math.Floor(v / _tol);

        public void Add(FamilySymbol sym, XYZ pt)
        {
            if (pt.Z < _zMin || pt.Z >= _zMax) return;
            var k = (Key(sym), Cell(pt.X), Cell(pt.Y));
            if (!_cells.TryGetValue(k, out var list)) _cells[k] = list = new List<XYZ>();
            list.Add(pt);
        }

        public bool Contains(FamilySymbol sym, XYZ pt)
        {
            long key = Key(sym), cx = Cell(pt.X), cy = Cell(pt.Y);
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    if (_cells.TryGetValue((key, cx + dx, cy + dy), out var list))
                        foreach (var p in list)
                            if (Math.Sqrt((p.X - pt.X) * (p.X - pt.X) + (p.Y - pt.Y) * (p.Y - pt.Y)) <= _tol)
                                return true;
            return false;
        }
    }

    /// <summary>Dismisses Revit warnings (e.g. "identical instances in the same place") so
    /// they do not pop up hundreds of times or cancel the transaction.</summary>
    public class WarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var f in accessor.GetFailureMessages())
                if (f.GetSeverity() == FailureSeverity.Warning)
                    accessor.DeleteWarning(f);
            return FailureProcessingResult.Continue;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SmartHostMEP.Core;
using SmartHostMEP.Revit;
using SmartHostMEP.UI;
using Settings = SmartHostMEP.Core.Settings;

namespace SmartHostMEP.Commands
{
    /// <summary>
    /// "Fix Host Line": click a placed element, then the DWG line it should sit on. The element is
    /// moved onto a vertical reference plane on that line (an existing SmartHost plane on the same
    /// line is reused), facing away from the line toward the CAD block, and its Needs Review mark in
    /// Comments (if any) is removed. Optionally every other element of the same CAD block along that
    /// line is moved too. Repeats until Esc; each fix is one Ctrl+Z.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class FixHostLineCommand : IExternalCommand
    {
        const double MmPerFoot = 304.8;
        const double PickToleranceMm = 300;   // the click must be this close to a straight DWG line
        const double BlockMatchMm = 600;      // element <-> its CAD block, in plan

        class InstanceFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is FamilyInstance;
            public bool AllowReference(Reference r, XYZ p) => true;
        }

        class DwgFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is ImportInstance;
            public bool AllowReference(Reference r, XYZ p) => true;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var uidoc = data.Application.ActiveUIDocument;
            var doc = uidoc.Document;
            RevitOwner.MainHandle = data.Application.MainWindowHandle;
            var settings = Settings.Load();
            var lineCache = new Dictionary<long, WallLineScorer>();
            var blockCache = new Dictionary<long, List<BlockRef>>();
            int fixedCount = 0;
            var problems = new List<string>();
            try
            {
                while (true)
                {
                    Reference er, lr;
                    try
                    {
                        er = uidoc.Selection.PickObject(ObjectType.Element, new InstanceFilter(),
                            "Fix Host Line: click the placed element (Esc to finish)");
                        lr = uidoc.Selection.PickObject(ObjectType.PointOnElement, new DwgFilter(),
                            "Fix Host Line: click the DWG line it should sit on (Esc to finish)");
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { break; }

                    var inst = doc.GetElement(er) as FamilyInstance;
                    var dwg = doc.GetElement(lr) as ImportInstance;
                    if (inst == null || dwg == null || lr.GlobalPoint == null) continue;
                    if (inst.Symbol?.Family?.FamilyPlacementType != FamilyPlacementType.WorkPlaneBased)
                    {
                        TaskDialog.Show("SmartHost MEP - Fix Host Line", "Only face-based (work plane based) families can go on a vertical plane.");
                        continue;
                    }
                    long key = Compat.IdValue(dwg.Id);
                    if (!lineCache.TryGetValue(key, out var lines))
                        lineCache[key] = lines = new WallLineScorer(DwgLines.Read(doc, dwg));
                    var seg = lines.Nearest(lr.GlobalPoint.X, lr.GlobalPoint.Y, PickToleranceMm / MmPerFoot);
                    if (seg == null)
                    {
                        TaskDialog.Show("SmartHost MEP - Fix Host Line", "No straight DWG line at that point. Click on a wall line (arcs are not used).");
                        continue;
                    }
                    if (!blockCache.TryGetValue(key, out var blocks))
                    {
                        blocks = DwgReader.Read(doc, dwg, settings.IncludeNestedBlocks);
                        if (settings.SimplifyBlockNames) DwgReader.SimplifyNames(blocks);
                        blockCache[key] = blocks;
                    }

                    // The element's CAD block, and (optionally) the other elements of that block on this line.
                    var todo = new List<(FamilyInstance inst, BlockRef block)> { (inst, NearestBlock(blocks, Location(inst))) };
                    var block = todo[0].block;
                    if (block != null)
                    {
                        var others = OthersOnLine(doc, inst, block, blocks, seg);
                        if (others.Count > 0)
                        {
                            var td = new TaskDialog("SmartHost MEP - Fix Host Line")
                            {
                                MainInstruction = "Move this element onto the picked line?",
                                MainContent = $"CAD block: {block.Name}",
                                VerificationText = $"Apply to all elements of the same block on this line ({others.Count} more)",
                                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                                DefaultButton = TaskDialogResult.Ok,
                            };
                            if (td.Show() != TaskDialogResult.Ok) continue;
                            if (td.WasVerificationChecked()) todo.AddRange(others);
                        }
                    }

                    using (var t = new Transaction(doc, "SmartHost: Fix host line"))
                    {
                        var fo = t.GetFailureHandlingOptions();
                        fo.SetFailuresPreprocessor(new WarningSwallower());
                        t.SetFailureHandlingOptions(fo);
                        t.Start();
                        var planes = new Dictionary<long, VerticalPlanes>();
                        var placer = new Placer(doc, settings);
                        int done = 0;
                        foreach (var (i, b) in todo)
                        {
                            var problem = Rehost(doc, placer, planes, i, b, seg);
                            if (problem == null) done++;
                            else problems.Add(problem);
                        }
                        if (done > 0)
                        {
                            t.Commit();
                            fixedCount += done;
                        }
                        else t.RollBack();
                    }
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("SmartHost MEP - error", ex.ToString());
                return Result.Failed;
            }
            if (fixedCount > 0 || problems.Count > 0)
                TaskDialog.Show("SmartHost MEP - Fix Host Line",
                    $"Moved {fixedCount} element(s) onto the picked line." +
                    (problems.Count > 0 ? "\n\nNot moved:\n" + string.Join("\n", problems.Distinct().Take(15)) : ""));
            return Result.Succeeded;
        }

        static XYZ Location(FamilyInstance i) => (i.Location as LocationPoint)?.Point;

        static XYZ BlockPoint(BlockRef b) => DwgReader.SymbolCentre(b) ?? b.Point;

        static double Plan(XYZ a, XYZ b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        /// <summary>The CAD block nearest to the element (symbol centre or base point), within 600 mm.</summary>
        static BlockRef NearestBlock(List<BlockRef> blocks, XYZ p)
        {
            if (p == null) return null;
            BlockRef best = null;
            double bestD = BlockMatchMm / MmPerFoot;
            foreach (var b in blocks)
            {
                double d = Math.Min(Plan(BlockPoint(b), p), Plan(b.Point, p));
                if (d < bestD) { bestD = d; best = b; }
            }
            return best;
        }

        /// <summary>Other blocks with the same name within the wall/column search of the picked line
        /// (and along it), each with the placed element of the same family nearest to it.</summary>
        static List<(FamilyInstance, BlockRef)> OthersOnLine(Document doc, FamilyInstance inst, BlockRef block, List<BlockRef> blocks, LineSeg seg)
        {
            double reach = EdgeSnap.DefaultSearchMm / MmPerFoot;
            var a = new V3(seg.X1, seg.Y1, 0);
            var e = new V3(seg.X2, seg.Y2, 0);
            var familyId = inst.Symbol.Family.Id;
            var candidates = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(f => f.Id != inst.Id && f.Symbol?.Family?.Id == familyId && f.Location is LocationPoint)
                .ToList();
            var used = new HashSet<ElementId> { inst.Id };
            var result = new List<(FamilyInstance, BlockRef)>();
            foreach (var b in blocks)
            {
                if (ReferenceEquals(b, block) || !string.Equals(b.Name, block.Name, StringComparison.OrdinalIgnoreCase)) continue;
                var p = BlockPoint(b);
                var v = new V3(p.X, p.Y, 0);
                if (EdgeIndex<int>.SegmentDistance(p.X, p.Y, seg.X1, seg.Y1, seg.X2, seg.Y2) > reach || !EdgeSnap.Covers(v, a, e)) continue;
                FamilyInstance best = null;
                double bestD = BlockMatchMm / MmPerFoot;
                foreach (var f in candidates)
                {
                    if (used.Contains(f.Id)) continue;
                    double d = Plan(Location(f), p);
                    if (d < bestD) { bestD = d; best = f; }
                }
                if (best == null) continue;
                used.Add(best.Id);
                result.Add((best, b));
            }
            return result;
        }

        /// <summary>Recreates the element on a vertical plane on the line (same type, height and
        /// parameters), then deletes the old one. Returns a problem text, or null.</summary>
        static string Rehost(Document doc, Placer placer, Dictionary<long, VerticalPlanes> planes, FamilyInstance old, BlockRef block, LineSeg seg)
        {
            var loc = Location(old);
            if (loc == null) return $"{old.Id}: no point location";
            var level = doc.GetElement(old.LevelId) as Level ?? LevelOf(doc, old);
            if (level == null) return $"{old.Id}: no level";
            // Side and point from the CAD block (else the element itself); facing away from the line toward it.
            var side = block != null ? BlockPoint(block) : loc;
            var fo = old.FacingOrientation;
            var current = fo != null && Math.Abs(fo.Z) < 0.5 ? new V3(fo.X, fo.Y, 0).Normalize() : new V3(0, 1, 0);
            var ep = EdgeSnap.Plan(new V3(side.X, side.Y, 0), new V3(seg.X1, seg.Y1, 0), new V3(seg.X2, seg.Y2, 0), current,
                                   PlanePosition.SnapToFace, loc.Z);
            var facing = new XYZ(ep.Facing.X, ep.Facing.Y, 0);
            long lk = Compat.IdValue(level.Id);
            if (!planes.TryGetValue(lk, out var vp)) planes[lk] = vp = new VerticalPlanes(doc, level);
            var planeRef = vp.Get(new XYZ(ep.Point.X, ep.Point.Y, loc.Z), facing, out var onPlane, out var normal, out _);
            var along = XYZ.BasisZ.CrossProduct(normal).Normalize();
            FamilyInstance inst;
            try { inst = doc.Create.NewFamilyInstance(planeRef, onPlane, along, old.Symbol); }
            catch (Exception ex) { return $"{old.Id}: {ex.Message}"; }
            CopyParameters(old, inst, level);
            placer.SnapToPoint(inst, onPlane, normal, along, facing, new List<string>());
            doc.Delete(old.Id);
            return null;
        }

        static Level LevelOf(Document doc, FamilyInstance i)
        {
            var id = i.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM)?.AsElementId();
            return id != null ? doc.GetElement(id) as Level : null;
        }

        /// <summary>Writable instance parameters of the old element (project, shared and family
        /// parameters, Mark and Comments) onto the new one, and its level. The Needs Review mark
        /// is taken out of Comments.</summary>
        static void CopyParameters(FamilyInstance from, FamilyInstance to, Level level)
        {
            var keep = new HashSet<BuiltInParameter> { BuiltInParameter.ALL_MODEL_MARK, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS };
            foreach (Parameter p in from.Parameters)
            {
                try
                {
                    if (p.IsReadOnly || !p.HasValue || p.StorageType == StorageType.None || p.StorageType == StorageType.ElementId) continue;
                    if (p.Definition is InternalDefinition d && d.BuiltInParameter != BuiltInParameter.INVALID && !keep.Contains(d.BuiltInParameter)) continue;
                    var q = to.get_Parameter(p.Definition);
                    if (q == null || q.IsReadOnly) continue;
                    switch (p.StorageType)
                    {
                        case StorageType.String:
                            var text = p.AsString() ?? "";
                            if (p.Definition is InternalDefinition c && c.BuiltInParameter == BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                                text = NeedsReview.WithoutMark(text);
                            q.Set(text);
                            break;
                        case StorageType.Integer: q.Set(p.AsInteger()); break;
                        case StorageType.Double: q.Set(p.AsDouble()); break;
                    }
                }
                catch (Exception) { /* parameter not settable on the new element */ }
            }
            try { to.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM)?.Set(level.Id); } catch (Exception) { }
        }
    }
}

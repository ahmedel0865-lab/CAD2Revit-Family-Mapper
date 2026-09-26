# -*- coding: utf-8 -*-
"""Place Revit family instances at CAD block locations.

Everything happens inside ONE Transaction (one Ctrl+Z undoes the whole run).
Each block is placed inside its own SubTransaction, so one bad block is rolled
back on its own and does not stop the rest. Preview mode runs exactly the same
code and then rolls the transaction back, so its counts (including hosts found
and failures) match what "Run" will do.
"""
import math
from Autodesk.Revit.DB import (
    XYZ, Line, Transaction, SubTransaction, FilteredElementCollector,
    FamilyInstance, Level, ElementTransformUtils, BuiltInParameter,
    FamilyPlacementType, IFailuresPreprocessor, FailureProcessingResult,
    FailureSeverity, TransactionStatus,
)
from Autodesk.Revit.DB.Structure import StructuralType
from cad2revit import config
from cad2revit.compat import eid_int
from cad2revit.hosting import HostFinder, create_temp_view
from cad2revit.report import Result, PLACED, DUPLICATE, UNMAPPED, SKIPPED, FAILED


# ------------------------------------------------------------------ helpers

class _WarningSwallower(IFailuresPreprocessor):
    """Dismiss Revit warnings (e.g. 'identical instances in the same place') so
    they do not pop up hundreds of times or cancel the transaction."""
    __namespace__ = "cad2revit"  # needed by pythonnet (CPython engine)

    def PreprocessFailures(self, accessor):
        for f in accessor.GetFailureMessages():
            if f.GetSeverity() == FailureSeverity.Warning:
                accessor.DeleteWarning(f)
        return FailureProcessingResult.Continue


class DuplicateIndex(object):
    """Spatial hash of existing family instances within a Z band (the target
    level up to the next level), keyed by family (or type)."""

    def __init__(self, doc, tol_ft, z_min, z_max, scope="family"):
        self.tol = tol_ft
        self.z_min, self.z_max = z_min, z_max
        self.by_family = (scope == "family")
        self.cells = {}
        for fi in FilteredElementCollector(doc).OfClass(FamilyInstance):
            pt = getattr(fi.Location, "Point", None)
            if pt is not None:
                self.add(fi.Symbol, pt)

    def _key(self, symbol):
        return eid_int(symbol.Family.Id if self.by_family else symbol.Id)

    def _cell(self, v):
        return int(math.floor(v / self.tol))

    def add(self, symbol, pt):
        if not (self.z_min <= pt.Z < self.z_max):
            return
        k = (self._key(symbol), self._cell(pt.X), self._cell(pt.Y))
        self.cells.setdefault(k, []).append((pt.X, pt.Y))

    def contains(self, symbol, pt):
        key = self._key(symbol)
        cx, cy = self._cell(pt.X), self._cell(pt.Y)
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for x, y in self.cells.get((key, cx + dx, cy + dy), ()):
                    if math.hypot(x - pt.X, y - pt.Y) <= self.tol:
                        return True
        return False


def _set_param(elem, bip, value):
    p = elem.get_Parameter(bip)
    if p is not None and not p.IsReadOnly:
        try:
            return p.Set(value)
        except Exception:
            return False
    return False


def _set_offset(inst, offset_ft):
    """'Elevation from Level' (level-based / wall-hosted) or 'Offset from Host'
    (work-plane based placed on the level plane)."""
    for bip in (BuiltInParameter.INSTANCE_ELEVATION_PARAM,
                BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM):
        if _set_param(inst, bip, offset_ft):
            return True
    return False


def _next_level_z(doc, level):
    zs = [l.ProjectElevation for l in FilteredElementCollector(doc).OfClass(Level)
          if l.ProjectElevation > level.ProjectElevation + 0.01]
    return min(zs) if zs else None


def plan(blocks, mapping):
    """Split blocks into mapped (block, row) pairs and unmapped {name: count}."""
    mapped, unmapped = [], {}
    for b in blocks:
        row = mapping.get(b.key)
        if row is None:
            unmapped[b.name] = unmapped.get(b.name, 0) + 1
        else:
            mapped.append((b, row))
    return mapped, unmapped


# ------------------------------------------------------------------ placement

class _Context(object):
    def __init__(self, doc, level, finder, dup_index, max_up_ft):
        self.doc = doc
        self.level = level
        self.level_z = level.ProjectElevation
        self.finder = finder
        self.dups = dup_index
        self.max_up = max_up_ft


def _place_one(ctx, b, row):
    """Place one block. Returns a Result (never raises for expected problems)."""
    doc, level, level_z = ctx.doc, ctx.level, ctx.level_z
    sym = row.symbol
    ptype = sym.Family.FamilyPlacementType
    angle = b.rotation + math.radians(row.rot_deg)
    offset_ft = config.mm_to_ft(row.offset_mm)
    x, y = b.point.X, b.point.Y
    notes = []

    def result(status, inst=None, msg=None, point=None, host=u""):
        if msg:
            notes.append(msg)
        if b.mirrored:
            notes.append(u"CAD block is mirrored - check orientation")
        return Result(b, row, status, eid_int(inst.Id) if inst is not None else None,
                      u"; ".join(notes), point, angle, host)

    # 1. Find a host if the row asks for one.
    hit = None
    if row.host != "none":
        if ptype == FamilyPlacementType.OneLevelBased:
            notes.append(u"family is not face/wall-hosted - placed level-based")
        else:
            if row.host in ("ceiling", "face"):
                hit = ctx.finder.find_above(row.host, x, y, level_z, ctx.max_up)
                where = u"within {:.0f} mm above the level".format(config.ft_to_mm(ctx.max_up))
            else:
                z = level_z + max(offset_ft, config.mm_to_ft(10))
                hit = ctx.finder.find_wall(x, y, z, config.mm_to_ft(config.WALL_SEARCH_DISTANCE_MM),
                                           b.rotation)
                where = u"within {:.0f} mm".format(config.WALL_SEARCH_DISTANCE_MM)
            if hit is not None and ptype == FamilyPlacementType.OneLevelBasedHosted and hit.is_linked:
                return result(FAILED, msg=u"host is in a Revit link; legacy wall/ceiling-hosted "
                                          u"families can only be hosted in this model - use a "
                                          u"face-based family")
            if hit is None:
                msg = u"no {} found {}".format(row.host, where)
                if not config.FALLBACK_TO_UNHOSTED or ptype == FamilyPlacementType.OneLevelBasedHosted:
                    return result(FAILED, msg=msg)
                notes.append(msg + u" - placed unhosted")

    # 2. Target point, then duplicate check at that point.
    target = hit.point if hit is not None else XYZ(x, y, level_z + offset_ft)
    if ctx.dups.contains(sym, target):
        return result(DUPLICATE, msg=u"an instance of this family already exists here",
                      point=target)

    if not sym.IsActive:
        sym.Activate()
        doc.Regenerate()
    cad_dir = XYZ(math.cos(angle), math.sin(angle), 0)
    host_text = hit.describe() if hit is not None else u""

    # 3. Create the instance according to the family's placement type.
    if hit is not None and ptype == FamilyPlacementType.WorkPlaneBased:
        n = hit.face_normal
        if row.host == "wall":
            ref_dir = XYZ.BasisZ.CrossProduct(n)  # family "up" = project up
        else:
            ref_dir = cad_dir.Subtract(n.Multiply(cad_dir.DotProduct(n)))  # CAD angle, in face plane
        inst = doc.Create.NewFamilyInstance(hit.reference, hit.point, ref_dir.Normalize(), sym)

    elif hit is not None and ptype == FamilyPlacementType.OneLevelBasedHosted:
        inst = doc.Create.NewFamilyInstance(hit.point, sym, hit.element, level,
                                            StructuralType.NonStructural)
        if row.host == "wall":
            _set_offset(inst, offset_ft)
            if inst.CanFlipFacing and inst.FacingOrientation.DotProduct(hit.normal) < 0:
                inst.flipFacing()
        elif abs(angle) > 1e-9:
            axis = Line.CreateBound(hit.point, hit.point.Add(XYZ.BasisZ))
            ElementTransformUtils.RotateElement(doc, inst.Id, axis, angle)

    elif ptype == FamilyPlacementType.WorkPlaneBased:
        # Face/work-plane based family without a host: put it on the level plane.
        inst = doc.Create.NewFamilyInstance(level.GetPlaneReference(), XYZ(x, y, level_z),
                                            cad_dir, sym)
        if not _set_offset(inst, offset_ft) and abs(offset_ft) > 1e-9:
            notes.append(u"could not set offset")

    elif ptype == FamilyPlacementType.OneLevelBased:
        base = XYZ(x, y, level_z)
        inst = doc.Create.NewFamilyInstance(base, sym, level, StructuralType.NonStructural)
        if not _set_offset(inst, offset_ft) and abs(offset_ft) > 1e-9:
            notes.append(u"could not set offset")
        if abs(angle) > 1e-9:
            axis = Line.CreateBound(base, base.Add(XYZ.BasisZ))
            ElementTransformUtils.RotateElement(doc, inst.Id, axis, angle)

    elif ptype == FamilyPlacementType.OneLevelBasedHosted:
        return result(FAILED, msg=u"family needs a host (wall/ceiling) - set Host_Type")
    else:
        return result(FAILED, msg=u"placement type '{}' is not supported".format(ptype))

    # 4. Parameters.
    _set_param(inst, BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM, level.Id)
    if config.WRITE_BLOCK_NAME_TO_COMMENTS:
        _set_param(inst, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, u"CAD: {}".format(b.name))

    ctx.dups.add(sym, target)
    return result(PLACED, inst, point=target, host=host_text)


def place_all(doc, blocks, mapping, level, dry_run=False, progress=None):
    """Place every mapped block. Returns a list of report.Result.
    dry_run=True rolls everything back (preview). progress(i, n) is optional."""
    results = []
    mapped, unmapped = plan(blocks, mapping)
    for name, n in sorted(unmapped.items()):
        results.append(Result(name, None, UNMAPPED, message=u"not in mapping file", count=n))

    level_z = level.ProjectElevation
    top = _next_level_z(doc, level)
    max_up = config.mm_to_ft(config.HOST_SEARCH_DISTANCE_MM)
    if top is not None:
        max_up = min(max_up, top - level_z)
    band_top = top if top is not None else level_z + max(max_up, config.mm_to_ft(3000))

    t = Transaction(doc, "CAD2Revit: Place families")
    opts = t.GetFailureHandlingOptions()
    try:
        opts.SetFailuresPreprocessor(_WarningSwallower())
    except Exception:
        pass
    opts.SetClearAfterRollback(True)
    t.SetFailureHandlingOptions(opts)
    t.Start()
    try:
        view = None
        finder = None
        if any(r.host != "none" for _, r in mapped):
            view = create_temp_view(doc)
            finder = HostFinder(doc, view)
        dups = DuplicateIndex(doc, config.mm_to_ft(config.DUPLICATE_TOLERANCE_MM),
                              level_z - config.mm_to_ft(300), band_top,
                              config.DUPLICATE_SCOPE)
        ctx = _Context(doc, level, finder, dups, max_up)

        for i, (b, row) in enumerate(mapped):
            if progress is not None:
                progress(i, len(mapped))
            if row.symbol is None:
                results.append(Result(b, row, SKIPPED, message=u"family/type not loaded",
                                      point=b.point, rotation=b.rotation))
                continue
            st = SubTransaction(doc)
            st.Start()
            try:
                res = _place_one(ctx, b, row)
                if res.status == PLACED:
                    st.Commit()
                else:
                    st.RollBack()
            except Exception as ex:
                st.RollBack()
                res = Result(b, row, FAILED, message=u"{}".format(ex).strip(),
                             point=b.point, rotation=b.rotation)
            results.append(res)

        if view is not None:
            doc.Delete(view.Id)
        if dry_run:
            t.RollBack()
            for r in results:
                r.element_id = None
        else:
            status = t.Commit()
            if status != TransactionStatus.Committed:
                raise Exception("Revit did not commit the transaction ({})".format(status))
    except Exception:
        if t.HasStarted() and not t.HasEnded():
            t.RollBack()
        raise
    return results

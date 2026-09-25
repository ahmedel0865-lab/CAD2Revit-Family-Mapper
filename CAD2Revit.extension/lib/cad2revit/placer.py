# -*- coding: utf-8 -*-
"""Place Revit family instances at CAD block locations."""
import math
from Autodesk.Revit.DB import (
    XYZ, Line, Transaction, FilteredElementCollector, FamilyInstance, View3D,
    ViewFamilyType, ViewFamily, ElementTransformUtils, BuiltInParameter,
    ReferenceIntersector, ElementClassFilter, FindReferenceTarget, Ceiling,
)
from Autodesk.Revit.DB.Structure import StructuralType
from cad2revit import config


class Result(object):
    def __init__(self, block, row, status, element_id=None, message=u""):
        self.block = block
        self.row = row
        self.status = status  # placed / skipped / duplicate / unmapped / failed
        self.element_id = element_id
        self.message = message


def _existing_points(doc):
    """symbol id -> list of XYZ for existing instances (duplicate check)."""
    idx = {}
    for fi in FilteredElementCollector(doc).OfClass(FamilyInstance):
        loc = getattr(fi.Location, "Point", None)
        if loc is not None:
            idx.setdefault(fi.Symbol.Id.IntegerValue, []).append(loc)
    return idx


def _is_duplicate(idx, symbol, pt, tol_ft):
    for p in idx.get(symbol.Id.IntegerValue, []):
        if XYZ(p.X, p.Y, 0).DistanceTo(XYZ(pt.X, pt.Y, 0)) <= tol_ft:
            return True
    return False


def _get_3d_view(doc):
    for v in FilteredElementCollector(doc).OfClass(View3D):
        if not v.IsTemplate:
            return v
    vft = [t for t in FilteredElementCollector(doc).OfClass(ViewFamilyType)
           if t.ViewFamily == ViewFamily.ThreeDimensional][0]
    return View3D.CreateIsometric(doc, vft.Id)


def _find_ceiling_face(intersector, pt, level_z):
    origin = XYZ(pt.X, pt.Y, level_z + 0.01)
    ctx = intersector.FindNearest(origin, XYZ.BasisZ)
    if ctx is None or ctx.Proximity > config.mm_to_ft(config.HOST_SEARCH_DISTANCE_MM):
        return None
    return ctx.GetReference()


def _set_param(elem, bip, value):
    p = elem.get_Parameter(bip)
    if p is not None and not p.IsReadOnly:
        p.Set(value)
        return True
    return False


def plan(blocks, mapping):
    """Split blocks into mapped (block,row) pairs and unmapped names."""
    mapped, unmapped = [], {}
    for b in blocks:
        row = mapping.get(b.key)
        if row is None:
            unmapped[b.name] = unmapped.get(b.name, 0) + 1
        else:
            mapped.append((b, row))
    return mapped, unmapped


def place_all(doc, blocks, mapping, level):
    results = []
    mapped, unmapped = plan(blocks, mapping)
    for name, n in unmapped.items():
        results.append(Result(name, None, "unmapped", message=u"{} instance(s)".format(n)))

    tol = config.mm_to_ft(config.DUPLICATE_TOLERANCE_MM)
    level_z = level.ProjectElevation

    t = Transaction(doc, "CAD2Revit: Place families")
    t.Start()
    try:
        idx = _existing_points(doc)
        intersector = None
        if any(r.host == "face" for _, r in mapped):
            view3d = _get_3d_view(doc)
            intersector = ReferenceIntersector(
                ElementClassFilter(Ceiling), FindReferenceTarget.Face, view3d)
            intersector.FindReferencesInRevitLinks = True

        for b, row in mapped:
            sym = row.symbol
            if sym is None:
                results.append(Result(b.name, row, "skipped", message=u"family/type not loaded"))
                continue
            if _is_duplicate(idx, sym, b.point, tol):
                results.append(Result(b.name, row, "duplicate", message=u"already placed here"))
                continue
            try:
                if not sym.IsActive:
                    sym.Activate()
                    doc.Regenerate()
                angle = b.rotation + math.radians(row.rot_deg)
                inst, msg = None, u""

                if row.host == "face" and intersector is not None:
                    ref = _find_ceiling_face(intersector, b.point, level_z)
                    if ref is not None:
                        ref_dir = XYZ(math.cos(angle), math.sin(angle), 0)
                        inst = doc.Create.NewFamilyInstance(ref, ref.GlobalPoint, ref_dir, sym)
                        msg = u"hosted on ceiling"
                    else:
                        msg = u"no ceiling found - placed on level"

                if inst is None:
                    pt = XYZ(b.point.X, b.point.Y, level_z)
                    inst = doc.Create.NewFamilyInstance(pt, sym, level, StructuralType.NonStructural)
                    _set_param(inst, BuiltInParameter.INSTANCE_ELEVATION_PARAM,
                               config.mm_to_ft(row.offset_mm))
                    if abs(angle) > 1e-9:
                        axis = Line.CreateBound(pt, pt + XYZ.BasisZ)
                        ElementTransformUtils.RotateElement(doc, inst.Id, axis, angle)

                if b.mirrored:
                    msg = (msg + u"; " if msg else u"") + u"CAD block is mirrored - check orientation"
                if config.WRITE_BLOCK_NAME_TO_COMMENTS:
                    _set_param(inst, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,
                               u"CAD: {}".format(b.name))

                idx.setdefault(sym.Id.IntegerValue, []).append(b.point)
                results.append(Result(b.name, row, "placed", inst.Id.IntegerValue, msg))
            except Exception as ex:
                results.append(Result(b.name, row, "failed", message=u"{}".format(ex)))
        t.Commit()
    except Exception:
        if t.HasStarted() and not t.HasEnded():
            t.RollBack()
        raise
    return results

# -*- coding: utf-8 -*-
"""Place Revit family instances at CAD block locations."""
import math
from System.Collections.Generic import List
from Autodesk.Revit.DB import (
    XYZ, Line, Transaction, FilteredElementCollector, FamilyInstance, View3D,
    ViewFamilyType, ViewFamily, ElementTransformUtils, BuiltInParameter,
    ReferenceIntersector, ElementMulticategoryFilter, FindReferenceTarget,
    BuiltInCategory, ElementId, PlanarFace, FamilyPlacementType,
)
from Autodesk.Revit.DB.Structure import StructuralType
from cad2revit import config, hosting

# Host category key -> Revit category.
_CATEGORIES = {
    "ceiling": BuiltInCategory.OST_Ceilings,
    "floor": BuiltInCategory.OST_Floors,
}


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


class HostHit(object):
    """A face found above a block: where to put the family and how the face is tilted."""
    def __init__(self, reference, point, normal, kind, linked, element):
        self.reference = reference  # usable by NewFamilyInstance (linked faces too)
        self.point = point          # exact hit point on the (sloped) face, model coords
        self.normal = normal        # XYZ, model coords, pointing down for undersides
        self.kind = kind            # "ceiling" / "floor"
        self.linked = linked
        self.element = element      # host element (in its own document)

    def describe(self):
        return hosting.describe(self.kind, self.linked, _tup(self.normal))


def _tup(v):
    return (v.X, v.Y, v.Z)


def _make_intersector(view3d, category_keys):
    cats = List[BuiltInCategory]([_CATEGORIES[k] for k in category_keys])
    ri = ReferenceIntersector(ElementMulticategoryFilter(cats), FindReferenceTarget.Face, view3d)
    ri.FindReferencesInRevitLinks = True
    return ri


def _host_element(doc, ref):
    """(element, face, transform, linked) for a reference from the intersector."""
    if ref.LinkedElementId != ElementId.InvalidElementId:
        link = doc.GetElement(ref.ElementId)
        elem = link.GetLinkDocument().GetElement(ref.LinkedElementId)
        face = elem.GetGeometryObjectFromReference(ref.CreateReferenceInLink())
        return elem, face, link.GetTotalTransform(), True
    elem = doc.GetElement(ref.ElementId)
    return elem, elem.GetGeometryObjectFromReference(ref), None, False


def _face_normal(face, transform, point):
    """Normal of the face at the hit point, in model coordinates (linked faces are
    transformed by the link instance's transform). None if it cannot be computed."""
    try:
        local = transform.Inverse.OfPoint(point) if transform is not None else point
        proj = face.Project(local)
        if proj is not None:
            n = face.ComputeNormal(proj.UVPoint)
        elif isinstance(face, PlanarFace):
            n = face.FaceNormal
        else:
            return None
        if transform is not None:
            n = transform.OfVector(n)
        return n.Normalize()
    except Exception:
        return None


def _kind_of(elem):
    """"ceiling" / "floor" for the host element (works for linked elements too)."""
    try:
        cid = elem.Category.Id
    except Exception:
        return None
    for key, bic in _CATEGORIES.items():
        if cid == ElementId(bic):
            return key
    return None


def _find_host_face(doc, intersector, pt, level_z):
    """Nearest ceiling/slab UNDERSIDE straight above (x, y) of the block, within
    HOST_SEARCH_DISTANCE_MM. The ray starts just above the level; faces whose normal
    points up (e.g. the top of the slab the level sits on) are skipped."""
    origin = XYZ(pt.X, pt.Y, level_z + config.mm_to_ft(config.HOST_RAY_START_MM))
    max_dist = config.mm_to_ft(config.HOST_SEARCH_DISTANCE_MM)
    candidates = []
    for ctx in intersector.Find(origin, XYZ.BasisZ):
        if ctx.Proximity > max_dist:
            continue
        ref = ctx.GetReference()
        try:
            elem, face, tf, linked = _host_element(doc, ref)
        except Exception:
            continue
        hit_pt = ref.GlobalPoint
        n = _face_normal(face, tf, hit_pt)
        if n is None:
            n = XYZ(0, 0, -1)   # cannot evaluate: assume a flat underside
        candidates.append((ctx.Proximity, _tup(n), HostHit(ref, hit_pt, n, _kind_of(elem), linked, elem)))
    return hosting.pick_nearest_underside(candidates, max_dist)


def _place_level_based(doc, sym, level, x, y, level_z, elevation_ft, angle):
    pt = XYZ(x, y, level_z)
    inst = doc.Create.NewFamilyInstance(pt, sym, level, StructuralType.NonStructural)
    _set_param(inst, BuiltInParameter.INSTANCE_ELEVATION_PARAM, elevation_ft)
    if abs(angle) > 1e-9:
        axis = Line.CreateBound(pt, pt + XYZ.BasisZ)
        ElementTransformUtils.RotateElement(doc, inst.Id, axis, angle)
    return inst


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
        view3d = None
        intersectors = {}   # Host_Type -> ReferenceIntersector (built once per run)
        for host in set(r.host for _, r in mapped):
            keys = hosting.categories_for(host, config.HOST_CATEGORIES)
            if keys:
                view3d = view3d or _get_3d_view(doc)
                intersectors[host] = _make_intersector(view3d, keys)

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
                ptype = sym.Family.FamilyPlacementType

                intersector = intersectors.get(row.host)
                if intersector is not None:
                    hit = _find_host_face(doc, intersector, b.point, level_z)
                    if hit is None:
                        msg = u"host: none - no {} found within {:.0f} mm above the level; " \
                              u"placed on level at CSV offset".format(
                                  u"ceiling/slab" if row.host == "face" else row.host,
                                  config.HOST_SEARCH_DISTANCE_MM)
                    elif ptype == FamilyPlacementType.WorkPlaneBased:
                        # Face-based / work-plane-based: host on the face at the exact hit
                        # point, CAD rotation projected into the (sloped) face plane.
                        rd = hosting.ref_direction(angle, _tup(hit.normal))
                        inst = doc.Create.NewFamilyInstance(
                            hit.reference, hit.point, XYZ(rd[0], rd[1], rd[2]), sym)
                        msg = hit.describe()
                    elif ptype == FamilyPlacementType.OneLevelBasedHosted and not hit.linked:
                        # Legacy ceiling-hosted family: host element in this model only.
                        inst = doc.Create.NewFamilyInstance(
                            hit.point, sym, hit.element, level, StructuralType.NonStructural)
                        if abs(angle) > 1e-9:
                            axis = Line.CreateBound(hit.point, hit.point + XYZ.BasisZ)
                            ElementTransformUtils.RotateElement(doc, inst.Id, axis, angle)
                        msg = hit.describe() + u" (legacy hosted family)"
                    elif ptype == FamilyPlacementType.OneLevelBased:
                        # Level-based family under a (sloped) face: keep it level-based but
                        # take the height from the face at this point, not the CSV offset.
                        elev = hit.point.Z - level_z
                        inst = _place_level_based(doc, sym, level, b.point.X, b.point.Y,
                                                  level_z, elev, angle)
                        msg = hit.describe() + u"; level-based family: elevation {:.0f} mm " \
                              u"from face (CSV offset ignored)".format(config.ft_to_mm(elev))
                    else:
                        raise Exception(u"{}: family placement type {} cannot be hosted on "
                                        u"this face - use a face-based family".format(
                                            hit.describe(), ptype))

                if inst is None:
                    try:
                        inst = _place_level_based(doc, sym, level, b.point.X, b.point.Y, level_z,
                                                  config.mm_to_ft(row.offset_mm), angle)
                    except Exception as ex:
                        # Keep the host information in the log (e.g. face-based family, no host).
                        raise Exception((msg + u"; " if msg else u"") + u"{}".format(ex))

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

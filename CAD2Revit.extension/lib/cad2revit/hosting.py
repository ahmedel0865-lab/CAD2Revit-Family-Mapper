# -*- coding: utf-8 -*-
"""Find host faces (ceilings, slab soffits, walls) for hosted placement.

Uses ReferenceIntersector (ray casting) in a temporary, clean 3D view so that
view templates, hidden categories or section boxes in the user's views cannot
hide hosts. Hosts inside linked Revit models (e.g. the architectural model) are
found too; face-based families can be hosted on linked faces.
"""
import math
import clr  # noqa: F401  (makes .NET namespaces importable on the CPython engine)
from System.Collections.Generic import List
from Autodesk.Revit.DB import (
    XYZ, Transform, BuiltInCategory, ElementId, FilteredElementCollector,
    ElementCategoryFilter, ElementMulticategoryFilter, FindReferenceTarget,
    ReferenceIntersector, RevitLinkInstance, View3D, ViewFamily, ViewFamilyType,
    ViewDetailLevel, PlanarFace,
)
from cad2revit import config

TEMP_VIEW_NAME = "CAD2Revit - host search (temporary)"


class HostHit(object):
    """A face found for hosting."""

    def __init__(self, reference, point, normal, element, is_linked, distance):
        self.reference = reference   # Reference usable by NewFamilyInstance
        self.point = point           # hit point, model coordinates
        self.face_normal = normal    # true face normal, model coordinates
        self.normal = normal         # for walls: horizontal, pointing to the CAD point
        self.element = element       # host element (in its own document)
        self.is_linked = is_linked   # True if the host is in a Revit link
        self.distance = distance     # ray length, feet

    def describe(self):
        cat = u"?"
        try:
            cat = self.element.Category.Name
        except Exception:
            pass
        return u"{}{}".format(cat, u" (linked)" if self.is_linked else u"")


def create_temp_view(doc):
    """Create a plain isometric 3D view (call inside a transaction)."""
    vft = [t for t in FilteredElementCollector(doc).OfClass(ViewFamilyType)
           if t.ViewFamily == ViewFamily.ThreeDimensional][0]
    view = View3D.CreateIsometric(doc, vft.Id)
    try:
        view.ViewTemplateId = ElementId.InvalidElementId
        view.IsSectionBoxActive = False
        view.DetailLevel = ViewDetailLevel.Fine
        view.Name = TEMP_VIEW_NAME
    except Exception:
        pass  # name clash from a crashed earlier run - harmless
    doc.Regenerate()
    return view


def _category_filter(categories):
    if len(categories) == 1:
        return ElementCategoryFilter(categories[0])
    return ElementMulticategoryFilter(List[BuiltInCategory](categories))


def _face_normal(doc, ref, point, fallback):
    """Return the true normal of the referenced face, in model coordinates."""
    try:
        if ref.LinkedElementId != ElementId.InvalidElementId:
            link = doc.GetElement(ref.ElementId)
            ldoc = link.GetLinkDocument()
            elem = ldoc.GetElement(ref.LinkedElementId)
            face = elem.GetGeometryObjectFromReference(ref.CreateReferenceInLink())
            tf = link.GetTotalTransform()
        else:
            elem = doc.GetElement(ref.ElementId)
            face = elem.GetGeometryObjectFromReference(ref)
            tf = Transform.Identity
        if isinstance(face, PlanarFace):
            n = face.FaceNormal
        else:
            proj = face.Project(tf.Inverse.OfPoint(point))
            n = face.ComputeNormal(proj.UVPoint)
        return tf.OfVector(n).Normalize()
    except Exception:
        return fallback


def _host_element(doc, ref):
    if ref.LinkedElementId != ElementId.InvalidElementId:
        link = doc.GetElement(ref.ElementId)
        if isinstance(link, RevitLinkInstance) and link.GetLinkDocument() is not None:
            return link.GetLinkDocument().GetElement(ref.LinkedElementId), True
        return None, True
    return doc.GetElement(ref.ElementId), False


class HostFinder(object):
    def __init__(self, doc, view3d):
        self.doc = doc
        self.view = view3d
        self._intersectors = {}

    def _intersector(self, mode):
        if mode not in self._intersectors:
            cats = {
                "ceiling": [BuiltInCategory.OST_Ceilings],
                "face": [BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Floors,
                         BuiltInCategory.OST_Roofs, BuiltInCategory.OST_StructuralFraming],
                "wall": [BuiltInCategory.OST_Walls],
            }[mode]
            ri = ReferenceIntersector(_category_filter(cats), FindReferenceTarget.Face, self.view)
            ri.FindReferencesInRevitLinks = config.SEARCH_REVIT_LINKS
            self._intersectors[mode] = ri
        return self._intersectors[mode]

    def _hit(self, ctx, direction):
        ref = ctx.GetReference()
        pt = ref.GlobalPoint
        normal = _face_normal(self.doc, ref, pt, direction.Negate())
        elem, linked = _host_element(self.doc, ref)
        return HostHit(ref, pt, normal, elem, linked, ctx.Proximity)

    def find_above(self, mode, x, y, level_z, max_dist_ft):
        """Nearest ceiling ('ceiling') or ceiling/slab/roof/beam ('face') face
        straight above (x, y), searching from just above the level."""
        origin = XYZ(x, y, level_z + 0.01)
        ctx = self._intersector(mode).FindNearest(origin, XYZ.BasisZ)
        if ctx is None or ctx.Proximity > max_dist_ft:
            return None
        return self._hit(ctx, XYZ.BasisZ)

    def find_wall(self, x, y, z, max_dist_ft, start_angle=0.0, rays=16):
        """Nearest wall face around (x, y) at height z. Casts `rays` horizontal
        rays (starting at the CAD block's rotation) and keeps the shortest hit."""
        ri = self._intersector("wall")
        origin = XYZ(x, y, z)
        best, best_dir = None, None
        for i in range(rays):
            a = start_angle + 2.0 * math.pi * i / rays
            d = XYZ(math.cos(a), math.sin(a), 0)
            ctx = ri.FindNearest(origin, d)
            if ctx is not None and ctx.Proximity <= max_dist_ft:
                if best is None or ctx.Proximity < best.Proximity:
                    best, best_dir = ctx, d
        if best is None:
            return None
        hit = self._hit(best, best_dir)
        # Keep only the horizontal part of the normal (vertical walls).
        n = XYZ(hit.normal.X, hit.normal.Y, 0)
        if n.GetLength() < 1e-6:
            n = best_dir.Negate()
        n = n.Normalize()
        hit.face_normal = n
        # Room side = back towards the CAD point (differs from the face normal
        # only if the CAD point lies inside the wall thickness).
        hit.normal = n.Negate() if n.DotProduct(best_dir) > 0 else n
        return hit

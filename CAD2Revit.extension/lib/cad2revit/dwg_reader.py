# -*- coding: utf-8 -*-
"""Read block references (insert point, rotation, scale, mirror) from a DWG
ImportInstance (linked or imported CAD).

Coordinates: Revit converts DWG units to feet when the file is linked, and the
top-level GeometryInstance transform is the link's placement in this model
(position, rotation, and the shared-coordinates offset if the link was placed
"By Shared Coordinates"). Multiplying that transform with each block's own
transform therefore gives the block insertion point directly in this model's
internal coordinates - no manual unit or coordinate conversion is needed.

Note: the Revit API does not expose AutoCAD block ATTRIBUTES or dynamic-block
properties, only the block name and geometry. See README > Limitations.
"""
import math
from Autodesk.Revit.DB import Options, GeometryInstance, ViewDetailLevel


class BlockRef(object):
    def __init__(self, name, transform, link_transform, depth):
        self.name = (name or u"").strip()
        self.transform = transform
        self.depth = depth                    # 1 = top-level block in the DWG
        self.point = transform.Origin         # model coordinates, feet
        bx = transform.BasisX
        self.rotation = math.atan2(bx.Y, bx.X)  # radians, in plan
        self.mirrored = transform.HasReflection
        # Block scale relative to the DWG itself (1.0 = inserted at scale 1).
        base = link_transform.BasisX.GetLength() or 1.0
        self.scale_x = bx.GetLength() / base
        self.scale_y = transform.BasisY.GetLength() / base

    @property
    def key(self):
        return self.name.lower()

    @property
    def is_anonymous(self):
        """Dynamic / anonymous blocks show up as '*U12' or similar."""
        return self.name.startswith(u"*") or not self.name


def _symbol_name(doc, geom_inst):
    try:
        sym = geom_inst.Symbol
        if sym is not None:
            return sym.Name
    except Exception:
        pass
    try:
        el = doc.GetElement(geom_inst.GetSymbolGeometryId().SymbolId)
        if el is not None:
            return el.Name
    except Exception:
        pass
    return u""


def _walk(doc, geom, parent_tf, link_tf, depth, include_nested, result):
    for g in geom:
        if not isinstance(g, GeometryInstance):
            continue
        tf = parent_tf.Multiply(g.Transform)
        result.append(BlockRef(_symbol_name(doc, g), tf, link_tf, depth))
        if include_nested:
            _walk(doc, g.GetSymbolGeometry(), tf, link_tf, depth + 1, include_nested, result)


def read_blocks(doc, import_inst, include_nested=False):
    """Return a list of BlockRef in Revit model coordinates (feet)."""
    opts = Options()
    opts.ComputeReferences = False
    opts.IncludeNonVisibleObjects = False
    if import_inst.ViewSpecific:
        # DWGs linked with "Current view only" only have geometry in their view.
        opts.View = doc.GetElement(import_inst.OwnerViewId)
    else:
        opts.DetailLevel = ViewDetailLevel.Fine
    geo = import_inst.get_Geometry(opts)
    result = []
    if geo is None:
        return result
    for g in geo:
        # Top-level instance = the whole DWG; its transform = link placement.
        if isinstance(g, GeometryInstance):
            _walk(doc, g.GetSymbolGeometry(), g.Transform, g.Transform, 1,
                  include_nested, result)
    return result


def count_by_name(blocks):
    counts = {}
    for b in blocks:
        counts[b.name] = counts.get(b.name, 0) + 1
    return counts

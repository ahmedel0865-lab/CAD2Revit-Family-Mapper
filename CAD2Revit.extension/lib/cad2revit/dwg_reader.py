# -*- coding: utf-8 -*-
"""Read block references (insert point, rotation, mirror) from a DWG ImportInstance.

Note: the Revit API does not expose AutoCAD block ATTRIBUTES, only the block
geometry and name. See README > Limitations.
"""
import math
from Autodesk.Revit.DB import Options, GeometryInstance, ViewDetailLevel


class BlockRef(object):
    def __init__(self, name, transform, depth):
        self.name = (name or u"").strip()
        self.transform = transform
        self.depth = depth
        self.point = transform.Origin
        bx = transform.BasisX
        self.rotation = math.atan2(bx.Y, bx.X)  # radians, in plan
        self.mirrored = transform.HasReflection

    @property
    def key(self):
        return self.name.lower()


def _symbol_name(doc, geom_inst):
    try:
        sym = geom_inst.Symbol
        if sym is not None:
            return sym.Name
    except Exception:
        pass
    try:
        sym_id = geom_inst.GetSymbolGeometryId().SymbolId
        el = doc.GetElement(sym_id)
        if el is not None:
            return el.Name
    except Exception:
        pass
    return u""


def _walk(doc, geom, parent_tf, depth, include_nested, result):
    for g in geom:
        if not isinstance(g, GeometryInstance):
            continue
        tf = parent_tf.Multiply(g.Transform)
        result.append(BlockRef(_symbol_name(doc, g), tf, depth))
        if include_nested:
            _walk(doc, g.GetSymbolGeometry(), tf, depth + 1, include_nested, result)


def read_blocks(doc, import_inst, include_nested=False):
    """Return a list of BlockRef in Revit model coordinates (feet)."""
    opts = Options()
    opts.ComputeReferences = False
    opts.IncludeNonVisibleObjects = False
    opts.DetailLevel = ViewDetailLevel.Fine
    geo = import_inst.get_Geometry(opts)
    result = []
    if geo is None:
        return result
    for g in geo:
        # Top-level instance = the whole DWG; its transform = link placement.
        if isinstance(g, GeometryInstance):
            _walk(doc, g.GetSymbolGeometry(), g.Transform, 1, include_nested, result)
    return result


def count_by_name(blocks):
    counts = {}
    for b in blocks:
        counts[b.name] = counts.get(b.name, 0) + 1
    return counts

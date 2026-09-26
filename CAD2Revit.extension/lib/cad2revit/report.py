# -*- coding: utf-8 -*-
"""Placement results, summaries and the CSV log. Pure Python (unit tested)."""
import math

MM_PER_FOOT = 304.8

# Result.status values
PLACED = "placed"          # created (or, in preview, would be created)
DUPLICATE = "duplicate"    # an instance already exists at this location
UNMAPPED = "unmapped"      # block name not in the mapping file
SKIPPED = "skipped"        # mapped, but family/type not loaded in the project
FAILED = "failed"          # Revit refused the placement / no host found

LOG_HEADER = ["Status", "CAD_Block", "Family", "Type", "ElementId", "Host",
              "X_mm", "Y_mm", "Z_mm", "Rotation_deg", "Block_Scale", "Mirrored",
              "Message"]


class Result(object):
    def __init__(self, block, row, status, element_id=None, message=u"",
                 point=None, rotation=None, host=u"", count=1):
        self.block = block            # BlockRef, or block name for unmapped
        self.row = row                # MapRow or None
        self.status = status
        self.element_id = element_id  # int or None
        self.message = message
        self.point = point            # (x, y, z) feet, model internal coordinates
        self.rotation = rotation      # radians
        self.host = host              # text, e.g. "Ceilings (linked)"
        self.count = count            # >1 only for grouped "unmapped" rows

    @property
    def block_name(self):
        return getattr(self.block, "name", self.block) or u""


def _xyz(p):
    if p is None:
        return None
    if hasattr(p, "X"):
        return (p.X, p.Y, p.Z)
    return tuple(p)


def _mm(v):
    return u"" if v is None else u"%.1f" % (v * MM_PER_FOOT)


def _deg(rad):
    if rad is None:
        return u""
    d = math.degrees(rad) % 360.0
    return u"%.2f" % (0.0 if abs(d - 360.0) < 0.005 else d)


def summarize(results):
    """Returns dict with:
       by_type:  [(family : type, placed count)] sorted
       status:   {status: count} (unmapped counted per instance)
       unmapped: [(block name, instances)] sorted
       problems: [Result] failed / skipped (not duplicates)"""
    by_type, status, unmapped, problems = {}, {}, {}, []
    for r in results:
        status[r.status] = status.get(r.status, 0) + r.count
        if r.status == PLACED and r.row is not None:
            by_type[r.row.label] = by_type.get(r.row.label, 0) + 1
        elif r.status == UNMAPPED:
            unmapped[r.block_name] = unmapped.get(r.block_name, 0) + r.count
        elif r.status in (FAILED, SKIPPED):
            problems.append(r)
    return {
        "by_type": sorted(by_type.items(), key=lambda kv: kv[0].lower()),
        "status": status,
        "unmapped": sorted(unmapped.items(), key=lambda kv: kv[0].lower()),
        "problems": problems,
    }


def group_problems(problems):
    """[(status, block, message, count)] - identical problems collapsed."""
    groups = {}
    for r in problems:
        k = (r.status, r.block_name, r.message)
        groups[k] = groups.get(k, 0) + 1
    return sorted([k + (n,) for k, n in groups.items()], key=lambda g: (g[0], g[1].lower()))


def log_rows(results):
    """Rows for the CSV log (one per block instance / unmapped block name)."""
    rows = []
    for r in results:
        p = _xyz(r.point)
        b = r.block
        scale = u""
        if hasattr(b, "scale_x"):
            scale = u"%.3g" % b.scale_x if abs(b.scale_x - b.scale_y) < 1e-6 else \
                u"%.3g x %.3g" % (b.scale_x, b.scale_y)
        rows.append([
            r.status, r.block_name,
            r.row.family if r.row else u"", r.row.type_name if r.row else u"",
            r.element_id if r.element_id is not None else u"",
            r.host,
            _mm(p[0]) if p else u"", _mm(p[1]) if p else u"", _mm(p[2]) if p else u"",
            _deg(r.rotation), scale,
            u"yes" if getattr(b, "mirrored", False) else u"",
            r.message if r.count == 1 else u"{} instance(s). {}".format(r.count, r.message).strip(),
        ])
    return rows

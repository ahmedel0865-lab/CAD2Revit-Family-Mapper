# -*- coding: utf-8 -*-
"""Load and validate the CAD-block -> Revit-family mapping CSV."""
from Autodesk.Revit.DB import FilteredElementCollector, FamilySymbol
from cad2revit.utils import read_csv, elem_name

REQUIRED_COLUMNS = ["CAD_Block_Name", "Revit_Family_Name", "Revit_Type_Name"]
HOST_TYPES = ("none", "face")


class MapRow(object):
    def __init__(self, block, family, type_name, offset_mm, rot_deg, host):
        self.block = block
        self.family = family
        self.type_name = type_name
        self.offset_mm = offset_mm
        self.rot_deg = rot_deg
        self.host = host
        self.symbol = None  # resolved FamilySymbol


def _num(text, default=0.0):
    try:
        return float(text) if text not in (None, u"") else default
    except ValueError:
        return None


def load_mapping(doc, path):
    """Return (dict key->MapRow, list of error strings)."""
    rows = read_csv(path)
    errors = []
    if not rows:
        return {}, ["Mapping file is empty: {}".format(path)]
    missing = [c for c in REQUIRED_COLUMNS if c not in rows[0]]
    if missing:
        return {}, ["Missing columns: {}".format(", ".join(missing))]

    symbols = {}
    for s in FilteredElementCollector(doc).OfClass(FamilySymbol):
        symbols[(s.Family.Name.lower(), elem_name(s).lower())] = s

    mapping = {}
    for i, r in enumerate(rows, start=2):
        block = r.get("CAD_Block_Name", u"")
        if not block:
            continue
        offset = _num(r.get("Offset_From_Level_mm"))
        rot = _num(r.get("Rotation_Adjustment_deg"))
        host = (r.get("Host_Type") or u"none").lower()
        if host not in HOST_TYPES:
            errors.append(u"Row {}: Host_Type '{}' not supported (use none/face) - using none".format(i, host))
            host = "none"
        if offset is None or rot is None:
            errors.append(u"Row {}: offset/rotation must be numbers - row skipped".format(i))
            continue
        row = MapRow(block, r["Revit_Family_Name"], r["Revit_Type_Name"], offset, rot, host)
        row.symbol = symbols.get((row.family.lower(), row.type_name.lower()))
        if row.symbol is None:
            errors.append(u"Row {}: family '{}' / type '{}' is not loaded in the project".format(
                i, row.family, row.type_name))
        if block.lower() in mapping:
            errors.append(u"Row {}: duplicate block '{}' - last row wins".format(i, block))
        mapping[block.lower()] = row
    return mapping, errors

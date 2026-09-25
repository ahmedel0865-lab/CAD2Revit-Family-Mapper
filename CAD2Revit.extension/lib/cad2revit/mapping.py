# -*- coding: utf-8 -*-
"""Load and validate the CAD-block -> Revit-family mapping (CSV or XLSX).

parse_rows() is pure Python (unit tested); resolve_symbols() needs Revit.
"""
import re
from cad2revit.tables import read_table

# Canonical field -> accepted header spellings (compared after lower-casing and
# removing everything that is not a letter/digit, so "Offset_From_Level (mm)",
# "Offset From Level mm" and "offset_from_level_mm" all match).
HEADER_ALIASES = {
    "block": ("cadblockname", "blockname", "cadblock", "block"),
    "family": ("revitfamilyname", "familyname", "family"),
    "type": ("revittypename", "typename", "type"),
    "offset": ("offsetfromlevelmm", "offsetfromlevel", "offsetmm", "offset", "elevationmm"),
    "rotation": ("rotationadjustmentdeg", "rotationadjustment", "rotationdeg", "rotation"),
    "host": ("hosttype", "host", "hosting"),
}
REQUIRED = ("block", "family", "type")

# Host_Type cell value -> internal host mode
HOST_VALUES = {
    "": "none", "none": "none", "nonhosted": "none", "unhosted": "none",
    "level": "none", "levelbased": "none", "no": "none",
    "ceiling": "ceiling",
    "face": "face",
    "wall": "wall",
}
HOST_MODES = ("none", "ceiling", "face", "wall")

TEMPLATE_HEADER = ["CAD_Block_Name", "Revit_Family_Name", "Revit_Type_Name",
                   "Offset_From_Level_mm", "Rotation_Adjustment_deg", "Host_Type"]


def _norm(text):
    return re.sub(r"[^a-z0-9]", "", (text or u"").lower())


class MapRow(object):
    def __init__(self, block, family, type_name, offset_mm=0.0, rot_deg=0.0, host="none", line=0):
        self.block = block
        self.family = family
        self.type_name = type_name
        self.offset_mm = offset_mm
        self.rot_deg = rot_deg
        self.host = host
        self.line = line      # row number in the source file (for messages)
        self.symbol = None    # resolved Revit FamilySymbol

    @property
    def label(self):
        return u"{} : {}".format(self.family, self.type_name)


def _num(text):
    """'' -> 0.0, '2800' -> 2800.0, '2,5' -> 2.5, 'abc' -> None."""
    text = (text or u"").strip()
    if not text:
        return 0.0
    if u"," in text and u"." not in text:
        text = text.replace(u",", u".")
    try:
        return float(text)
    except ValueError:
        return None


def _column_map(header_keys):
    """Map canonical field -> actual header text present in the file."""
    by_norm = dict((_norm(h), h) for h in header_keys)
    cols = {}
    for field, aliases in HEADER_ALIASES.items():
        for a in aliases:
            if a in by_norm:
                cols[field] = by_norm[a]
                break
    return cols


def parse_rows(rows):
    """rows = list of dicts from read_table(). Returns (dict key->MapRow, errors).
    Keys are lower-cased block names (block matching is case-insensitive)."""
    errors = []
    if not rows:
        return {}, [u"Mapping file has no data rows"]
    cols = _column_map(rows[0].keys())
    missing = [f for f in REQUIRED if f not in cols]
    if missing:
        return {}, [u"Missing column(s): {}. Expected headers: {}".format(
            u", ".join(missing), u", ".join(TEMPLATE_HEADER))]

    def get(r, field):
        return (r.get(cols[field]) or u"").strip() if field in cols else u""

    mapping = {}
    for line, r in enumerate(rows, start=2):
        block = get(r, "block")
        family = get(r, "family")
        if not block:
            continue
        if not family:
            continue  # template row left blank on purpose = "do not place"
        type_name = get(r, "type")
        if not type_name:
            errors.append(u"Row {}: '{}' has a family but no type - row skipped".format(line, block))
            continue
        offset = _num(get(r, "offset"))
        rot = _num(get(r, "rotation"))
        if offset is None or rot is None:
            errors.append(u"Row {}: offset/rotation must be numbers - row skipped".format(line))
            continue
        raw_host = get(r, "host")
        host = HOST_VALUES.get(_norm(raw_host))
        if host is None:
            errors.append(u"Row {}: Host_Type '{}' not recognised (use {}) - using none".format(
                line, raw_host, u"/".join(HOST_MODES)))
            host = "none"
        key = block.lower()
        if key in mapping:
            errors.append(u"Row {}: block '{}' is mapped twice - last row wins".format(line, block))
        mapping[key] = MapRow(block, family, type_name, offset, rot, host, line)
    return mapping, errors


def resolve_symbols(doc, mapping):
    """Attach the loaded FamilySymbol to each MapRow. Returns error strings."""
    from Autodesk.Revit.DB import FilteredElementCollector, FamilySymbol
    from cad2revit.utils import elem_name

    symbols = {}
    for s in FilteredElementCollector(doc).OfClass(FamilySymbol):
        symbols[(s.Family.Name.strip().lower(), elem_name(s).strip().lower())] = s
    errors = []
    for row in sorted(mapping.values(), key=lambda r: r.line):
        row.symbol = symbols.get((row.family.lower(), row.type_name.lower()))
        if row.symbol is None:
            errors.append(u"Row {}: family '{}' / type '{}' is not loaded in the project".format(
                row.line, row.family, row.type_name))
    return errors


def load_mapping(doc, path):
    """Read + validate + resolve. Returns (dict key->MapRow, list of errors)."""
    try:
        rows = read_table(path)
    except Exception as ex:
        return {}, [u"Could not read '{}': {}".format(path, ex)]
    mapping, errors = parse_rows(rows)
    if doc is not None and mapping:
        errors.extend(resolve_symbols(doc, mapping))
    return mapping, errors

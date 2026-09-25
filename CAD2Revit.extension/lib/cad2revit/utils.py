# -*- coding: utf-8 -*-
"""Small Revit helpers that work in both IronPython and CPython engines."""
# CSV helpers moved to tables.py; re-exported here for older imports.
from cad2revit.tables import write_csv, read_csv, write_table, read_table  # noqa: F401


def elem_name(element):
    """Element.Name is unreliable on some types in IronPython; use the getter."""
    from Autodesk.Revit.DB import Element
    try:
        return Element.Name.GetValue(element)
    except Exception:
        return element.Name

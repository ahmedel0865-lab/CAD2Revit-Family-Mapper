# -*- coding: utf-8 -*-
"""Revit version compatibility helpers (Revit 2022 - 2026).

Revit 2024 introduced 64-bit ElementId.Value and Revit 2026 removed the old
ElementId.IntegerValue, so never call either directly - use eid_int().
"""


def eid_int(element_id):
    """Return an ElementId as a plain Python int on any Revit version."""
    if element_id is None:
        return None
    try:
        return int(element_id.Value)          # Revit 2024+
    except AttributeError:
        return int(element_id.IntegerValue)   # Revit 2022 / 2023

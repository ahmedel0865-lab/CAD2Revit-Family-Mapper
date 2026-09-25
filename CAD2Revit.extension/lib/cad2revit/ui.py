# -*- coding: utf-8 -*-
"""pyRevit dialogs."""
from pyrevit import forms
from Autodesk.Revit.DB import FilteredElementCollector, ImportInstance, Level
from cad2revit.utils import elem_name


class _Item(object):
    def __init__(self, element, name):
        self.element = element
        self.name = name


def pick_import_instance(doc):
    imports = list(FilteredElementCollector(doc).OfClass(ImportInstance))
    if not imports:
        forms.alert("No linked or imported DWG found in this project.", exitscript=True)
    items = []
    for imp in imports:
        t = doc.GetElement(imp.GetTypeId())
        kind = "Link" if imp.IsLinked else "Import"
        items.append(_Item(imp, u"{} - {} (id {})".format(
            kind, elem_name(t) if t else "DWG", imp.Id.IntegerValue)))
    if len(items) == 1:
        return items[0].element
    picked = forms.SelectFromList.show(items, name_attr="name",
                                       title="Select the DWG", multiselect=False)
    return picked.element if picked else None


def pick_level(doc):
    levels = sorted(FilteredElementCollector(doc).OfClass(Level), key=lambda l: l.Elevation)
    items = [_Item(l, elem_name(l)) for l in levels]
    active = doc.ActiveView.GenLevel
    if active is not None:
        items.sort(key=lambda i: 0 if i.element.Id == active.Id else 1)
    picked = forms.SelectFromList.show(items, name_attr="name",
                                       title="Target level", multiselect=False)
    return picked.element if picked else None

# -*- coding: utf-8 -*-
"""pyRevit dialogs."""
from pyrevit import forms
from Autodesk.Revit.DB import FilteredElementCollector, ImportInstance, Level
from Autodesk.Revit.UI.Selection import ISelectionFilter, ObjectType
from Autodesk.Revit.Exceptions import OperationCanceledException
from cad2revit.utils import elem_name

PICK_IN_VIEW = "Pick in view"
PICK_FROM_LIST = "Choose from list"


class _Item(object):
    def __init__(self, element, name):
        self.element = element
        self.name = name


class _CadOnlyFilter(ISelectionFilter):
    """Only CAD links/imports can be clicked while picking."""

    def AllowElement(self, element):
        return isinstance(element, ImportInstance)

    def AllowReference(self, reference, position):
        return False


def _describe(doc, imp):
    t = doc.GetElement(imp.GetTypeId())
    kind = "Link" if imp.IsLinked else "Import"
    return u"{} - {} (id {})".format(kind, elem_name(t) if t else "DWG", imp.Id.IntegerValue)


def _selected_imports(doc, uidoc):
    """CAD instances the user already selected before clicking the button."""
    if uidoc is None:
        return []
    result = []
    for eid in uidoc.Selection.GetElementIds():
        el = doc.GetElement(eid)
        if isinstance(el, ImportInstance):
            result.append(el)
    return result


def _pick_in_view(uidoc):
    try:
        ref = uidoc.Selection.PickObject(
            ObjectType.Element, _CadOnlyFilter(),
            "Click the CAD link/import to use (Esc to cancel)")
    except OperationCanceledException:
        return None
    return uidoc.Document.GetElement(ref.ElementId)


def _pick_from_list(doc, imports):
    items = [_Item(imp, _describe(doc, imp)) for imp in imports]
    picked = forms.SelectFromList.show(items, name_attr="name",
                                       title="Select the DWG", multiselect=False)
    return picked.element if picked else None


def pick_import_instance(doc, uidoc=None):
    """Return the CAD ImportInstance to work on, or None if cancelled.

    1. If exactly one CAD is already selected in the view, use it.
    2. Otherwise ask: pick it in the view, or choose it from a list.
    """
    imports = list(FilteredElementCollector(doc).OfClass(ImportInstance))
    if not imports:
        forms.alert("No linked or imported DWG found in this project.", exitscript=True)

    preselected = _selected_imports(doc, uidoc)
    if len(preselected) == 1:
        return preselected[0]

    if uidoc is None:
        return imports[0] if len(imports) == 1 else _pick_from_list(doc, imports)

    mode = forms.CommandSwitchWindow.show(
        [PICK_IN_VIEW, PICK_FROM_LIST],
        message="How do you want to select the CAD? ({} in project)".format(len(imports)))
    if not mode:
        return None
    if mode == PICK_IN_VIEW:
        return _pick_in_view(uidoc)
    return _pick_from_list(doc, imports)


def pick_level(doc):
    levels = sorted(FilteredElementCollector(doc).OfClass(Level), key=lambda l: l.Elevation)
    items = [_Item(l, elem_name(l)) for l in levels]
    active = doc.ActiveView.GenLevel
    if active is not None:
        items.sort(key=lambda i: 0 if i.element.Id == active.Id else 1)
    picked = forms.SelectFromList.show(items, name_attr="name",
                                       title="Target level", multiselect=False)
    return picked.element if picked else None

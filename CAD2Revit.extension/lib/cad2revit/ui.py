# -*- coding: utf-8 -*-
"""pyRevit dialogs."""
import os
from pyrevit import forms, script
from Autodesk.Revit.DB import FilteredElementCollector, ImportInstance, Level
from cad2revit import config
from cad2revit.compat import eid_int
from cad2revit.utils import elem_name

XAML = os.path.join(os.path.dirname(__file__), "main_dialog.xaml")
MAPPING_EXT = "CSV / Excel (*.csv;*.xlsx)|*.csv;*.xlsx|All files (*.*)|*.*"


class _Item(object):
    def __init__(self, element, name):
        self.element = element
        self.name = name


def import_items(doc):
    items = []
    for imp in FilteredElementCollector(doc).OfClass(ImportInstance):
        t = doc.GetElement(imp.GetTypeId())
        kind = u"Link" if imp.IsLinked else u"Import"
        if imp.ViewSpecific:
            kind += u", view-only"
        items.append(_Item(imp, u"{} - {} (id {})".format(
            elem_name(t) if t else u"DWG", kind, eid_int(imp.Id))))
    return sorted(items, key=lambda i: i.name.lower())


def level_items(doc):
    levels = sorted(FilteredElementCollector(doc).OfClass(Level), key=lambda l: l.ProjectElevation)
    return [_Item(l, u"{}  ({:+.0f} mm)".format(elem_name(l), config.ft_to_mm(l.Elevation)))
            for l in levels]


def default_level_index(doc, items, imp=None):
    candidates = [doc.ActiveView.GenLevel]
    if imp is not None and imp.ViewSpecific:
        owner = doc.GetElement(imp.OwnerViewId)
        candidates.insert(0, getattr(owner, "GenLevel", None))
    for lvl in candidates:
        if lvl is not None:
            for i, it in enumerate(items):
                if it.element.Id == lvl.Id:
                    return i
    return 0


def pick_import_instance(doc):
    items = import_items(doc)
    if not items:
        forms.alert("No linked or imported DWG found in this project.", exitscript=True)
    if len(items) == 1:
        return items[0].element
    picked = forms.SelectFromList.show(items, name_attr="name",
                                       title="Select the DWG", multiselect=False)
    return picked.element if picked else None


def pick_level(doc):
    items = level_items(doc)
    items.insert(0, items.pop(default_level_index(doc, items)))
    picked = forms.SelectFromList.show(items, name_attr="name",
                                       title="Target level", multiselect=False)
    return picked.element if picked else None


def pick_mapping_file(initial=None):
    return forms.pick_file(files_filter=MAPPING_EXT, title="Select mapping file (CSV or XLSX)",
                           init_dir=os.path.dirname(initial) if initial else "")


# ------------------------------------------------------------- main dialog

class PlaceOptions(object):
    def __init__(self, import_inst, mapping_path, level, mode, include_nested):
        self.import_inst = import_inst
        self.mapping_path = mapping_path
        self.level = level
        self.mode = mode                  # "Preview" or "Run"
        self.include_nested = include_nested


class _MainDialog(forms.WPFWindow):
    def __init__(self, doc, last_mapping):
        forms.WPFWindow.__init__(self, XAML)
        self.result = None
        self.dwg_cb.ItemsSource = import_items(doc)
        self.dwg_cb.SelectedIndex = 0
        levels = level_items(doc)
        self.level_cb.ItemsSource = levels
        imp = self.dwg_cb.SelectedItem.element if self.dwg_cb.SelectedItem else None
        self.level_cb.SelectedIndex = default_level_index(doc, levels, imp)
        self.map_tb.Text = last_mapping or u""
        self.nested_cb.IsChecked = config.INCLUDE_NESTED_BLOCKS

    def browse_click(self, sender, args):
        path = pick_mapping_file(self.map_tb.Text)
        if path:
            self.map_tb.Text = path

    def _finish(self, mode):
        path = (self.map_tb.Text or u"").strip().strip('"')
        if self.dwg_cb.SelectedItem is None or self.level_cb.SelectedItem is None:
            forms.alert("Select a DWG and a level.")
            return
        if not os.path.isfile(path):
            forms.alert("Mapping file not found:\n{}".format(path))
            return
        self.result = PlaceOptions(self.dwg_cb.SelectedItem.element, path,
                                   self.level_cb.SelectedItem.element, mode,
                                   bool(self.nested_cb.IsChecked))
        self.Close()

    def preview_click(self, sender, args):
        self._finish("Preview")

    def run_click(self, sender, args):
        self._finish("Run")


def _step_by_step(doc, last_mapping):
    """Fallback if the WPF dialog cannot be shown (e.g. CPython engine)."""
    imp = pick_import_instance(doc)
    if imp is None:
        return None
    path = pick_mapping_file(last_mapping)
    if not path:
        return None
    level = pick_level(doc)
    if level is None:
        return None
    mode = forms.CommandSwitchWindow.show(["Preview", "Run"],
                                          message="Preview counts or place families?")
    if not mode:
        return None
    return PlaceOptions(imp, path, level, mode, config.INCLUDE_NESTED_BLOCKS)


def ask_place_options(doc):
    """Show the main dialog. Returns PlaceOptions or None (cancelled)."""
    if not import_items(doc):
        forms.alert("No linked or imported DWG found in this project.", exitscript=True)
    cfg = script.get_config()
    last = cfg.get_option("last_mapping", u"")
    try:
        dlg = _MainDialog(doc, last)
        dlg.ShowDialog()
        opts = dlg.result
    except Exception:
        opts = _step_by_step(doc, last)
    if opts is not None:
        cfg.last_mapping = opts.mapping_path
        script.save_config()
    return opts

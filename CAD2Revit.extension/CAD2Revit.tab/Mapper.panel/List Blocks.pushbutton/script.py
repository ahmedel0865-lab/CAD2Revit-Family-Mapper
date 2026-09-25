# -*- coding: utf-8 -*-
__title__ = "List\nBlocks"
__doc__ = "List every block name in a linked/imported DWG and export a mapping template CSV."

from pyrevit import revit, forms, script
from cad2revit import ui, dwg_reader, config
from cad2revit.utils import write_csv

doc = revit.doc
output = script.get_output()

imp = ui.pick_import_instance(doc)
if imp is None:
    script.exit()

blocks = dwg_reader.read_blocks(doc, imp, config.INCLUDE_NESTED_BLOCKS)
counts = dwg_reader.count_by_name(blocks)
if not counts:
    forms.alert("No blocks found in the selected DWG.", exitscript=True)

names = sorted(counts.keys(), key=lambda n: n.lower())
output.print_md("## Blocks found: {} types, {} instances".format(len(names), len(blocks)))
output.print_table([[n, counts[n]] for n in names], columns=["Block name", "Count"])

if forms.alert("Export a mapping template with these block names?", yes=True, no=True):
    path = forms.save_file(file_ext="csv", default_name="cad2revit_mapping")
    if path:
        header = ["CAD_Block_Name", "Revit_Family_Name", "Revit_Type_Name",
                  "Offset_From_Level_mm", "Rotation_Adjustment_deg", "Host_Type"]
        write_csv(path, header, [[n, "", "", 0, 0, "none"] for n in names])
        output.print_md("Template saved: `{}`".format(path))

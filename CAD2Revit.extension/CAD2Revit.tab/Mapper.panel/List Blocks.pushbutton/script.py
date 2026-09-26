# -*- coding: utf-8 -*-
__title__ = "List\nBlocks"
__doc__ = ("List every block name in a linked/imported DWG (with counts) and "
           "export a mapping template (XLSX or CSV).")

from pyrevit import revit, forms, script
from cad2revit import ui, dwg_reader, config
from cad2revit.mapping import TEMPLATE_HEADER
from cad2revit.tables import write_table

doc = revit.doc
output = script.get_output()

imp = ui.pick_import_instance(doc)
if imp is None:
    script.exit()

blocks = dwg_reader.read_blocks(doc, imp, config.INCLUDE_NESTED_BLOCKS)
counts = dwg_reader.count_by_name(blocks)
if not counts:
    forms.alert("No blocks found in the selected DWG.\n\n"
                "If the drawing was exploded before linking, it contains no blocks "
                "(see README > Limitations).", exitscript=True)

names = sorted(counts.keys(), key=lambda n: n.lower())
anonymous = [n for n in names if n.startswith(u"*") or not n]
mirrored = {}
for b in blocks:
    if b.mirrored:
        mirrored[b.name] = mirrored.get(b.name, 0) + 1

output.print_md("## Blocks found: {} names, {} instances".format(len(names), len(blocks)))
output.print_table([[n, counts[n], mirrored.get(n, u"")] for n in names],
                   columns=["Block name", "Count", "Mirrored"])
if anonymous:
    output.print_md("**Note:** {} anonymous block name(s) (e.g. `*U12`) found. These are usually "
                    "dynamic blocks or unnamed groups - see README > Limitations.".format(len(anonymous)))

if forms.alert("Export a mapping template with these block names?", yes=True, no=True):
    path = forms.save_file(file_ext="xlsx",
                           files_filter="Excel workbook (*.xlsx)|*.xlsx|CSV UTF-8 (*.csv)|*.csv",
                           default_name="cad2revit_mapping")
    if path:
        write_table(path, TEMPLATE_HEADER,
                    [[n, u"", u"", 0, 0, u"non-hosted"] for n in names if n not in anonymous])
        output.print_md("Template saved: `{}`".format(path))
        output.print_md("Fill in family, type, offset and host type. Leave the family "
                        "empty for blocks you do not want to place.")

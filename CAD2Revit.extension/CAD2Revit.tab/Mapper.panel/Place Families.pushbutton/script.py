# -*- coding: utf-8 -*-
__title__ = "Place\nFamilies"
__doc__ = "Replace DWG blocks with Revit families using a CSV mapping file."

import os
import datetime
from pyrevit import revit, forms, script
from cad2revit import ui, dwg_reader, mapping as mapping_mod, placer, config
from cad2revit.utils import write_csv

doc = revit.doc
output = script.get_output()

imp = ui.pick_import_instance(doc)
if imp is None:
    script.exit()

map_path = forms.pick_file(file_ext="csv", title="Select mapping CSV")
if not map_path:
    script.exit()

mapping, errors = mapping_mod.load_mapping(doc, map_path)
if errors:
    output.print_md("### Mapping warnings")
    for e in errors:
        output.print_md("- " + e)
if not mapping:
    forms.alert("No valid rows in the mapping file.", exitscript=True)

level = ui.pick_level(doc)
if level is None:
    script.exit()

mode = forms.CommandSwitchWindow.show(["Preview", "Run"], message="Preview counts or place families?")
if not mode:
    script.exit()

blocks = dwg_reader.read_blocks(doc, imp, config.INCLUDE_NESTED_BLOCKS)
mapped, unmapped = placer.plan(blocks, mapping)

if mode == "Preview":
    per_type = {}
    for b, row in mapped:
        k = u"{} : {}".format(row.family, row.type_name)
        per_type[k] = per_type.get(k, 0) + 1
    output.print_md("## Preview - nothing was changed")
    output.print_table(sorted(per_type.items()), columns=["Family : Type", "Will place"])
    if unmapped:
        output.print_table(sorted(unmapped.items()), columns=["Unmapped block", "Count"])
    script.exit()

results = placer.place_all(doc, blocks, mapping, level)

summary = {}
for r in results:
    summary[r.status] = summary.get(r.status, 0) + 1
output.print_md("## Done")
output.print_table(sorted(summary.items()), columns=["Status", "Count"])

problems = [r for r in results if r.status in ("failed", "unmapped", "skipped")]
if problems:
    output.print_table([[r.status, r.block, r.message] for r in problems],
                       columns=["Status", "Block", "Message"])

stamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
log_path = os.path.join(os.path.dirname(map_path), "cad2revit_log_{}.csv".format(stamp))
rows = []
for r in results:
    rows.append([r.status, r.block,
                 r.row.family if r.row else "", r.row.type_name if r.row else "",
                 r.element_id or "", r.message])
write_csv(log_path, ["Status", "CAD_Block", "Family", "Type", "ElementId", "Message"], rows)
output.print_md("Log saved: `{}`".format(log_path))

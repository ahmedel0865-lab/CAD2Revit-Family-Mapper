# -*- coding: utf-8 -*-
__title__ = "Place\nFamilies"
__doc__ = "Replace DWG blocks with Revit families using a CSV/XLSX mapping file."

import os
import datetime
from pyrevit import revit, forms, script
from cad2revit import ui, dwg_reader, mapping as mapping_mod, placer, report
from cad2revit.tables import write_csv

doc = revit.doc
output = script.get_output()

# 1. Dialog: DWG, mapping file, level, Preview/Run.
opts = ui.ask_place_options(doc)
if opts is None:
    script.exit()
preview = (opts.mode == "Preview")

# 2. Mapping file.
mapping, errors = mapping_mod.load_mapping(doc, opts.mapping_path)
if errors:
    output.print_md("### Mapping warnings")
    for e in errors:
        output.print_md(u"- " + e)
if not mapping:
    forms.alert("No valid rows in the mapping file.", exitscript=True)

# 3. Blocks from the DWG.
blocks = dwg_reader.read_blocks(doc, opts.import_inst, opts.include_nested)
if not blocks:
    forms.alert("No blocks found in the selected DWG.", exitscript=True)

# 4. Place (Preview = same work, rolled back at the end).
with forms.ProgressBar(title="CAD2Revit: {} ({{value}} of {{max_value}})".format(opts.mode)) as pb:
    def progress(i, n):
        if i % 25 == 0:
            pb.update_progress(i, n)
    results = placer.place_all(doc, blocks, mapping, opts.level,
                               dry_run=preview, progress=progress)

# 5. Summary.
s = report.summarize(results)
st = s["status"]
title = "Preview - nothing was changed" if preview else "Done"
output.print_md(u"## {}".format(title))
output.print_md(u"**{}** {} placed, **{}** duplicates skipped, **{}** failed, **{}** not loaded, "
                u"**{}** unmapped instances".format(
                    st.get(report.PLACED, 0), "would be" if preview else "",
                    st.get(report.DUPLICATE, 0), st.get(report.FAILED, 0),
                    st.get(report.SKIPPED, 0), st.get(report.UNMAPPED, 0)))
if s["by_type"]:
    output.print_table(s["by_type"], columns=["Family : Type",
                                               "Would place" if preview else "Placed"])
if s["unmapped"]:
    output.print_md("### Unmapped blocks (add them to the mapping file to place them)")
    output.print_table(s["unmapped"], columns=["CAD block", "Instances"])
if s["problems"]:
    output.print_md("### Failed / skipped")
    output.print_table([[g[0], g[1], g[3], g[2]] for g in report.group_problems(s["problems"])],
                       columns=["Status", "CAD block", "Count", "Reason"])

# 6. CSV log next to the mapping file (Documents if that folder is read-only).
stamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
name = "cad2revit_{}_{}.csv".format("preview" if preview else "log", stamp)
rows = report.log_rows(results)
for folder in (os.path.dirname(opts.mapping_path), os.path.expanduser("~\\Documents")):
    try:
        log_path = os.path.join(folder, name)
        write_csv(log_path, report.LOG_HEADER, rows)
        output.print_md(u"Log saved: `{}`".format(log_path))
        break
    except Exception:
        continue
if not preview and st.get(report.PLACED, 0):
    output.print_md("Undo the whole run with a single **Ctrl+Z** "
                    "(\"CAD2Revit: Place families\").")

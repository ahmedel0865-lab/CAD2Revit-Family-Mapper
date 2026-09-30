# Changelog

## [0.19.1] - 2026-09-30
### Fixed: offset of families on vertical planes
- Families on a **vertical plane** (Host Type *Vertical plane*, or *Wall* with no wall found) could land shifted from the CAD block insertion point, depending on the direction the plane faced.
- **Exact point.** The CAD insertion point is projected perpendicularly onto the plane, at the row's elevation, and the family is placed there.
  - A shared plane from an earlier block is reused only if it faces the same way (within 0.01°) and passes within 0.5 mm of the point. Otherwise a new plane is made. Before, planes were shared by a rounded key, and a slightly turned plane moved the family along the wall.
- **Orientation.** The reference direction is horizontal along the plane (Z × normal), and the facing comes from the CAD block rotation (+Y of the block).
  - New planes get their end points in the order that makes the normal point the right way. There is no `Flip()`, whose effect can't be trusted before a regeneration.
  - If the placed family faces the back of the plane, its work plane is flipped back.
- **Family origin.** With `CenterFamiliesOnCadPoint = true` (new setting, default), a family whose origin is more than 10 mm from its geometric centre along the plane is shifted so its centre sits on the CAD point. The log notes the offset.
- **Check after placing.** The family's position is measured against the CAD point.
  - More than **10 mm** off: it is moved back onto the point, and a **WARNING** is shown in the result window ("family landed N mm from the CAD point - moved back onto it").
  - The position is measured again after the move.
- The timing table has a new phase, *Position check + snap (vertical planes)*. The check regenerates the model for each vertical-plane block, which makes these rows a little slower.
- New Core class `VerticalPlacement`, with tests for planes facing north, south, east, west and at angles.

## [0.19.0] - 2026-09-29
### Changed: the mapping window starts with no families picked, and a warning before placing twice
- **Revit Family starts at (Skip) every time.** Opening Place Families no longer fills in the families from the last run, and no longer auto-selects families by name. Every row starts at `(Skip)`, so nothing is placed that you did not pick this time.
- **The other columns are still remembered** per project: Elevation From Level, Host Type, Rotation, Facing, Level and Category, plus the Slab (above) / Ceiling *Search range* and *Fallback reference plane height*. You only re-pick the families.
- **Families come back only when you ask**: **Load...** fills the grid from a mapping file (families included), and **Auto-match** still pre-selects by name when you click it. Preview keeps your picks when you return to the mapping window.
- **Clear All Families** button (next to Load / Save / Auto-match): sets every row back to `(Skip)` and leaves the other columns as they are.
- **Warning before Run when elements are already there.** Before a Run, the tool checks every block location for an element that is already in the model: an instance of the same family (the existing duplicate check, same tolerance and level band), or one whose Comments say `CAD: <block>` (placed by CAD2Revit, even with another family). If any are found, it asks: *"X elements already exist at these locations."* **Skip them** (default) / **Place anyway** / **Cancel** (back to the mapping window). Skipped blocks are listed as duplicates in the result window and the log.
- The duplicate check during a run also counts elements whose Comments say `CAD: <block>` for the same block.

## [0.18.0] - 2026-09-29
### Changed: Ceiling uses the same search and fallback as Slab (above)
- **One shared search.** Slab (above) and Ceiling now use the same code (`HostFinder.FindUnderside`, with the host type choosing the categories): the nearest bottom face straight above the block, from the level up to the search range, in this model and in links. Ceiling searches Ceilings; Slab (above) searches Floors, Roofs and beams. Hosts are still collected once into the face index, with no ray per block.
- **Ceiling found**: the face-based family is hosted on the ceiling's bottom face, facing down, with the CAD rotation. Level-based families are placed level-based at the ceiling's underside height (before: failed).
- **No ceiling in range**: same fallback as Slab (above). The family goes on one reference plane per level, `CAD2Revit_<Level>_+3000mm`, facing down, or level-based at that height. It is logged as `No ceiling within 5000 mm - placed on reference plane at +3000 mm` and its Comments get `CAD2Revit: Host = Reference Plane`. Before, it was placed unhosted at the row's elevation, and the search was capped at the next level or `HostSearchDistanceMm`.
- **Needs Review** lists Ceiling fallbacks too (reason `No ceiling within 5000 mm`), with the same select/zoom, Copy IDs and Export to Excel.
- **Counts** in Preview and the result window: `Ceiling: N hosted on ceiling, N on reference plane, N level-based`, next to the Slab (above) line.
- The mapping window's two values are now labelled **Slab (above) and Ceiling: Search range (mm) / Fallback reference plane height (mm)**. They are saved in the same `Slab_Search_Range_mm` / `Slab_Fallback_Plane_mm` columns, so existing mapping files keep working.
- Duplicate check: re-runs now also see instances placed above the next level (up to the search range / fallback plane) as duplicates.

## [0.17.0] - 2026-09-29
### Changed: Slab (above) hosts on slabs or beams, and a Needs Review list
- **Nearest slab or beam.** Slab (above) now looks for Floors, Roofs **and Structural Framing (beams)**, in this model and in links, within the search range above the level. The nearest bottom face straight above the block wins, so a drop beam under the slab is chosen over the slab. A beam is only used where the point is under its bottom face; otherwise the next host up is used. Hosts are still collected once into the face index, with no ray per block.
- **Hosting** on the found slab/beam: bottom face, facing down, CAD rotation (unchanged).
- **Fallback** (no slab or beam in range): unchanged, one reference plane per level at the fallback height (`CAD2Revit_<Level>_+3000mm`), or level-based at that height. The log text is now `No slab/beam within 5000 mm - placed on reference plane at +3000 mm`.
- **Comments.** Every fallback element's Comments is set to `CAD2Revit: Host = Reference Plane` (followed by ` | CAD: <block>` when writing block names is on), so it can be found with a filter or schedule.
- **Needs Review window** after a run, when there are fallback elements: Element ID, Family : Type, CAD Block, X, Y (mm), Reason. Clicking a row selects and zooms to the element in Revit. Buttons: **Select All in Revit**, **Copy IDs** (comma-separated, for Manage > Select by ID), **Export to Excel** (.xlsx or .csv) and Close.
- **Counts** in Preview and in the result window: `Slab (above): N hosted on slab, N hosted on beam, N on reference plane, N level-based`.

## [0.16.0] - 2026-09-29
### Changed: Slab (above) search range and fallback plane
- **Search range.** Slab (above) looks for a slab from the row's level up to the **Slab search range** (default 5000 mm), and ignores anything higher, even when the level above is higher. It replaces "next level + `SlabSearchToleranceMm`", and the 0.15.3 extra search of sloped faces up to `HostSearchDistanceMm`. Floors and roofs, in this model and in links, are still collected once and looked up in memory: there is no ray per block.
- **Slab found**: the face-based family is hosted on the slab's bottom face, facing down, with the CAD rotation (unchanged).
- **No slab in range**: the family is hosted on one reference plane per level, `CAD2Revit_<Level>_+3000mm`, facing down, at the **Fallback reference plane height** (default 3000 mm). The plane is reused by every block that falls back and by later runs. Before, the plane height came from the biggest slab above or the level above.
- **Level-based families**: with no slab, they are placed level-based with Elevation From Level = the fallback height (still batch-created). Under a slab, they are placed level-based at the slab underside height; before, they failed. Legacy ceiling-hosted families with no slab still fail.
- **Log and counts**: every fallback block is logged as `No slab within 5000 mm - placed on reference plane at +3000 mm` (or `placed level-based at +3000 mm`). Preview and the result window show the number of fallback blocks, and the full report lists it too.
- **Mapping window**: *Slab search range (mm)* and *Fallback reference plane height (mm)* are at the top of the window. They are saved with the mapping (the project's remembered mapping and Save...) as the `Slab_Search_Range_mm` and `Slab_Fallback_Plane_mm` columns, and restored by Load.... Invalid values turn red and block Preview/Run.
- `SlabSearchToleranceMm` in settings.ini now only affects Slab (below).

## [0.15.3] - 2026-09-29
### Fixed: Slab (above) did not follow sloped slabs
- **Roofs count as slabs.** Sloped slabs are often modelled as Roofs, which Slab (above) and Slab (below) ignored. The tool found no slab, and the family ended up flat on a reference plane below the slope. Roofs are now searched along with Floors, in this model and in links.
- **High end of a sloped slab.** The search stopped at the next level + `SlabSearchToleranceMm`. Where a sloped underside rises above that, it was missed and the family was placed flat. Sloped faces (more than about 3 degrees) are now also searched up to `HostSearchDistanceMm`. A flat slab two floors up is still never used.

## [0.15.2] - 2026-09-28
### Changed: simpler results window after Place Families
- **Headline**: "Placed X of Y families" ("Would place ..." in Preview), green when every block was placed, amber otherwise.
- **What was placed**: one line per family with the number placed.
- **Warnings**, shown only when there are any: one plain line per block and problem, worst first, e.g. `LIGHT (x2): no ceiling found above, placed on level`. Element ids, slopes, distances and DEBUG text are left out.
- **Show details** reveals the previous full report (tables, timings, log path) with Copy, Open log and Log folder links. There is one **Close** button.
- The window stays attached to Revit and cannot be minimized.

## [0.15.1] - 2026-09-28
### Fixed: Revit looks frozen after a CAD2Revit window is closed or hidden
- Every CAD2Revit dialog (DWG picker, mapping window, results, lists) is now **owned by Revit's main window**, so it always stays on top of Revit. Before, the DWG picker, the result window and the list pickers had no owner and could drop behind Revit. Revit stays locked while a dialog is open, so it looked frozen and the dialog could not be found.
- The mapping window and the result window can no longer be **minimized**. They have no taskbar button, so a minimized window could not be brought back while Revit stayed locked.
- **Ribbon tab**: start-up never fails. If a *Mapper* or *Tools* panel already exists on the CAD2Revit tab (a second copy of the add-in, or the old pyRevit extension), it is reused or a separate panel is added, instead of the add-in failing to load and the buttons disappearing.

## [0.15.0] - 2026-09-28
### Removed
- The **List Blocks** button and command. The mapping window (Place Families) already lists every block with its count, and **Save** writes the same mapping file for Excel. The ribbon now has **Place Families**, **Settings** and **Help**.

## [0.14.0] - 2026-09-28
### Fixed: face hosting on slabs and ceilings in Revit links
- Families could end up hosted on a **Reference Plane** instead of the slab in a linked model (*Properties > Host* showed Reference Plane).
- **Stable link references.**
  - When the ray-cast fallback hits a face in a link, the tool now takes the linked element (`LinkedElementId` in the link's document) and walks its own geometry, with `ComputeReferences` on.
  - It picks the face that contains the hit point, converted into link coordinates with the inverse link transform, and that faces the ray: the **soffit** (normal down) for Slab (above), Ceiling and Face, or the top face for Slab (below).
  - It hosts on `face.Reference.CreateLinkReference(linkInstance)`, with the hit point in host coordinates. The CAD rotation is projected onto the face plane, using the normal transformed by the link transform.
  - The face index already used references built this way.
- **The temporary 3D view shows the links**: the Revit Links, Floors, Ceilings, Roofs, Structural Framing and Walls categories are visible, hidden link instances are unhidden, and all worksets are visible. It still has no section box or view template.
- **Verify after placing.** Each face-hosted instance is checked:
  - *Host* must be the `RevitLinkInstance` for a linked face, or the element for a face in this model, and *Host Face* must be set.
  - If Revit hosted it on a reference plane, a level or nothing, the placement is retried once by ray with a stable reference.
  - If it still isn't on the face, the block is **failed** (`failed - not hosted on linked slab: Host is Reference Plane`) and rolled back, never reported as placed.
- **Family must be face-based**: `ceiling`, `face`, `slab above` and `slab below` rows with a level-based family (`OneLevelBased`) are now **failed** with "family is not face-based". Before, they were placed level-based with a warning. `wall` rows are unchanged.
- **`DebugHosting`** setting (`settings.ini`, default false). Adds a `DEBUG` line per block to the log: linked yes/no, link name, host element id and category, face normal, and final Host.

## [0.13.0] - 2026-09-28
### Changed: one tool
- The repository now contains **only the standalone Revit add-in**. The pyRevit extension (`CAD2Revit.extension/`) and its Python tests are removed.
  - Everything the pyRevit version did is in the add-in, including face hosting on **sloped** ceilings and slabs (PR #3 was the pyRevit fix for this, now superseded).
  - The add-in already hosts on the exact point of the face with the CAD rotation projected into the face plane, for ceilings, slabs, roofs and beams, in linked models too.
  - If you had installed the pyRevit version, remove it (`pyrevit extend remove CAD2Revit`) so only one CAD2Revit tab remains.
- CI builds and tests only the add-in. Build output (`bin/`, `obj/`, `dist/`) is git-ignored.

## [0.12.0] - 2026-09-27
### Performance
- **Timings**: every Preview/Run measures each phase with a Stopwatch: reading the DWG, loading the mapping, host detection (index build, per block, ray fallback), duplicate check, family creation (single and batched), rotation, planes, parameter setting, and commit/rollback.
  - The result window shows a **Timings** table (time, calls, share of the total).
  - The CSV log gets the same rows with Status `timing`.
- **Host detection without one ray per block.**
  - Slabs, ceilings and Face hosts are collected **once**, from this model and from links. Their horizontal planar faces (with openings) go into a plan index.
  - Each block point is looked up there: a bounding-box prefilter, then a point-in-face test. Results are cached.
  - **Walls**: candidate walls come from an index of their location lines. The point is then projected onto the side faces of those 1–2 walls. Before, each block cast 16 rays.
  - Ray casting (`ReferenceIntersector`, one instance per host type) remains only as a fallback: where faces could not be indexed (curved faces, curtain walls), or when a face from the index refuses to host.
- **Batched creation**: level-based rows are created with **`NewFamilyInstances2`**, one call per mapping row and level, with the rotation in the `FamilyInstanceCreationData`. If a batch fails, it falls back to one-by-one creation.
- **No `Regenerate()` in the loop.**
  - All needed family types are activated once, with one regeneration, before the loop.
  - Reference-plane rows no longer regenerate per block to check their facing.
- **Duplicate check**: one spatial hash grid (cell = tolerance) of the existing instances, built **once per run**. Before, it was rebuilt for every level used.
- **Parameters** (Comments, schedule level, elevation) are set in one pass after creation.
- **DWG** geometry is read at Coarse detail level. Nested blocks are only walked when the option is on.
- **Warnings** (e.g. *identical instances in the same place*) are deleted in the failures preprocessor, and failure dialogs are never forced. The commit doesn't stop for them.
- **Progress bar with Cancel** while placing. Cancel rolls the whole run back and returns to the mapping window. Revit's window is disabled while it runs.
### Unchanged
- Still one transaction per run (one Ctrl+Z). Non-level-based blocks keep their own sub-transaction, so one bad block doesn't stop the rest.

## [0.11.0] - 2026-09-27
### Added
- **Host Type "Slab (above)"**, for ceiling devices hosted on the underside of the structural slab.
  - A ray is cast **straight up** from the row's level at each block, in a clean temporary 3D view, using `ReferenceIntersector` with face targets.
  - Only **Floor** elements count, both structural and architectural slabs. Beams, ceilings, ducts and anything else are ignored.
  - Slabs in **linked Revit models** are found too, e.g. a structural link.
  - The **first slab underside above the level** is used, which is the slab of the level above. For example, target *Second Floor* hosts on the underside of the *Third Floor* slab. Top faces, such as a finish slab sitting just above the level, are skipped.
  - The face-based family is hosted on that **bottom face, facing down**, with the CAD block rotation as its direction.
  - The search stops at the **level-to-level height + 500 mm**, so it never reaches a slab two floors up. The 500 mm is `SlabSearchToleranceMm` in `settings.ini`.
- **Host Type "Slab (below)"**, for floor devices (floor boxes, floor sockets).
  - A ray is cast **down** from 300 mm above the level, and the family is hosted on the **top face** of the slab at that level, **facing up**.
- **Slab fallbacks**, each logged as a warning:
  - **No slab at a block** (a slab opening, or no slab there): the family is hosted on a reference plane at the **underside of that level's slab** (or the top, for Slab (below)). That slab is found from the floors' extents in the model and its links. If the level has no slab, the plane goes at the level above's elevation (or at the level itself for Slab (below)). The plane is created or reused as `CAD2Revit_<Level>_+<elev>mm`.
  - **Family not face-based / work-plane-based**: placed level-based at *Elevation From Level*.
- **Detected Host (Preview)** column in the mapping window.
  - After **Preview**, each row shows the host it would use, e.g. `Floor: 250mm RC Slab - Third Floor (linked: STR.rvt)`.
  - Mixed results are counted, e.g. `... (38) · Reference plane CAD2Revit_Second Floor_+3250mm (4) · 1 failed`.
  - The value is cleared when you change that row's family, level or host type.
- The log's **Host** column uses the same detailed names for all host types: category, type, level and link.
### Unchanged
- Still **one transaction** per run with a sub-transaction per block. Reference planes created by a fallback are undone by the same Ctrl+Z.

## [0.10.0] - 2026-09-26
### Added
- **2D symbol preview** in the mapping window.
  - A **Symbol preview** panel on the right shows the CAD block's line work for the row under the mouse, or the selected row when the mouse leaves the grid.
  - It also shows the block name, instance count, category, size (e.g. `600 x 600 mm`) and the chosen family.
  - Hovering the block name shows the symbol in a tooltip too.
  - The line work (lines, arcs, polylines, curves and nested blocks) is read from each block definition once, when the DWG is read. Very large blocks are simplified.
  - Text, hatches and solids are not part of the DWG geometry that Revit exposes, so they don't appear in the preview.

## [0.9.0] - 2026-09-26
### Changed: a tidier, more organized tool
- **Mapping window redesigned.**
  - A header card with the DWG and live counts: mapped, instances to place, skipped, invalid.
  - Controls grouped into two titled sections, **1 · Filter** and **2 · Edit selected rows**. The *Reference planes for all rows* switch moved into the edit section.
  - Styled column headers, striped rows, and **skipped rows greyed out** so the mapped ones stand out.
  - Footer: mapping file (Load / Save / Auto-match) on the left, **Preview** and a highlighted **Run** on the right.
  - Tooltips on the buttons.
- **Step-1 dialog and result window** get the same blue title header.
- **Ribbon:** a new **Tools** panel.
  - **Settings** opens `settings.ini` in Notepad.
  - **Help** shows the version, a short how-to, and links to the user guide, the logs folder and the saved project mappings.
  - New matching icons.
- **README reorganized:** badges, a workflow diagram, contents, a ribbon table, and features grouped into *Mapping window / Placement and hosting / Safety and output*.

## [0.8.1] - 2026-09-26
### Fixed
- Reference Plane rows in a model with **no section/elevation view**: the temporary 3D view used to draw the planes was created inside the first block's sub-transaction. If that block failed, the view was rolled back with it, later blocks used a deleted view, and the final clean-up threw, **rolling back the whole run**. The view is now created up front, and validity is checked before it is reused or deleted.

## [0.8.0] - 2026-09-26
### Added
- **Edit many rows at once.** Select rows in the mapping window (Ctrl+click, Shift+click, Ctrl+A or *Select all shown*), set **Host Type**, **Level**, **Elevation**, **Facing** and/or **Category** in the new bar above the grid, then click **Apply to selected rows**. Fields left at *(keep)* are not changed. Combined with *Show* (category), e.g. all Electrical blocks can be given the same host in one step.

### Changed
- **No target level in the first window.** Step 1 now asks only for the DWG and the nested-blocks option. Every row's Level starts at the level the DWG is linked on (view-only links: its view's level; otherwise the active plan's level) and is set per row, or for many rows with the new bulk edit.

## [0.7.0] - 2026-09-26
### Added
- **Level column** next to *Elevation From Level (mm)* in the mapping window. Each block can be placed on its own level in one run; it defaults to the level picked in step 1.
  - Hosting, reference planes (`CAD2Revit_<Level>_+<elev>mm`), vertical planes, the ceiling search limit and the duplicate check all work per level.
  - The log has a new **Level** column.
  - Mapping files have an optional **Level** column, written right before the offset.
- **Block categories.** Every block is classified as Electrical, Mechanical, Plumbing, Architectural, Structural, Annotation or Other.
  - The classification comes from the block name (tested on real Revit-exported names such as `MAAP_Ceiling Mounted Luminaire - F1`, `Casework 16`, `Elevator`, `Grid Head`). When the name gives no clue, the matched family's Revit category is used.
  - The mapping window **groups rows by category** (Electrical first), has a **Show** filter and a **Skip shown rows** button, and the category can be edited per row. It is saved with the mapping (optional **Category** column).
  - List Blocks shows and exports the category.

### Fixed
- The List Blocks template now writes every column of the mapping format (the Facing column was missing).

## [0.6.0] - 2026-09-25
### Added
- **Host Type "Reference Plane (auto-create)"** (standalone add-in).
  - Creates a **horizontal reference plane** at level + *Elevation From Level*, named `CAD2Revit_<Level>_+<elevation>mm`, spanning the DWG link extents plus 1 m.
  - Hosts the family on it with `NewFamilyInstance(reference, point, CAD direction, symbol)`.
  - Existing planes with the same name are reused, so rows with the same elevation share one plane.
- **Facing** column (Down / Up, default Down) sets which side the family faces. The plane normal is set to match; up-facing planes are named `..._Up`. After placement the facing is checked and the work plane flipped if needed.
- Families that are not face-/work-plane-based are placed level-based with a **WARNING** in the log. The result window gains a *Placed with warnings* section.
- **"Use reference planes for all rows"** checkbox; unticking restores the previous Host Types.
- Host Type dropdown uses readable labels: None (level-based), Ceiling, Wall, Reference Plane (auto-create), Face, Vertical plane. Mapping files accept both labels and short names, plus an optional **Facing** column.
- README: *Ceiling vs Reference Plane* guidance.

### Fixed
- Vertical planes (v0.5.0) and the new reference planes are recreated if the block that first created them was rolled back, instead of reusing a deleted element.

## [0.5.0] - 2026-09-25
### Added
- **Vertical placement for wall devices** (standalone add-in).
  - New Host Type **`vertical`**: face-based families stand upright on a vertical work plane through the CAD point, at the elevation, facing the block's local +Y (turned by Rotation). No Revit wall is needed, which suits MEP models that only have the DWG background.
  - Planes are named `CAD2Revit vertical <id>`. Devices on the same wall line share one plane, and later runs reuse them.
### Changed
- Host Type **`wall`**: when no wall is found, face-based devices are now placed on a vertical plane instead of lying flat on the level.

## [0.4.1] - 2026-09-25
### Fixed
- **Mapping window now groups instances by block name.** Revit reports block names as `<file>.dwg.<block>`, and DWGs exported from Revit name every block `<Family> - <Type>-<element id>-<view>`, so each instance appeared as its own `(1)` row. Names are now simplified before grouping: the file prefix is removed, and for Revit-exported DWGs the view suffix and element id are removed. For example, `EL101-...PLAN.dwg.MAAP_Ceiling Mounted Luminaire - F1-7107100-GROUND FLOOR LIGHTING PLAN` becomes `MAAP_Ceiling Mounted Luminaire - F1`, so all instances share one row, sorted by name, and it auto-matches the `MAAP_Ceiling Mounted Luminaire : F1` family. Normal AutoCAD names are left alone. Can be turned off with `SimplifyBlockNames = false` in settings.ini.
- The *Revit Family* column could be squeezed to a few pixels by long block names. It now has a fixed 400 px width; long block names end in "..." with the full name in a tooltip.

### Added
- **Find** box in the mapping window to filter rows by block or family name.

## [0.4.0] - 2026-09-25
### Added
- **Mapping window (WPF)**, opened by Place Families after picking the DWG and level. It shows one row per unique CAD block (`SMOKE-DET (42)`) with:
  - a **searchable** "Revit Family" dropdown (`Family : Type`, filtered to electrical categories, "(Skip)" by default);
  - **Elevation From Level (mm)**, validated as a number;
  - optional **Rotation** and **Host Type** columns.
- **Auto-select** of families whose names closely match the block name, including common CAD abbreviations (DET, SKT, SW, MCP, DB, 1G...). An *Auto-match* button re-runs it.
- **Per-project memory**: the grid is saved when you click Preview/Run and pre-fills the window next time (`%AppData%\CAD2Revit\projects\`).
- **Load Mapping / Save Mapping** (XLSX or CSV, same format as before).
- Preview returns to the mapping window with your choices kept.
- Unit tests for name matching, project keys, and saving mappings with Skip rows.

### Changed
- Place Families step 1 now only asks for the DWG, level and nested-blocks option; the mapping file is optional (Load Mapping).
- Blocks set to (Skip) are logged as `unmapped` ("not mapped (Skip)").
- Logs are written to `Documents\CAD2Revit\Logs\<project>\`.

## [0.3.0] - 2026-09-25
### Added
- **Standalone Revit add-in (C#)** in `addin/`, so pyRevit is no longer required. Builds for Revit 2022, 2023 and 2024 (.NET Framework 4.8) and Revit 2025 and 2026 (.NET 8), with its own CAD2Revit ribbon tab (List Blocks, Place Families).
- Same features as 0.2.0: XLSX/CSV mapping, ceiling/face/wall/non-hosted placement (incl. linked hosts), one-dialog workflow, real preview, per-level duplicate check, one-transaction undo, summary window and CSV log.
- Settings in `%AppData%\CAD2Revit\settings.ini` (created on first run; also remembers the last mapping file).
- `Install.bat` / `Uninstall.bat`: per-user install for every Revit version found, no admin rights; unblocks downloaded DLLs.
- `tools/package.sh` builds every version into one zip; GitHub Actions builds it on every push/PR and publishes it as a Release for `v*` tags.
- C# unit tests for CSV/XLSX, mapping, report and settings.

### Changed
- The pyRevit extension is kept as an optional alternative.

## [0.2.0] - 2026-09-25
### Added
- Excel (.xlsx) mapping files (built-in reader, no Excel needed); List Blocks exports .xlsx or .csv.
- Host types `ceiling`, `face` (ceilings/slabs/roofs/beams), `wall` and `non-hosted`, with hosts in linked Revit models.
- Wall hosting: nearest wall face at the row's offset height, facing into the room.
- Support for face-based, level-based and legacy wall/ceiling-hosted families.
- `FALLBACK_TO_UNHOSTED` option when no host is found.
- Single dialog (DWG, mapping file, level, nested blocks, Preview / Run); remembers the last mapping file.
- Preview now runs the real placement and rolls it back, so counts include host failures.
- Summary per family type, grouped failure reasons, and a preview log.
- Log columns: host, X/Y/Z (mm), rotation, block scale, mirrored.
- Revit 2022-2026 compatibility helper (ElementId.Value / IntegerValue).
- Flexible header names; semicolon-separated CSV.
- Unit tests (no Revit needed), docs/TESTING.md, docs/LIMITATIONS.md.

### Fixed
- Duplicate check now only looks at the target level band, so identical floors are no longer treated as duplicates. It uses a spatial index (fast on large models) and compares against the final hosted position.
- Face-based families without a host are placed on the level's work plane instead of failing.
- DWGs linked with "Current view only" now return their blocks.
- One failing block no longer leaves partial changes (per-block sub-transactions); Revit warnings no longer interrupt the run.
- The schedule level is set to the target level for hosted elements.

## [0.1.0] - 2026-09-25
### Added
- "List Blocks" button: lists block names in a DWG link and exports a mapping template.
- "Place Families" button: places Revit families at CAD block locations using a CSV mapping.
- Preview mode, duplicate check, face-based (ceiling) hosting, CSV log output.

# Changelog

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

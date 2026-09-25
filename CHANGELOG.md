# Changelog

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

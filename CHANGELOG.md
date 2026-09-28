# Changelog

## [0.2.0] - 2026-09-28
### Added
- Manual CAD selection: "Pick in view" lets you click the DWG in the active view (only CAD links/imports are selectable).
- If one CAD is already selected when a button is clicked, it is used directly.
### Changed
- The DWG selection dialog now always offers "Pick in view" or "Choose from list", even when the project has only one CAD.

## [0.1.0] - 2026-09-25
### Added
- "List Blocks" button: lists block names in a DWG link and exports a mapping template.
- "Place Families" button: places Revit families at CAD block locations using a CSV mapping.
- Preview mode, duplicate check, face-based (ceiling) hosting, CSV log output.

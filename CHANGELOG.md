# Changelog

## [0.2.0] - 2026-09-28
### Added
- **Host_Type** values `ceiling`, `floor` and `face`. `face` means the nearest ceiling or slab underside above; the categories come from `HOST_CATEGORIES` in `config.py`.
- **Floors / slabs** are now searched as hosts, not only ceilings (lights or detectors under a concrete slab with no ceiling). This includes faces in linked models.
- The log message shows the **host found and the slope**, e.g. `host: linked ceiling, slope 12.5 deg`, or `host: none`.
### Fixed
- **Sloped ceilings and slabs**:
  - The reference direction is now the CAD rotation projected into the face plane. It used to be a horizontal vector, which failed or mis-oriented families on slopes.
  - It falls back to `n x Z` when the rotation is parallel to the normal.
  - The face normal is evaluated at the hit point. Linked faces are transformed by the link's position.
- The family is placed at the **exact hit point** on the face.
- The upward ray skips top faces, so it hosts on the underside of the slab **above**, never on the slab the level sits on.
- **Level-based families** under a ceiling/slab take their elevation from the face height at that point, and the log says so. They no longer use a flat CSV offset under a sloped ceiling.
### Unchanged
- One transaction per run, the duplicate check (in plan, XY), and IronPython compatibility.

## [0.1.0] - 2026-09-25
### Added
- "List Blocks" button: lists block names in a DWG link and exports a mapping template.
- "Place Families" button: places Revit families at CAD block locations using a CSV mapping.
- Preview mode, duplicate check, face-based (ceiling) hosting, CSV log output.

# Testing on a small sample drawing

The first time you use the tool on a project, and whenever you change Revit version, test on a small sample before a full floor. It takes about 15 minutes.

## 1. Make a test DWG (AutoCAD)

In a new drawing (units: millimetres):

1. Create 4 blocks. Draw each one so its insertion point is at the device centre and it "faces" +X:
   - `T-LIGHT`: 600x600 square
   - `T-SMOKE`: circle Ø150
   - `T-SOCKET`: 80x40 rectangle, drawn so the wall side is at the insertion point
   - `T-PANEL`: 600x200 rectangle
2. Draw a 10 x 8 m room outline (just lines, as a reference).
3. Insert:
   - 4 × `T-LIGHT` in a grid, one of them rotated 45°
   - 2 × `T-SMOKE`, one of them **mirrored**
   - 3 × `T-SOCKET` with their insertion point on the inside face of the room outline, rotated 0°, 90° and 180°
   - 1 × `T-PANEL` rotated 90°
   - 1 × any other block you don't map (e.g. `T-TEXT`)
4. Save as `cad2revit_test.dwg`.

## 2. Make a test Revit model

1. Start from your normal electrical template. Use two levels (Level 1 at 0, Level 2 at 4000).
2. Draw the room walls on Level 1 along the room outline (link the DWG first to trace it), and add a ceiling at 2800 mm.
   To test linked hosts, put the walls and ceiling in a separate architectural model and link that instead.
3. Load 4 families: a **face-based** light fixture, a **face-based** smoke detector, a **face-based** socket, and a level-based panel.
4. Link `cad2revit_test.dwg` in the Level 1 floor plan. For the first test use *Origin to Origin*, then repeat with the link **moved and rotated** (e.g. rotate the link 30°).

## 3. Mapping

In the mapping window (Place Families > Next), set the rows as below (or save this table as an .xlsx and use **Load Mapping...**):

| CAD_Block_Name | Revit_Family_Name | Revit_Type_Name | Offset_From_Level_mm | Rotation_Adjustment_deg | Host_Type |
|---|---|---|---|---|---|
| T-LIGHT | *your light family* | *type* | 2800 | 0 | ceiling |
| T-SMOKE | *your detector family* | *type* | 2800 | 0 | ceiling |
| T-SOCKET | *your socket family* | *type* | 300 | 0 | wall |
| T-PANEL | *your panel family* | *type* | 1500 | 0 | non-hosted |

## 4. Checklist

Run **List Blocks** first, then **Place Families** > pick the DWG > mapping window > **Preview**, then **Run**.

| # | Check | Expected |
|---|---|---|
| 1 | List Blocks | 5 names; T-SMOKE shows 1 mirrored |
| 2 | Mapping window | 5 rows (one per block name) with counts, e.g. `T-LIGHT (4)`; `T-TEXT` stays (Skip). Typing part of a family name filters the dropdown; a non-numeric elevation turns red and blocks Preview |
| 2b | Preview | 10 would be placed, 1 unmapped (`T-TEXT`), nothing changed in the model; closing the result returns to the mapping window |
| 3 | Run: position | Each family's origin sits on its CAD insertion point in plan (zoom in, turn on the DWG) |
| 4 | Run: rotation | The 45° light and the 90° panel match the CAD symbols. If a family is 90°/180° off, fix it with `Rotation_Adjustment_deg`, not in the family |
| 5 | Ceiling hosting | Lights and detectors report host `Ceilings`, and their elevation = ceiling height |
| 6 | Wall hosting | Sockets sit on the wall face at 300 mm, facing into the room, host `Walls` |
| 7 | Mirrored | The mirrored detector has "CAD block is mirrored" in the log |
| 8 | Log | `cad2revit_log_*.csv` has 11 rows with ElementIds for the 10 placed elements |
| 9 | Duplicate check + memory | Run again: the grid is pre-filled with your last mapping; 0 placed, 10 duplicates |
| 10 | Undo | Ctrl+Z once removes everything the tool placed in that run |
| 11 | Rotated link | Rotate/move the DWG link, delete the placed families, run again: everything follows the link |
| 12 | No host | Delete the ceiling and run: lights are placed at 2800 mm unhosted, with "no ceiling found... placed unhosted" in the log |
| 12b | Reference Plane | Set the lights to *Reference Plane (auto-create)* at 2800: one plane `CAD2Revit_Level 1_+2800mm` is created, all lights host on it facing down; run again and no second plane is created; Ctrl+Z removes the plane too |
| 13 | Other level | Select all rows, set Level = Level 2 with *Apply to selected rows*, run: 10 placed on Level 2 (the Level 1 elements are not treated as duplicates) |

If everything passes, run it on one real floor, check a few devices of each type, and only then do the whole building.

## 5. Automated tests (developers)

These run without Revit:

```
# Revit add-in (C#): CSV/XLSX, mapping, report, settings
cd addin && dotnet test tests/CAD2Revit.Core.Tests

# pyRevit version (Python), including placement control flow against a fake Revit API
python -m unittest discover -s tests -v
```

CI (`.github/workflows/build.yml`) runs both, builds the add-in for Revit 2022–2026, and uploads the installable zip.

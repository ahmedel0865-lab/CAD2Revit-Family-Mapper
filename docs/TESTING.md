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

Run **Place Families** > pick the DWG > mapping window > **Preview**, then **Run**.

| # | Check | Expected |
|---|---|---|
| 2 | Mapping window | 5 rows (one per block name) with counts, e.g. `T-LIGHT (4)`; `T-TEXT` stays (Skip). Typing part of a family name filters the dropdown; a non-numeric elevation turns red and blocks Preview |
| 2b | Preview | 10 would be placed, 1 unmapped (`T-TEXT`), nothing changed in the model; closing the result returns to the mapping window |
| 3 | Run: position | Each family's origin sits on its CAD insertion point in plan (zoom in, turn on the DWG) |
| 4 | Run: rotation | The 45° light and the 90° panel match the CAD symbols. If a family is 90°/180° off, fix it with `Rotation_Adjustment_deg`, not in the family |
| 5 | Ceiling hosting | Lights and detectors report host `Ceilings`, and their elevation = ceiling height |
| 6 | Wall hosting | Sockets sit on the wall face at 300 mm, facing into the room, host `Walls` |
| 6b | Wall: block on the right face | Straight wall running north–south. Put a socket block 150 mm to the **right** (east) of the wall, rotated 37° in CAD. The family goes on the **east** face, at the CAD point projected onto it, flat on the wall (rotation ignored), facing east. Put another block on the west side: it goes on the west face, facing west |
| 6c | Wall: block inside the wall | Insertion point inside the wall thickness, symbol drawn on the west side: the family goes on the west face (log: "CAD point is inside the wall - side taken from the block symbol") |
| 6d | Wall: curved wall | Block outside a curved wall: on the outer face, flat on the curve (tangent at that point), facing out. Block inside the curve: on the inner face |
| 6e | Wall: Needs Review | A block 800 mm from any wall: vertical-plane fallback, Needs Review "No wall within 500 mm". A block 350 mm from the face (Wall search distance 500): placed, Needs Review "Moved more than 200 mm to reach the wall face". A block at a wall in a linked model: placed on the link, Needs Review "Wall is in a linked model" |
| 7 | Mirrored | The mirrored detector has "CAD block is mirrored" in the log |
| 8 | Log | `cad2revit_log_*.csv` has 11 rows with ElementIds for the 10 placed elements |
| 9 | Duplicate check + memory | Run again: the grid is pre-filled with your last mapping; 0 placed, 10 duplicates |
| 10 | Undo | Ctrl+Z once removes everything the tool placed in that run |
| 11 | Rotated link | Rotate/move the DWG link, delete the placed families, run again: everything follows the link |
| 12 | No host | Delete the ceiling and run: lights are placed at 2800 mm unhosted, with "no ceiling found... placed unhosted" in the log |
| 12b | Reference Plane | Set the lights to *Reference Plane (auto-create)* at 2800: one plane `CAD2Revit_Level 1_+2800mm` is created, all lights host on it facing down; run again and no second plane is created; Ctrl+Z removes the plane too |
| 12c | Slab (above) | Add a floor slab at Level 2 (4000) (or link a structural model with one), set the lights to *Slab (above)* on Level 1, Preview: *Detected Host* shows `Floor: <type> - Level 2` (with `(linked: <file>.rvt)` for a link). Run: lights sit on the slab underside, facing down. Put a shaft opening in the slab over one light: that light goes on a reference plane at the slab underside with a warning |
| 12d | Slab (below) | Floor slab at Level 1, a floor box row set to *Slab (below)*: hosted on the slab top, facing up |
| 12e | Progress + Cancel | On a large DWG, click Run, then Cancel in the progress window: "Cancelled... nothing was changed", nothing added to the model, back in the mapping window |
| 12f | Timings | The result window ends with a Timings table, and the CSV log has `timing` rows |
| 12g | Linked flat slab | Structural model with a flat slab at Level 2, linked. Face-based light, Host *Slab (above)* (and again with *Face*), on Level 1, `DebugHosting = true`. Run: select a light, *Properties > Host* = the link (`STR.rvt`), not *Reference Plane*. Light sits on the soffit, facing down. Log: `placed`, `DEBUG linked=yes link=STR.rvt element=<id> (Floors) normal=(0.000,0.000,-1.000); host=Revit link STR.rvt, host face ok` |
| 12h | Linked sloped slab | Slope the linked slab (slope arrow, or a sloped roof-like slab) and run again: Host = the link, the light follows the slope, and the DEBUG normal is tilted (Z between -1 and -0.5) |
| 12i | Linked ceiling | Architectural model with a ceiling, linked; Host *Ceiling*. Host = the link, DEBUG category `Ceilings` |
| 12j | Slab in host model | Same slab modelled in this model (no link); Host *Slab (above)*. Host = the Floor, DEBUG `linked=no ... (Floors)`, `host=element of this model (Floors <id>), host face ok` |
| 12k | No slab above | A light outside the slab outline (or under a shaft opening). *Slab (above)*: placed on a reference plane with a WARNING, DEBUG `no host face found; host=Reference Plane ...`. *Face* with `FallbackToUnhosted = false`: `failed`, "no face found" |
| 12l | Not face-based | Map a level-based (non face-based) family to *Slab (above)*: `failed`, "family is not face-based (placement type OneLevelBased)" and nothing placed |
| 12m | Vertical plane, 4 directions + angle | Blocks rotated 0° (north), 90° (west), 180° (south), 270° (east) and 30°, Host *Vertical plane*, elevation 1200. Each family sits on its CAD insertion point (plan: centred on the block; elevation 1200), facing the block's +Y. No "landed N mm from the CAD point" warnings. Measure a few: within 10 mm |
| 12n | Off-centre family origin | A face-based family whose origin is at one edge, Host *Vertical plane*: its origin sits on the block (default). With `CenterGeometryOnCadPoint = true` its geometry is centred on the block instead |
| 12o | Vertical planes: one per line | Two sockets 2 m apart across a room, same facing: each gets its own plane `CAD2Revit_V_Level 1_1`, `_2` through its own point, and each family sits on its block. A third socket further along the first wall line reuses `_1` |
| 12p | Final distance check | Set *Review if farther than* = 50. Elements on their intended point (CAD block, wall face, snapped face point) are not listed. An element that ends up more than 50 mm from it (e.g. a family with an odd origin that could not be moved) is listed with its Element ID |
| 12q | Block drawn away from its base point | New rows show *Place At = Symbol centre* by default. A wall-light block whose base point is on the wall line and whose circle is 200 mm into the room, Host *Vertical plane*. With *Place At = Base point*: the family is on the wall line, and Needs Review says "Block base point is 200 mm from its symbol". With *Place At = Symbol centre*: the family sits on the circle |
| 12r | Mirrored block | Mirror one wall-light block in AutoCAD. Host *Vertical plane*: the family faces the same side as the mirrored symbol |
| 12s | Vertical plane parallel to the wall | Revit wall running at 30°, a socket block 150 mm in front of it drawn at 0°, Host *Vertical plane*. The plane (`CAD2Revit_V_...`) is parallel to the wall, and the socket faces away from the wall. With *Snap to face* it sits on the wall face. With *Through block point* it stays at the block point |
| 12t | Column | Block 200 mm from a structural column's face: the plane is parallel to that face |
| 12u | DWG walls only | No Revit walls; the DWG has walls drawn as two lines 200 mm apart (any layer name). The plane follows the wall face on the block's side, and the Host column says "CAD wall pair (thk 200 mm)". A block 700 mm from any wall: Needs Review "Wall/column not detected" |
| 12v | Same face, one plane | Three sockets along the same wall face: one shared plane. A block 300 mm from the face with *Snap to face*: Needs Review "Moved 300 mm to snap to the wall/column face" |
| 12w | DWG columns | A 400x400 column drawn as a closed polyline, one drawn as four separate lines, and a D=500 circle. Sockets beside them follow the column side (or the circle's tangent): "CAD column 400x400", "CAD circular column D=500". A socket between a wall and a column, the column side 30 mm farther: the column is used |
| 12x | Ignored line work | Door swings, hatch and dimensions near a socket do not orient its plane; the lines of the socket block itself are never taken as a wall |
| 12y | Show detection | Tick *Show detection*, Preview: red detail lines on the detected wall faces and column outlines in the active plan view; choose *Delete them*: the lines are gone and nothing is in the undo list. Run and choose *Keep them*: the lines stay |
| 12z | Detection ranges | Set *DWG wall thickness* to 100-150: a 200 mm wall is no longer detected. Save and Load the mapping: the ranges and *Show detection* come back |
| 13 | Other level | Select all rows, set Level = Level 2 with *Apply to selected rows*, run: 10 placed on Level 2 (the Level 1 elements are not treated as duplicates) |

If everything passes, run it on one real floor, check a few devices of each type, and only then do the whole building.

## 5. Automated tests (developers)

These run without Revit:

```
cd addin && dotnet test tests/CAD2Revit.Core.Tests
```

CI (`.github/workflows/build.yml`) runs them, builds the add-in for Revit 2022–2026, and uploads the installable zip.

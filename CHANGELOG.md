# Changelog

## [0.38.0] - 2026-10-25
### Changed: renamed to DMC Remapper
- The tool is now **DMC Remapper** - *CAD blocks → hosted Revit families* (formerly SmartHost MEP, and before that CAD2Revit). Placement is unchanged.
- Ribbon tab, window titles and dialogs say **DMC Remapper**; the mapping window header shows the tagline. Code, project and DLL: `DMCRemapper`. The add-in keeps the same AddInId.
- New elements use the `DMC_` prefix: reference planes `DMC_<Level>_+2800mm`, vertical planes `DMC_V_<Level>_<n>`, Comments tag `DMC: <block>` (only when `WriteComments` is on), Needs Review mark `DMC: Host = Reference Plane`, detection line style `DMC Detection`.
- **Models made with older versions keep working:** `SmartHost_` and `CAD2Revit_` reference planes and vertical planes are still found and reused (no duplicates, plane numbers continue), and `SmartHost:` / `CAD:` / `CAD2Revit:` Comments still count for the "already placed" check. Fix Host Line also removes the old Needs Review marks.
- Settings and saved mappings move to `%AppData%\DMCRemapper`: copied once from `%AppData%\SmartHostMEP` (or `%AppData%\CAD2Revit`); the old folder is left untouched. Logs go to `Documents\DMCRemapper\Logs` (`dmc_log_<date>.csv`).
- The installer removes the old `SmartHostMEP` and `CAD2Revit` add-ins, so two versions are never loaded together. Package and CI artifact: `DMCRemapper-<version>`.

## [0.37.0] - 2026-10-25
### Fixed: Slab (above) did not find the slab over stair lights
- In a stair core the floor slab has the stair opening, so straight above a stair light there is no Floor - only the landing or flight of the stair above. Slab (above) only looked at floors, roofs and beams, so it found nothing in range and used the fallback plane.
- Slab (above) now also hosts on **stairs** (Stairs, Stair Runs, Stair Landings), in this model and in Revit links: the nearest underside straight above the block wins, as before. Under a landing the family sits flat; under a flight it follows the slope of the flight.
- The note says "stair" when the host is a stair.

## [0.36.0] - 2026-10-25
### Added: Family category filter
- A **Family category** dropdown in the toolbar (next to the search box) limits every *Revit Family : Type* dropdown, and the Family field of the selection bar, to one Revit category, e.g. *Lighting Fixtures* or *Fire Alarm Devices*. Each category shows how many types it has; *All family categories* shows everything.
- Typing in a family dropdown searches inside the chosen category. `(Skip)` and each row's current family always stay in the list, so nothing already picked disappears.
- The choice is remembered (settings.ini `FamilyCategory`).

## [0.35.0] - 2026-10-25
### Changed: the mapping window is redesigned
Placement itself is unchanged; only the windows are new.

**One window instead of three.** *Place Families* opens the mapping window straight away. The DWG and level pop-up is gone: **DWG**, **Level** and **Host default** are dropdowns on one setup row. Preview and Run show their progress bar and Cancel inside the window, and the "already placed" question is a themed dialog over it.

**Layout, top to bottom:**
1. Header: SmartHost MEP, the version, and `DWG: X | Level: Y | 18 block types, 642 instances`.
2. Setup row: DWG (with *Pick in view*), Level, Host default.
3. Toolbar: search box, **All / Mapped / Unmapped** chips with counts, Auto-match, **Columns**.
4. Grid: **Block** (name and count badge), **Revit Family : Type** (searchable, `(Skip)` by default), **Host**, **Elevation (mm)**. Rotation, Facing, Level, Category, Place At, Score, Source View, Top 3 and Detected Host are optional columns (Score/Top 3 appear after Auto-match, Detected Host after Preview). Skipped rows are greyed out.
5. Live summary: `12 mapped, 6 skipped, 590 elements will be placed`; during Preview/Run it becomes the progress bar.
6. **Advanced** (collapsible, closed by default): search ranges, fallback plane height, snap to face, distance check, show detection, include nested blocks.
7. Footer: Load / Save mapping on the left; Preview, **Run** and Cancel on the right.

**Easier to use:**
- Selecting two or more rows shows a bar to set **Family**, **Host** and **Elevation** on all of them.
- Right-click: *Apply this row to selected*, *Skip selected*, *Copy settings from this row*, *Paste settings to selected*, *Select all shown*, *Clear all families*.
- Inline validation: an invalid elevation or rotation turns its cell red with the reason as tooltip; Preview and Run are disabled while there are errors (a note says why).
- Tooltips on every column header and Advanced field.
- Keys: Enter = Preview, Ctrl+Enter = Run, Esc = Cancel, Ctrl+F = search, Del = skip selected, Ctrl+C / Ctrl+V = copy / paste row settings.
- Remembers the window size and position, column widths, shown columns and whether Advanced is open (settings.ini, *Remembered automatically*).

**Look:** one modern style on an 8 px grid with one accent colour and rounded corners. It follows Revit's light or dark theme (Revit 2024+). The window is resizable with a minimum size. The result window and **Needs Review** use the same style (both were WinForms).

### Audit of the old window
| Item | Decision |
|---|---|
| CAD Block, Revit Family, Host Type, Elevation columns | KEEP (main grid) |
| Rotation, Facing, Level, Category, Place At, Score, Source View, Top 3, Detected Host columns | MOVE to optional columns (*Columns* menu) |
| Symbol preview side panel | MOVE to *Columns > Symbol preview panel* (block tooltip still shows the symbol) |
| Step 1 DWG dialog (DWG list, Pick in view, Include nested) | MOVE into the window: DWG dropdown + Pick in view in the setup row, Include nested in Advanced |
| Level per run (was the DWG's level only) | KEEP as the Level dropdown in the setup row |
| Slab/ceiling search range, fallback plane height, wall search, vertical plane search and position, DWG line search, review distance, Show detection | MOVE TO ADVANCED |
| Find box | KEEP as the search box (also matches family names) |
| Show (category) filter and *Skip shown rows* | REMOVE (replaced by search, chips and *Skip selected*; rows are still grouped by category) |
| Header stat chips | REMOVE (replaced by the header status line and the live summary) |
| "2 · Edit selected rows" bar (Host, Level, Elevation, Facing, Category) | KEEP as the selection bar with Family, Host, Elevation; Level/Facing/Category via grid edit of selected rows or *Paste settings* |
| *Reference planes for all rows* checkbox | REMOVE (Host default, or select all + Host) |
| Auto-match, Clear All Families | KEEP (Auto-match in the toolbar, Clear all families in the right-click menu) |
| Load / Save mapping, Preview, Run, Cancel | KEEP |
| Progress pop-up window | REMOVE (progress bar inside the window) |
| Wall/column layer fields, Pick wall/column line buttons, Block back side, parallel-pair / closed-shape ranges | Already removed in 0.31; the old mapping columns `DWG_Wall_Layers` / `DWG_All_Layers` are still ignored when loading |
| Unused code: PlaceDialog, Pickers, ProgressWindow, WinForms result and Needs Review forms | REMOVE |
| settings.ini keys | KEEP (all still used); new: `DefaultHost`, `WindowPlacement`, `GridColumns`, `ShownColumns`, `AdvancedOpen` |

## [0.34.0] - 2026-10-22
### Fixed: family 2D symbols were not found (every family fell back to its 3D outline)
- Symbolic lines in a family are not owned by one view (they show in every parallel view), so 0.32 and 0.33 never saw them. The view is now asked what it shows: symbolic and detail lines, and the curves of detail components, annotation symbols, **nested families** (their symbolic lines and annotations) and imported CAD. Solids are left out, so the 3D body no longer mixes into the symbol.
- A **3D outline** (`... (3D)`, families with no 2D symbol at all) is only offered in *Top 3 Candidates*, never selected automatically: many devices are plain boxes in 3D and all look alike. Those families can still be matched by name.
- Blocks larger than **6000 mm** (a whole plan or xref drawn as a block) get no symbol and are matched by name only.
- The symbol cache is rebuilt once (new format).

## [0.33.0] - 2026-10-22
### Fixed: symbol Auto-match found no symbol for most families
- **More families have a symbol:**
  - Views are found by their type, not their name: every plan view (Ref. Level first), then the Front and Back elevations. Families made in another language, or with renamed views, now work.
  - A family with no 2D symbol (no symbolic lines, detail components or annotation symbols) now uses the outline of its 3D geometry as its plan view shows it. *Source View* shows e.g. `Ref. Level (3D)`. Such families can still be matched by name too, since an outline is a weaker match.
- **Drawing differences matter less:**
  - The element mix is now the share of curved vs straight line work, read from the geometry. A circle drawn as a polyline, or a rectangle drawn as four lines instead of one polyline, no longer lowers the score or drops the family in the pre-filter.
  - The shape score tolerates more difference between the CAD block and the family symbol.
  - The automatic category guess no longer filters families out; only a category you set by hand does.
- **Auto-match report:** after Auto-match you can open `%TEMP%\SmartHostMEP\AutoMatch-report.txt`. It lists each family (which view its symbol came from, or why it has none: in-place, not editable, nothing drawn, could not be opened) and each block (its symbol, the result and the top 3 scores).
- The symbol cache from 0.32 is rebuilt once (new format).

## [0.32.0] - 2026-10-22
### Changed: Auto-match compares symbols, not just names
**Auto-match** now matches each CAD block to a loaded family by the shape of its 2D symbol. No family folder is needed: it uses the families already loaded in the project.
1. **Block symbol:** the block's lines, arcs, circles and polylines. Text, attributes, hatch and wipeouts are left out (by layer name). The symbol is moved to the origin, its rotation is removed and it is scaled to unit size.
2. **Family symbol:** for each family in the Revit Family dropdown, the views **Ref. Level**, **Front** and **Back** are read in that order, and the first one with line work is used: symbolic and detail lines, detail components and annotation symbols, and imported CAD in that view. It is normalized the same way.
   - Only **loadable families** are opened (with Edit Family, closed without saving). System families, in-place families and families that cannot be opened are skipped.
3. **Cache:** family symbols are saved in `%TEMP%\SmartHostMEP\SymbolCache` (never next to the project), so a family is only opened again when it is new or changed (reloaded or edited). Families without a symbol are remembered too. If the folder is deleted, it is rebuilt on the next Auto-match. The first Auto-match on a project can take a while; a progress bar with Cancel is shown.
4. **Pre-filter:** pairs whose category, element counts or aspect ratio are clearly different are not compared.
5. **Score 0-100:**
   - shape overlap: mean distance between points sampled along the two symbols, both ways (chamfer distance), tested at 0/90/180/270° and mirrored;
   - element counts (lines, arcs, circles);
   - aspect ratio;
   - a 64 x 64 raster overlap, as a backup;
   - name similarity only as a tie-breaker (at most 2 points).

   The weights are new settings.ini keys: `SymbolScoreShape` 55, `SymbolScoreCounts` 15, `SymbolScoreAspect` 15, `SymbolScoreRaster` 15, `SymbolScoreName` 2.
6. **Learns from you:** every family you pick or fix by hand is saved (block name + symbol → family type) in `%AppData%\SmartHostMEP\learned-matches.txt` when you click Preview or Run. Auto-match uses it first next time, also for the same symbol under another block name. Automatic matches are never saved there, so they never replace a manual choice.
7. **Match rules:**
   - score **80** or more (`SymbolAutoMatchScore`): the family is selected;
   - if the second best family is within **10 %** (`SymbolAmbiguousPercent`), the best one is still selected and its elements are listed in Needs Review as **"Ambiguous symbol match"**;
   - **50-79**: offered in *Top 3 Candidates* but not selected;
   - below **50** (`SymbolMinimumScore`): the block stays unmatched.
   - Blocks without line work, and families without a symbol, are still matched by name as before.
   - Rows that already have a family are never changed.

### Added
- Grid columns **Score** (of the selected family; `(n)` = best candidate not selected, `?` = ambiguous, `Learned` = from your earlier choice), **Source View** (the family view that matched) and **Top 3 Candidates** (a dropdown with the three best families and their scores; picking one selects it and counts as a manual choice).
- The symbol preview now also shows full circles (they were missing before).
- New Core classes `SymbolSignature`, `SymbolMatcher`, `SignatureCache` and `LearnedMatches`, with tests.

## [0.31.0] - 2026-10-22
### Changed: vertical planes pick the wall line by score
For Host Type **Vertical plane**, each block now goes through three steps:
1. **Revit wall or column** (this model or a link) within the *Wall/column search* (600 mm): the closest face the device sits along.
2. **Otherwise, the best-scoring DWG line** near the block, on any layer:
   - Candidates are straight lines longer than 300 mm within the new *DWG line search* (300 mm, in the window, saved with the mapping as `Vertical_Line_Search_mm`) of the block **outline**, the symbol's bounding box.
   - Left out: the block's own geometry and other mapped or electrical blocks, wires and tails (a line or polyline that starts or ends at the outline), dimension blocks, hatch and dimension layers, and arcs such as door swings.
   - Score: **touch** (the line touches the outline or passes within 20 mm of the insertion point, biggest bonus), **distance** to the outline, **length** (capped at 3000 mm) and **continuity** (other blocks touching the same line, within 5 mm and 0.5°). The weights are new settings.ini keys: `LineScoreTouch` 100, `LineScoreDistance` 50, `LineScoreLength` 30, `LineScoreContinuity` 10, `LineScoreContinuityMax` 5 and `LineScoreAmbiguousPercent` 10.
   - The highest score wins. If the next best line (on another line) scores within 10 %, it is still used, and the element is listed in Needs Review as **"Ambiguous wall line"**.
3. **Otherwise, the block rotation**, listed in Needs Review as **"No wall line found"**.

The plane is on the chosen line and runs along it. The block point is projected onto it, the family faces away from the line toward the block, and elements on the same line share one plane. A snap of more than 200 mm is listed in Needs Review as "Moved N mm to snap to the wall line".

### Removed
- The DWG wall/column layer list and *All layers*: lines on every layer are scored. The old `DWG_Wall_Layers` / `DWG_All_Layers` mapping columns are ignored.
- The symbol back line, and the rule that faces parallel to the block or symbol direction (within 15°) beat closer ones. The block rotation is only used in step 3, or to pick a side when the block sits exactly on the line.

### Added
- **Detected Host** (Preview) shows where each plane came from: `Vertical plane: Revit wall (linked)`, `Touching line (layer: A-WALL)`, `Nearest line (score 74)` or `Block rotation`. The log has the plane name and the scores.
- **Show detection** checkbox (also `ShowDetection` in settings.ini): after Preview or Run, the chosen line of each block is drawn as magenta detail lines in the plan view (line style `SmartHost Detection`), with a short line from the block to where it went. The next Preview or Run removes them.
- **Fix Host Line** ribbon button (Host Placement panel):
  - Click a placed element, then the DWG line it should sit on.
  - The element is recreated on a vertical plane on that line (an existing plane is reused), with the same type, height and parameters, facing away from the line toward its CAD block. The Needs Review mark is removed from its Comments.
  - Option *Apply to all elements of the same block on this line*.
  - It repeats until Esc, and each fix is one Ctrl+Z. The element gets a new Element ID.
- Performance stays the same: one geometry read of the DWG, grid indexes for lines and faces, colinear grouping without a pairwise scan, and one transaction for the run. Detection lines are drawn in their own transaction, so they stay after a Preview.
- New Core class `WallLineScorer` (scores, tails, outlines, same-line groups), with tests.

## [0.30.0] - 2026-10-05
### Changed: the Comments parameter is left empty
- Placed elements no longer get anything written into **Comments** (no `SmartHost: <block>`, no `SmartHost: Host = Reference Plane`). Fallback elements are still listed in **Needs Review** with their Element ID.
- To write them again, set `WriteComments = true` in settings.ini. The old `WriteBlockNameToComments` key is ignored, because settings files written by earlier versions all say true.
- The duplicate check still works: an instance of the same family at the block location counts, and Comments written by earlier runs (`SmartHost:`, `CAD:`, `CAD2Revit:`) are still recognised.

## [0.29.0] - 2026-10-05
### Added: pick the DWG in the view
- **Pick in view...** button next to the DWG list: click the DWG link or import in the Revit view instead of choosing it from the list (only DWGs can be clicked; Esc goes back to the list).
- The **DWG list** shows every DWG link and import in the project with its level (or its view, for view-only links), and opens wider so long names are readable.
- **Pre-selected DWG:** a DWG selected in Revit before starting, else the DWG used last time (saved as `LastDwg` in settings.ini).

## [0.28.0] - 2026-10-05
### Added: select several rows and edit them together
- **Ctrl+click** adds or removes a row and **Shift+click** selects a range, anywhere on the row, including over the Revit Family and other dropdowns (they no longer open or grab the click while Ctrl/Shift is held).
- **Edit one, change all:** with several rows selected, changing a cell in any of them sets the same value on every selected row. This works for Revit Family, Level, Elevation, Rotation, Host Type, Facing, Place At and Category.
- Clicking a row's CAD Block name selects just that row again. The bar above the grid still sets several fields at once.

## [0.27.0] - 2026-10-05
### Improved: vertical planes pick the right wall more often
- **The device must sit along the wall.** A wall or column face only counts when the block point projects onto it (within 100 mm of its ends). A wall that ends before the device, or stops at a door opening, is no longer used.
- **Parallel faces win.** Among the faces within the search radius, one parallel to the device (within 15°) beats a closer perpendicular one, so at a room corner the device follows its own wall. If only a non-parallel face is found, it is used, and Needs Review says "Wall is N° off the block/symbol direction - check the orientation".
- **Direction from the symbol's flat back.** When the CAD symbol has a single straight back line (the flat side of a socket's half circle, a switch's base line), that line gives the wall direction and the facing (from the back toward the rest of the symbol). This works whatever the block rotation or layer, and for mirrored blocks. Symbols without one (circles, crosses, plain rectangles) use the block rotation as before. It is detected once per block name.
- The log shows the chosen face, its angle and the direction source ("symbol back line" or "block rotation").
- New Core code `EdgeSnap.Choose` / `Covers` / `AngleOffDeg` and `SymbolBackLine`, with tests.

## [0.26.0] - 2026-10-04
### Renamed to SmartHost MEP
Formerly **CAD2Revit Family Mapper**.
- **Names.** Display name *SmartHost MEP* (ribbon tab, windows, Needs Review, messages). Code name *SmartHostMEP*: solution, projects, `SmartHostMEP.dll`, namespaces, folders and `SmartHostMEP.addin`.
- **Same add-in for Revit.** The manifest keeps the same AddInId. The ribbon tab is now **SmartHost MEP**, with the panels **Host Placement** and **Tools**.
- **Created elements** use the `SmartHost_` prefix:
  - reference planes `SmartHost_<Level>_+3000mm` and `SmartHost_V_<Level>_<n>`;
  - Comments `SmartHost: <block>` and `SmartHost: Host = Reference Plane`.
- **Models made with the old version keep working.** Its `CAD2Revit_...` reference planes are found and reused (no second plane), and its `CAD: <block>` / `CAD2Revit: ...` Comments count in the duplicate check (no duplicates).
- **Logs** are named `smarthost_log_<date>.csv` (and `smarthost_preview_...`), in `Documents\SmartHostMEP\Logs`.
- **Settings and saved mappings** move to `%AppData%\SmartHostMEP`. On the first run they are copied once from the old folder (`%AppData%\CAD2Revit`), which is left as it was.
- **Installer.** `Install.bat` removes the old `CAD2Revit.addin` and its folder from each Revit version, so the two are never loaded together. `Uninstall.bat` removes both.
- **Build.** The CI artifact and release are named `SmartHostMEP-<version>`.
- **Repository:** https://github.com/ahmedel0865-lab/SmartHost-MEP

## [0.24.0] - 2026-10-04
### Changed: Place At defaults to Symbol centre
- New rows and the mapping window now use **Symbol centre**, so the family lands on the drawn CAD symbol, not on the block's base point.
- An empty `Place_At` cell, or a mapping saved before the column existed, now loads as Symbol centre. Rows saved with `Base point` keep it.
- Blocks with no line work still fall back to their base point (noted in the result message).
- Test for the new default.

## [0.23.0] - 2026-10-01
### Changed: vertical reference planes follow the nearest wall or column, not the block rotation
For Host Type **Vertical plane**, per block:
1. **Nearest edge** within **Wall/column search** (new box, default 600 mm):
   - first the side faces of walls and of architectural and structural columns, in this model and in links, that exist at the device height;
   - else the DWG line work on the **DWG wall/column layers**. That is a new box (wildcards, default `*WALL*, *COL*, *A-WALL*, *S-COLS*`) with an **All layers** option. Lines and polylines (and arcs, tessellated) are collected once from the DWG, ignoring segments under 100 mm and hatch layers. With All layers, lines inside blocks (the device symbols) are skipped;
   - else the block rotation, with Needs Review: "No wall/column within 600 mm - used block rotation".
2. **Orientation**: the plane direction is the edge direction, and the family faces away from the wall/column, toward the side the block is on. It is flipped after placing if needed.
3. **Position** (new option): **Snap to face** (default) puts the plane on the face, with the device at the block point projected onto it. **Through block point** puts the plane through the block point, parallel to the face. A snap of more than 200 mm goes to Needs Review.
4. **Reuse**: blocks on the same face share one plane (same line within 5 mm and 0.5°).
5. The **distance check** now measures against each element's **intended point** (the snapped face point, the wall face, or the CAD block), so expected snaps aren't flagged. Elements more than 50 mm from that point go to Needs Review with their Element ID.
6. **Performance**: walls, columns and DWG segments are indexed once per run in a grid (`EdgeIndex`), so each block only checks the nearby cells. It all still runs in one transaction.
- All new options are saved with the mapping: `Vertical_Edge_Search_mm`, `Vertical_Plane_Position`, `DWG_Wall_Layers`, `DWG_All_Layers`.
- New Core classes `EdgeIndex`, `EdgeSnap` and `LayerFilter`, with tests.

## [0.22.0] - 2026-10-01
### Fixed: one block type lands away from its CAD symbol
When only one block goes wrong, the cause is usually in how that block was drawn:
- **Base point away from the symbol.** For example, the base point is on the wall line and the circle is drawn in the room. The family went on the base point, so it looked away from the CAD symbol.
  - New per-row **Place At** column: **Base point** (default) or **Symbol centre**. With Symbol centre, the family goes on the centre of the drawn symbol (bounding box of the block's line work, computed once per block).
  - It is saved in the mapping as `Place_At`.
  - Blocks whose base point is more than 150 mm from their symbol are listed in **Needs Review** with that hint. Wall rows are not, because their base point is often on the wall on purpose.
- **Mirrored blocks faced the wrong way.** The facing was derived from the block's rotation, which is the block's X axis. For a mirrored block, the +Y axis is flipped, so devices faced the opposite side. The facing now comes from the block's real +Y axis.
- Tests for Place At (parse, save/load) and for the facing of normal and mirrored blocks.

## [0.21.0] - 2026-10-01
### Fixed: families on vertical reference planes placed far from their CAD blocks
- **Cause.** The 0.16 *centre on the CAD point* correction (`CenterFamiliesOnCadPoint`, on by default, and written as `true` into every settings.ini since then) shifted each family by its bounding box. A family with geometry far from its insertion point (nested annotation, hidden lines) was pushed far away. That correction is now **off**, under a new key `CenterGeometryOnCadPoint` (default false). The old key is ignored.
- **One plane per block.** Each block gets a vertical reference plane through its insertion point.
  - It runs along the block's X direction, with its normal on the block's +Y, drawn from the level up to level + 3000 mm (`NewReferencePlane2`), and is named `SmartHost_V_<Level>_<n>`.
- **Reuse only if colinear.** An existing vertical plane (this run, or an earlier one, including the old `CAD2Revit vertical ...` planes) is reused only if the block point lies on it (< 5 mm) and its direction matches (< 0.5°). It is never reused because it is the nearest plane or on the same level or elevation.
- **Position.** The family goes on the block point at level + Elevation From Level, on the plane's reference, with the plane direction as its reference direction.
- **Verify and correct.**
  - After placing, a family that faces away from the block is flipped: work plane flip, or *flip facing* when `FacingOrientation` is opposite to the block.
  - If its location point is more than 10 mm from the block point, it is moved there with `ElementTransformUtils.MoveElement`.
  - If it still isn't within 10 mm, it goes to **Needs Review**: "Placed N mm away from CAD block".
### Added: final distance check for all host types
- After the run, with one regeneration, each placed element's location is compared with its CAD block in plan.
  - Anything farther than **Review if farther than (mm)** (new box above the grid, default 50, saved as `Review_Distance_mm`) is listed in **Needs Review** with its Element ID, and gets a warning in the result window.
- Everything still runs in one transaction. The plane search is a simple scan of the tool's vertical planes, with no rays.
- New Core helpers (`VerticalPlacement.CanReuse`, `PlaneName`, `PlanDistanceMm`, `PlacedAwayReason`), with tests.

## [0.20.0] - 2026-09-30
### Fixed: wall hosting (Host Type "Wall")
- **Family on the opposite face.** The old search projected the CAD point onto every wall side face and took the nearest projection. Where the near face had a door/window opening at that point (or the point was past the face edge), only the far face gave a projection, so the family went on the wrong side. A block with no wall found stood on a vertical plane through the point, rotated like the CAD block.
- **Family rotated like the CAD block.** For wall hosting the CAD rotation is now ignored: the family uses the wall direction.
### New wall logic, per block
1. **Find the wall**: the nearest wall by plan distance to the wall body (location line and face offsets), within the **Wall search distance**. This is a new box above the grid, default 500 mm (from `WallSearchDistanceMm`), and it is saved with the mapping (`Wall_Search_Distance_mm`). Walls in this model and in links count, if they exist at the row's height.
2. **Pick the face on the block's side**: the sign of the CAD point's offset from the location line, compared with the offsets of the two side faces. Inside the wall thickness, the side the block symbol is drawn on (its centre of geometry) decides.
3. **Position**: the CAD point projected perpendicularly onto that face, at level + Elevation From Level.
4. **Orientation**: the reference direction is the wall direction, so the family sits flat on the face and faces out towards the block.
   - Face-based families facing into the wall are flipped after placing.
   - Legacy wall-hosted families get *flip facing*, and *flip hand* if mirrored.
5. **Straight and curved walls**: on a curved wall, the tangent at the projected point is used. Other curve types use `Curve.Project`.
6. **Needs Review** now also lists wall blocks: *No wall within 500 mm* (then the vertical/reference-plane fallback, Comments `CAD2Revit: Host = Reference Plane`), *Moved more than 200 mm to reach the wall face*, and *Wall is in a linked model*.
- Performance is unchanged: walls are collected once into the location-line index, face offsets are computed once per wall, and everything still runs in one transaction. No ray per block, except for walls that can't be indexed.
- New Core class `WallPlacement`, with tests. They include: a block on the right face goes on the right face; a block drawn at an angle still sits flat on the wall; a point inside the wall; a location line on a face; past the wall end; curved walls (clockwise and counter-clockwise).

## [0.19.2] - 2026-09-30
### Fixed
- **Place Families crashed** with `ArgumentException: Corresponding button not found (defaultButton)` when blocks were already placed. The "already placed" dialog set its default button before adding its buttons. The default (*Skip them*) is now set after the buttons are added.

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
- **Warning before Run when elements are already there.** Before a Run, the tool checks every block location for an element that is already in the model: an instance of the same family (the existing duplicate check, same tolerance and level band), or one whose Comments say `CAD: <block>` (placed by SmartHost MEP, even with another family). If any are found, it asks: *"X elements already exist at these locations."* **Skip them** (default) / **Place anyway** / **Cancel** (back to the mapping window). Skipped blocks are listed as duplicates in the result window and the log.
- The duplicate check during a run also counts elements whose Comments say `CAD: <block>` for the same block.

## [0.18.0] - 2026-09-29
### Changed: Ceiling uses the same search and fallback as Slab (above)
- **One shared search.** Slab (above) and Ceiling now use the same code (`HostFinder.FindUnderside`, with the host type choosing the categories): the nearest bottom face straight above the block, from the level up to the search range, in this model and in links. Ceiling searches Ceilings; Slab (above) searches Floors, Roofs and beams. Hosts are still collected once into the face index, with no ray per block.
- **Ceiling found**: the face-based family is hosted on the ceiling's bottom face, facing down, with the CAD rotation. Level-based families are placed level-based at the ceiling's underside height (before: failed).
- **No ceiling in range**: same fallback as Slab (above). The family goes on one reference plane per level, `SmartHost_<Level>_+3000mm`, facing down, or level-based at that height. It is logged as `No ceiling within 5000 mm - placed on reference plane at +3000 mm` and its Comments get `CAD2Revit: Host = Reference Plane`. Before, it was placed unhosted at the row's elevation, and the search was capped at the next level or `HostSearchDistanceMm`.
- **Needs Review** lists Ceiling fallbacks too (reason `No ceiling within 5000 mm`), with the same select/zoom, Copy IDs and Export to Excel.
- **Counts** in Preview and the result window: `Ceiling: N hosted on ceiling, N on reference plane, N level-based`, next to the Slab (above) line.
- The mapping window's two values are now labelled **Slab (above) and Ceiling: Search range (mm) / Fallback reference plane height (mm)**. They are saved in the same `Slab_Search_Range_mm` / `Slab_Fallback_Plane_mm` columns, so existing mapping files keep working.
- Duplicate check: re-runs now also see instances placed above the next level (up to the search range / fallback plane) as duplicates.

## [0.17.0] - 2026-09-29
### Changed: Slab (above) hosts on slabs or beams, and a Needs Review list
- **Nearest slab or beam.** Slab (above) now looks for Floors, Roofs **and Structural Framing (beams)**, in this model and in links, within the search range above the level. The nearest bottom face straight above the block wins, so a drop beam under the slab is chosen over the slab. A beam is only used where the point is under its bottom face; otherwise the next host up is used. Hosts are still collected once into the face index, with no ray per block.
- **Hosting** on the found slab/beam: bottom face, facing down, CAD rotation (unchanged).
- **Fallback** (no slab or beam in range): unchanged, one reference plane per level at the fallback height (`SmartHost_<Level>_+3000mm`), or level-based at that height. The log text is now `No slab/beam within 5000 mm - placed on reference plane at +3000 mm`.
- **Comments.** Every fallback element's Comments is set to `CAD2Revit: Host = Reference Plane` (followed by ` | CAD: <block>` when writing block names is on), so it can be found with a filter or schedule.
- **Needs Review window** after a run, when there are fallback elements: Element ID, Family : Type, CAD Block, X, Y (mm), Reason. Clicking a row selects and zooms to the element in Revit. Buttons: **Select All in Revit**, **Copy IDs** (comma-separated, for Manage > Select by ID), **Export to Excel** (.xlsx or .csv) and Close.
- **Counts** in Preview and in the result window: `Slab (above): N hosted on slab, N hosted on beam, N on reference plane, N level-based`.

## [0.16.0] - 2026-09-29
### Changed: Slab (above) search range and fallback plane
- **Search range.** Slab (above) looks for a slab from the row's level up to the **Slab search range** (default 5000 mm), and ignores anything higher, even when the level above is higher. It replaces "next level + `SlabSearchToleranceMm`", and the 0.15.3 extra search of sloped faces up to `HostSearchDistanceMm`. Floors and roofs, in this model and in links, are still collected once and looked up in memory: there is no ray per block.
- **Slab found**: the face-based family is hosted on the slab's bottom face, facing down, with the CAD rotation (unchanged).
- **No slab in range**: the family is hosted on one reference plane per level, `SmartHost_<Level>_+3000mm`, facing down, at the **Fallback reference plane height** (default 3000 mm). The plane is reused by every block that falls back and by later runs. Before, the plane height came from the biggest slab above or the level above.
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
### Fixed: Revit looks frozen after a SmartHost MEP window is closed or hidden
- Every SmartHost MEP dialog (DWG picker, mapping window, results, lists) is now **owned by Revit's main window**, so it always stays on top of Revit. Before, the DWG picker, the result window and the list pickers had no owner and could drop behind Revit. Revit stays locked while a dialog is open, so it looked frozen and the dialog could not be found.
- The mapping window and the result window can no longer be **minimized**. They have no taskbar button, so a minimized window could not be brought back while Revit stayed locked.
- **Ribbon tab**: start-up never fails. If a *Mapper* or *Tools* panel already exists on the SmartHost MEP tab (a second copy of the add-in, or the old pyRevit extension), it is reused or a separate panel is added, instead of the add-in failing to load and the buttons disappearing.

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
- The repository now contains **only the standalone Revit add-in**. The pyRevit extension and its Python tests are removed.
  - Everything the pyRevit version did is in the add-in, including face hosting on **sloped** ceilings and slabs (PR #3 was the pyRevit fix for this, now superseded).
  - The add-in already hosts on the exact point of the face with the CAD rotation projected into the face plane, for ceilings, slabs, roofs and beams, in linked models too.
  - If you had installed the pyRevit version, remove it (`pyrevit extend remove CAD2Revit`) so only one ribbon tab remains.
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
  - **No slab at a block** (a slab opening, or no slab there): the family is hosted on a reference plane at the **underside of that level's slab** (or the top, for Slab (below)). That slab is found from the floors' extents in the model and its links. If the level has no slab, the plane goes at the level above's elevation (or at the level itself for Slab (below)). The plane is created or reused as `SmartHost_<Level>_+<elev>mm`.
  - **Family not face-based / work-plane-based**: placed level-based at *Elevation From Level*.
- **Detected Host (Preview)** column in the mapping window.
  - After **Preview**, each row shows the host it would use, e.g. `Floor: 250mm RC Slab - Third Floor (linked: STR.rvt)`.
  - Mixed results are counted, e.g. `... (38) · Reference plane SmartHost_Second Floor_+3250mm (4) · 1 failed`.
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
  - Hosting, reference planes (`SmartHost_<Level>_+<elev>mm`), vertical planes, the ceiling search limit and the duplicate check all work per level.
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
  - Creates a **horizontal reference plane** at level + *Elevation From Level*, named `SmartHost_<Level>_+<elevation>mm`, spanning the DWG link extents plus 1 m.
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
- **Per-project memory**: the grid is saved when you click Preview/Run and pre-fills the window next time (`%AppData%\SmartHostMEP\projects\`).
- **Load Mapping / Save Mapping** (XLSX or CSV, same format as before).
- Preview returns to the mapping window with your choices kept.
- Unit tests for name matching, project keys, and saving mappings with Skip rows.

### Changed
- Place Families step 1 now only asks for the DWG, level and nested-blocks option; the mapping file is optional (Load Mapping).
- Blocks set to (Skip) are logged as `unmapped` ("not mapped (Skip)").
- Logs are written to `Documents\SmartHostMEP\Logs\<project>\`.

## [0.3.0] - 2026-09-25
### Added
- **Standalone Revit add-in (C#)** in `addin/`, so pyRevit is no longer required. Builds for Revit 2022, 2023 and 2024 (.NET Framework 4.8) and Revit 2025 and 2026 (.NET 8), with its own SmartHost MEP ribbon tab (List Blocks, Place Families).
- Same features as 0.2.0: XLSX/CSV mapping, ceiling/face/wall/non-hosted placement (incl. linked hosts), one-dialog workflow, real preview, per-level duplicate check, one-transaction undo, summary window and CSV log.
- Settings in `%AppData%\SmartHostMEP\settings.ini` (created on first run; also remembers the last mapping file).
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

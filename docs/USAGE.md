# Usage Guide

## 0. Install (once)

DMC Remapper is a **standalone Revit add-in**. It does not need pyRevit or any other add-in.

1. Download `DMCRemapper-<version>.zip`: from the repository's **Releases** page, from the **Actions** tab (latest build > *Artifacts*), or from whoever sent it to you.
2. **Close Revit**, unzip the file anywhere, and double-click **`Install.bat`**.
   - It installs the add-in for every Revit **2022 / 2023 / 2024 / 2025 / 2026** found on the PC.
   - Per Windows user, so no administrator rights are needed. Files go to `%AppData%\Autodesk\Revit\Addins\<version>\`.
3. Start Revit. When Revit asks about loading the unsigned add-in *DMC Remapper*, click **Always Load**.
4. A **DMC Remapper** tab appears with two panels: **Mapper** (**Place Families**) and **Tools** (**Settings**, **Help**).

- **Manual install** (if IT policy blocks `Install.bat`): copy `<version>\DMCRemapper.addin` and the folder `<version>\DMCRemapper` into `%AppData%\Autodesk\Revit\Addins\<version>\`. Then right-click `DMCRemapper\DMCRemapper.dll` > *Properties* > tick **Unblock**.
- **Update:** close Revit and run `Install.bat` from the new zip.
- **Uninstall:** close Revit and run `Uninstall.bat`.

> Earlier versions also shipped a pyRevit extension. It was removed in 0.13.0; everything it did (including face hosting on sloped ceilings and slabs) is in the add-in. If you installed it, remove it with `pyrevit extend remove CAD2Revit` (its name before the rename, or delete the extension folder), so only one ribbon tab remains.

## 1. Prepare the drawing (AutoCAD)

- Devices must be **blocks** (INSERTs). Exploded symbols are just lines and cannot be detected.
- Give each device type its own block name (`LIGHT-600x600`, `SMOKE-DET`, ...). Blocks that differ only in attributes map to the same family type.
- Set the drawing units (`INSUNITS`) correctly (usually millimetres) so Revit scales the link correctly.
- `PURGE` and `AUDIT` the file. Binding xrefs is recommended; xref blocks are read, but their names may appear as `XREF$0$BLOCK`.
- Dynamic blocks: if a dynamic block has been stretched or flipped, AutoCAD stores it as an anonymous block (`*U123`) and Revit only sees that name. Run `BCONVERT`/`RESETBLOCK` or use normal blocks for devices (see Limitations).

## 2. Prepare the Revit project

1. Load all the families you want to place (*Insert > Load Family*).
   - For ceiling/wall hosting use **face-based** families (template "Generic Model face based", "Electrical Fixture" face-based, etc.). They can host on faces of linked architectural models.
   - Legacy "wall-based"/"ceiling-based" families only host on elements in the **same** model.
2. Open the floor plan of the target level.
3. Link the DWG (*Insert > Link CAD*):
   - *Current view only*: either works (on or off).
   - *Colors*: any. *Layers*: All.
   - *Import units*: Auto-detect (or the real units of the DWG).
   - *Positioning*: whatever you normally use (Auto - Origin to Origin, By Shared Coordinates...). The tool reads the final position of the link, including rotation, so any positioning works.
4. If you want hosting, link the architectural model (ceilings/walls) too.
5. **Work on a copy of the model** for your first runs.

## 3. Map the blocks and place (mapping window)

1. Click **DMC Remapper > Place Families**. The mapping window opens straight away.
2. **The mapping window**, top to bottom:
   - **Header:** DMC Remapper, the version, and what is loaded: `DWG: <name> | Level: <level> | 18 block types, 642 instances`.
   - **Setup row:**
     - **DWG**: every DWG link and import in the project, with the level it is on (or the view, for view-only links). Pre-selected: a DWG you selected in Revit before starting, else the one used last time. Choosing another DWG reads its blocks; families already picked for blocks with the same name are kept. **Pick in view** lets you click the DWG in the drawing (Esc comes back).
     - **Level**: the level the rows are placed on. It starts at the level the DWG is linked on (or the active plan's level). Rows set to another level in the *Level* column keep theirs.
     - **Host default**: the Host of new rows. Changing it also changes every row that still has the previous default. It is remembered.
   - **Toolbar:** the **search** box (Ctrl+F; matches block and family names), the **Family category** filter (every *Revit Family : Type* dropdown and the selection bar then list only types of that Revit category, e.g. *Lighting Fixtures*; `(Skip)` and each row's current family stay listed; remembered), the **All / Mapped / Unmapped** chips with their counts, **Auto-match** and **Columns** (show or hide the optional columns and the symbol preview panel).
   - **The grid:** one row per **unique** CAD block name (not one row per instance), **grouped by category** (Electrical first) and sorted by name. Rows on `(Skip)` are greyed out. Column widths can be dragged and are remembered.
   - **Summary:** `12 mapped, 6 skipped, 590 elements will be placed`, and the result of the last Preview. During Preview and Run this line becomes a **progress bar** with **Cancel**.
   - **Advanced** (closed by default, remembered): slab/ceiling search range, fallback plane height, wall search, vertical plane search and position (snap to face), DWG line search, *Review if farther than* (distance check), *Show detection* and *Include nested blocks*. Every field has a tooltip; an invalid value turns red, opens Advanced and disables Preview/Run.
   - **Footer:** **Load mapping... / Save mapping...** on the left; **Preview**, **Run** and **Cancel** on the right. Next to them, a short note says why Preview/Run are disabled (red cells, nothing mapped, an invalid Advanced value).
   - **Keys:** Enter = Preview, Ctrl+Enter = Run, Esc = Cancel, Ctrl+F = search, Del = skip the selected rows, Ctrl+C / Ctrl+V = copy / paste a row's settings.
   - The window follows Revit's **light or dark theme** (Revit 2024 and later) and remembers its size and position.
   - Block names are simplified so instances group correctly. The `<file>.dwg.` prefix Revit adds is removed. For DWGs **exported from Revit**, the `-<element id>-<view name>` suffix is removed too, so `MAAP_Ceiling Mounted Luminaire - F1-7107100-GROUND FLOOR LIGHTING PLAN` becomes `MAAP_Ceiling Mounted Luminaire - F1`. Turn this off with `SimplifyBlockNames = false` in settings.ini.

| Column | What to do |
|---|---|
| **Block** | Block name and number of instances (badge). Read-only. Hover to see its symbol. |
| **Revit Family : Type** | Pick the family type. **Type in the box to search**: every word you type must appear, so `smo cei` finds *Smoke Detector : Ceiling*. Press **Enter** to take the first match, **Esc** to restore the previous choice. `(Skip)` = not placed. |
| **Host** | None (level-based), Ceiling, Slab (above), Slab (below), Wall, Reference Plane (auto-create), Face (ceiling/slab/roof) or Vertical plane (no wall). See below. Starts at *Host default*. |
| **Elevation (mm)** | Height above the row's level. Must be a number; an invalid cell turns red (the reason is its tooltip) and Preview/Run stay disabled. |

Optional columns (**Columns** menu):

| Column | What it is |
|---|---|
| Rotation (deg) | Added to the CAD block rotation (counter-clockwise). Validated like Elevation. |
| Facing | Down (default) or Up. Which side a family on a **Reference Plane** faces: Down for ceiling devices (lights, detectors), Up for floor devices (floor boxes). |
| Level | The level of this row. Starts at the Level of the setup row; pick another level to place that block on a different floor in the same run. |
| Category | Electrical, Mechanical, Plumbing, Architectural, Structural, Annotation or Other, guessed from the block name. Used for grouping. |
| Place At | *Symbol centre* (default) or *Base point*. |
| Score, Source View, Top 3 Candidates | Auto-match results; shown automatically after Auto-match. |
| Detected Host | Filled in by **Preview**: the host each block would use, e.g. `Floor: 250mm RC Slab - Third Floor (linked: STR.rvt)`. Shown automatically after Preview. |

- The dropdown lists loaded family types in the electrical categories: Lighting Fixtures, Lighting Devices (switches), Electrical Fixtures, Electrical Equipment, Fire Alarm Devices, Communication Devices, Data Devices, Security Devices, Nurse Call Devices and Telephone Devices. A family from another category is added to the list automatically when a loaded mapping file uses it.
- **Families start at `(Skip)`:** every time the tool opens, the Revit Family of every row is `(Skip)`. Nothing is filled in from the last run. Click **Auto-match**, or **Load...** a mapping file. Always check the pre-selections. **Clear All Families** sets every row back to `(Skip)`.
- **Auto-match** (rows still on `(Skip)`; mapped rows are never changed):
  1. A family you picked by hand before for the same block and symbol (or the same symbol under another name) comes first. Score shows *Learned*.
  2. Otherwise the block's symbol is compared with the symbol of each loaded family in the Revit Family dropdown. The family's plan views (*Ref. Level* first) and then its *Front* and *Back* elevations are read, in that order (symbolic/detail lines, detail components, annotation symbols, imported CAD in the view). Views are found by type, so other languages work. Nested families count with their symbolic lines and annotations (not their 3D body). A family with no 2D symbol uses the outline of its 3D geometry in plan (Source View `... (3D)`): it is offered in Top 3 only, never selected automatically, and can still be matched by name. Blocks larger than 6000 mm (a plan drawn as a block) are matched by name only. Only loadable families are opened; system and in-place families are skipped. The first Auto-match on a project opens each family once (progress bar, Cancel stops it); the symbols are cached in `%TEMP%\DMCRemapper\SymbolCache`, so later runs only open new or changed families. Deleting that folder is safe.
  3. Score 0-100: shape (best of 4 rotations x mirrored), element counts, aspect ratio and a raster check; the name only breaks ties. **80+** is selected; if the second best is within 10 %, it is still selected and listed in Needs Review as *Ambiguous symbol match*. **50-79** is offered in **Top 3 Candidates** only. Below 50 the block stays unmatched. Weights and limits: `SymbolScore*`, `SymbolAutoMatchScore`, `SymbolMinimumScore`, `SymbolAmbiguousPercent` in settings.ini.
  4. Blocks without line work, and families without a symbol, are matched by name (e.g. `SMOKE-DET` → *Smoke Detector*, `SKT-DOUBLE` → *Duplex Receptacle*, `MCP` → *Manual Call Point*).
  - Columns: **Score** (of the selected family; `(n)` = best candidate, not selected; `?` = ambiguous), **Source View** and **Top 3 Candidates** (pick one to use it).
  - After Auto-match you can open a report (`%TEMP%\DMCRemapper\AutoMatch-report.txt`): each family's symbol view or why it has none, and each block's top 3 scores. Send it along if matches look wrong.
  - Every family you pick or fix by hand is remembered when you click Preview or Run (`%AppData%\DMCRemapper\learned-matches.txt`).
- **Remembered per project:** the grid is saved automatically when you click Preview or Run. The next time you open the mapping window in the same project, Elevation From Level, Host Type, Rotation, Facing, Level, Category and the Slab (above) / Ceiling values are restored; the families are not. The file is `%AppData%\DMCRemapper\projects\<project>_<id>.xlsx`, in the normal mapping format.
- **Already placed?** Before a Run, if elements are already at the block locations (same family within the duplicate tolerance, or Comments `DMC: <block>` / `CAD: <block>` written by an earlier run), the tool asks *"X elements already exist at these locations"*: **Skip them** (default), **Place anyway**, or **Cancel** to go back to the mapping window.
- **Load Mapping... / Save Mapping...** read and write the normal mapping file (XLSX or CSV, format below), e.g. to reuse one mapping across projects or share it with the team. Loading only changes the rows whose block names are in the file.

4. Click **Preview**. The tool runs the full placement, including host searches, and then **undoes it**. The result window shows exactly what *Run* would do: counts per family type, unmapped blocks, failures with reasons. **Close the result window to return to the mapping window** with your choices kept, adjust, and preview again.
5. Click **Run**. Everything is placed in **one transaction** named *DMC Remapper: Place families*. A single **Ctrl+Z** removes all of it.

Host types:

| Host_Type | What it does |
|---|---|
| `non-hosted` = **None (level-based)** | Placed on the level at the elevation, rotated like the CAD block. |
| `ceiling` | Casts a ray straight up from the block and hosts on the first **ceiling** face (this model or linked models), up to the next level or 6 m. |
| `face` | Same, but also hosts on floor/roof undersides and beams (useful where there is no ceiling, e.g. car parks, plant rooms). |
| `slab above` = **Slab (above)** | Hosts on the **nearest underside straight above** the block (from the row's level up to the Slab search range), **facing down**, with the CAD rotation as direction. Floors, roofs, beams and **stairs** (landings and flights - over stair lights the slab has the stair opening) count, in this model and in linked models; ceilings and ducts are ignored. The search goes up to the level-to-level height + `SlabSearchToleranceMm` (500 mm), so never two floors up. **No slab at a block** (opening, or no slab) → hosted on a reference plane at the underside of that level's slab (or at the level above if the level has no slab), with a warning. **Not a face-based / work-plane-based family** → not placed: status `failed`, "family is not face-based". The Facing column is not used. |
| `slab below` = **Slab (below)** | For floor boxes / floor sockets: casts a ray down from 300 mm above the level and hosts on the **top face of the slab** at that level, **facing up**. Same fallbacks (reference plane at the slab top, or at the level). |
| `wall` | Hosts on the **nearest wall** to the block (this model or linked models), within the **Wall search distance** (500 mm, in **Advanced**), among walls that exist at the row's height. **Side:** the face on the side of the wall where the CAD point is. If the point is inside the wall thickness, the side the block symbol is drawn on (its centre of geometry) decides. **Position:** the CAD point projected perpendicularly onto that face, at level + Elevation From Level. **Orientation:** the CAD rotation is **ignored**. The family uses the wall direction (the tangent at that point, on curved walls), so it sits flat on the face and faces **out towards the block**. After placing, a family facing into the wall is flipped. Legacy wall-hosted families get *flip facing* and, if mirrored, *flip hand*. **No wall found:** a face-based device is stood upright on a vertical plane instead (as `vertical` below). **Needs Review** lists: *No wall within 500 mm*, *Moved more than 200 mm to reach the wall face*, and *Wall is in a linked model*. |
| `reference plane` = **Reference Plane (auto-create)** | Creates (or reuses) a **horizontal reference plane** at *level elevation + Elevation From Level*, named `DMC_<Level>_+<elevation>mm` (e.g. `DMC_Level 1_+2800mm`; up-facing planes end in `_Up`). The plane covers the DWG link's extents plus 1 m. The family is hosted on it, with the CAD block rotation as its direction and facing Down or Up (the **Facing** column). All rows with the same elevation and facing share one plane, and later runs reuse it. Only face-based / work-plane-based families can be hosted this way; others are placed level-based with a warning. |
| `vertical` = **Vertical plane (no wall)** | **No Revit host needed.** Each block gets a **vertical reference plane on the wall line it sits against**. **1. Revit wall or column:** the closest wall or column face (architectural and structural, this model and links, at the device height) within *Wall/column search* (600 mm, set above the grid) that the device sits along. The block point must project onto it, within 100 mm of its ends. **2. Otherwise, the best DWG line on any layer:** straight lines longer than 300 mm within *DWG line search* (300 mm) of the block **outline**. Left out: the block's own geometry and other mapped or electrical blocks, wires and tails that start or end at the outline, dimensions, hatch lines and arcs. Each line is scored for touching the outline or passing within 20 mm of the insertion point (biggest bonus), distance, length (capped at 3000 mm) and continuity (other blocks touching the same line). The weights are in settings.ini (`LineScore...`). The highest score wins. If the next best line is within 10 %, the element also goes to Needs Review as "Ambiguous wall line". **3. Otherwise, the block rotation,** with Needs Review "No wall line found". **Plane:** on the chosen line and along it; the family faces away from the line, toward the block. *Position* (above the grid): *Snap to face* (default) puts the device on the block point projected onto the line. *Through block point* keeps the block point, parallel to the line. A snap of more than 200 mm goes to Needs Review. Elements on the same line share one plane `DMC_V_<Level>_<n>` (within 5 mm and 0.5°). The device goes at level + Elevation From Level. If it lands more than 10 mm from its intended point, it is moved there. If that fails, it goes to Needs Review. **Check it:** *Detected Host* shows the source (`Revit wall (linked)`, `Touching line (layer: ...)`, `Nearest line (score n)`, `Block rotation`). *Show detection* draws the chosen lines in the plan view, and **Fix Host Line** on the ribbon moves an element onto the line you click. |

The elevation is also the fallback height if a ceiling is not found.

**Place At (per row):** the default is *Symbol centre*, so the family lands on the drawn symbol even when the block's base point is elsewhere. Pick *Base point* when the insertion point is the exact location. A typical case is a wall light whose base point is on the wall line and whose circle is in the room: with *Base point* the family sits on the wall line, away from the circle. With *Base point*, when a block's base point is more than 150 mm from its symbol, the block is listed in Needs Review as "Block base point is N mm from its symbol - set Place At = Symbol centre" (not for Wall rows, where the base point is often on the wall on purpose). **Mirrored blocks** face the side their symbol is drawn on: the facing comes from the block's real +Y axis, not from its rotation.

**Final distance check (all host types):** after placing, every element's location is compared with its CAD block in plan. Anything farther than **Review if farther than (mm)** (in **Advanced**, default 50 mm, saved with the mapping as `Review_Distance_mm`) from its **intended point** is listed in **Needs Review** with its Element ID. The intended point is the CAD block, or the wall face / snapped face point the tool chose. A warning also appears in the result window.

**Hosting checks (ceiling, face, slab above/below, wall):**

- `ceiling`, `face` and the slab types need a **face-based** family (placement type *Work Plane-Based*). A level-based family on these rows is **not placed**: it is logged as `failed` with "family is not face-based".
- After each face-hosted placement, the tool checks where Revit really put the instance. It must be hosted on the face found: for a slab or ceiling in a **Revit link**, *Host* = the link and *Host Face* set. If Revit hosted it on a reference plane or a level instead, the placement is retried once with a fresh face reference from the linked element's own geometry. If it still isn't on the face, the block is logged as `failed - not hosted on linked slab: Host is Reference Plane` (or similar) and nothing is left in the model for it.
- The **reference-plane fallback** of the slab types (no slab found at that point) is unchanged, and it is shown as a warning. It is a deliberate fallback, not a hosting failure.
- `floor` is a short name for **Slab (below)** (top of slab). For devices on a slab **soffit**, use `slab`, `slab above` or `face`.

- **Edit many rows at once:**
  1. Select rows with **Ctrl+click** (adds or removes one row) or **Shift+click** (every shown row between the last clicked row and this one), anywhere on the row: over the dropdowns too, they do not open while Ctrl/Shift is held. Or **Ctrl+A** (all rows shown after search / chips).
  2. A blue bar appears above the grid: choose **Family**, **Host** and/or **Elevation (mm)** and click **Apply to selected**. Fields left at *(keep)*, or an empty elevation, are not changed. **Skip selected** sets them all to (Skip).
  - **Edit in the grid:** with several rows selected, change a cell in any of them (Revit Family, Level, Elevation, Rotation, Host, Facing, Place At, Category) and the same value is set on every selected row. Click the Block name of a row to go back to selecting just that row.
  - **Right-click menu:** *Apply this row to selected* (its family, host and elevation), *Skip selected*, *Copy settings from this row* / *Paste settings to selected* (host, elevation, rotation, facing, level, place at; not the family), *Select all shown*, *Clear all families*.

  Example: search `smoke` → Ctrl+A → Host = *Reference Plane (auto-create)*, Elevation = 2800 → *Apply to selected*.
- For choosing between **Ceiling** and **Reference Plane**, see the table in the [README](../README.md#ceiling-vs-reference-plane-which-host-to-use).

## 4. Mapping file format (Load / Save)

The mapping window lists every block name with its count. **Save** writes the mapping as a file you can fill in or share in Excel, and **Load** reads it back:

| Column | Example | Notes |
|---|---|---|
| CAD_Block_Name | `SMOKE-DET` | Block name (case-insensitive). |
| Revit_Family_Name | `Smoke Detector` | Family name exactly as loaded in the project. **Empty = (Skip).** |
| Revit_Type_Name | `Ceiling` | Type name exactly as in the project. |
| Level | `Level 2` | Optional. Level name; empty = the level picked when running. |
| Offset_From_Level_mm | `2800` | Elevation from level. |
| Rotation_Adjustment_deg | `90` | Rotation adjustment. |
| Host_Type | `ceiling` | `non-hosted`, `ceiling`, `slab above`, `slab below`, `wall`, `reference plane`, `face` or `vertical` (the window's labels are accepted too). |
| Place_At | `Symbol centre` | Optional. `Symbol centre` (default, also when empty or the column is missing): the family goes on the centre of the drawn symbol (its bounding box). `Base point`: it goes on the block's insertion point. |
| Facing | `Down` | Optional. `Down` (default) or `Up`, for reference-plane rows. |
| Category | `Electrical` | Optional. Electrical / Mechanical / Plumbing / Architectural / Structural / Annotation / Other; empty = detected from the name. |

Header spelling is flexible (`Offset_From_Level (mm)`, `offset from level mm`, ... all work). CSV files saved with `;` as separator (European/Middle-East Excel locale) are also accepted. In an .xlsx file, the sheet named **Mapping** is used, or the first sheet if there is none with that name. A full example is in [`templates/mapping_template.xlsx`](../templates/mapping_template.xlsx).

## 5. Check the results

- While placing, the summary line under the grid becomes a **progress bar** with the block count and the time left. **Cancel** (or Esc) stops and rolls everything back; nothing is changed and the window stays open.
- The result window shows placed counts per family type, unmapped blocks, and failed/skipped blocks grouped by reason. Use **Open log** / **Log folder** to jump to the CSV log.
- At the bottom, a **Timings** table shows where the time went: DWG reading, host detection (index build, per block, ray fallback), duplicate check, family creation (single and batched), rotation, planes, parameters, and commit. If a run is slow, this table shows which phase to look at.
- A `dmc_log_<date>.csv` (or `dmc_preview_<date>.csv`) is saved in `Documents\\DMCRemapper\\Logs\\<project>\\`. It has one row per block with:
  `Status, CAD_Block, Family, Type, Level, ElementId, Host, X_mm, Y_mm, Z_mm, Rotation_deg, Block_Scale, Mirrored, Message`.
  Coordinates are Revit internal coordinates in mm. To find an element, copy its ElementId into *Manage > Select by ID*.
- The **Comments** parameter of placed elements is left empty. Set `WriteComments = true` in settings.ini to write `DMC: <block name>` there (and `DMC: Host = Reference Plane` on fallback elements), for schedules and filters.
- Running the tool again skips blocks that already have an instance of the same family within 50 mm on the same level (status `duplicate`). So after adding blocks to the DWG, re-running only adds the new ones.

Statuses in the log:

| Status | Meaning |
|---|---|
| `placed` | Created (in a preview: would be created). |
| `duplicate` | An instance of the same family already exists there, so the block was skipped. |
| `unmapped` | The block is set to **(Skip)** in the mapping window (empty family in a mapping file). |
| `skipped` | Mapped, but the family/type is not loaded in the project. |
| `failed` | Revit refused the placement, or no host was found and fallback is off. The message says why. |
| `timing` | Not a block: one row per phase of the run, with its time and number of calls in *Message* (CAD_Block = phase name). |

## 6. Settings

Click **DMC Remapper > Tools > Settings** to open it in Notepad. Settings are stored in **`%AppData%\DMCRemapper\settings.ini`**, which is created on the first run. Open it in Notepad and change the values; the next command you run uses them. There is no need to restart Revit.

| Setting | Default | Meaning |
|---|---|---|
| `DuplicateToleranceMm` | 50 | Plan distance within which an existing instance counts as a duplicate. |
| `DuplicateSameTypeOnly` | false | `false`: any type of the same family is a duplicate. `true`: only the same type. |
| `IncludeNestedBlocks` | false | Default for the "nested blocks" checkbox. |
| `SimplifyBlockNames` | true | Group block names: remove the `.dwg.` file prefix and, for Revit-exported DWGs, the `-<id>-<view>` suffix. |
| `HostSearchDistanceMm` | 6000 | Max search distance up to a ceiling/soffit (never past the next level). |
| `WallSearchDistanceMm` | 500 | Starting value of the window's **Wall search distance** (max plan distance from the CAD point to a wall). The window's value is saved with the mapping (`Wall_Search_Distance_mm` column). |
| `SlabSearchToleranceMm` | 500 | Slab (above) searches up to the next level + this; Slab (below) searches this far below the level. |
| `SearchRevitLinks` | true | Also host on faces in linked Revit models. |
| `FallbackToUnhosted` | true | If no host is found, place unhosted at the row offset (`true`), or report as failed (`false`). |
| `WriteComments` | false | Write `DMC: <block>` (and `DMC: Host = Reference Plane` on fallback elements) into Comments. Off: Comments is left empty. |
| `DebugHosting` | false | `true`: each block's log *Message* gets a `DEBUG` line: linked yes/no, link name, host element id and category, face normal, and the final *Host* of the placed instance. Example: `DEBUG linked=yes link=STR.rvt element=412233 (Floors) normal=(0.000,0.000,-1.000); host=Revit link STR.rvt, host face ok`. Existing `settings.ini` files don't have this line; add `DebugHosting = true` yourself. |
| `CenterGeometryOnCadPoint` | false | Vertical planes. `false`: the family's origin (insertion point) sits on the CAD point. `true`: the family's geometry (bounding box) is centred on the point along the plane instead. Replaces `CenterFamiliesOnCadPoint` (0.16–0.20), which is now ignored. |
| `LastMappingPath` | | Remembered automatically. |


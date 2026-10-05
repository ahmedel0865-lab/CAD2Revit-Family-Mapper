# Usage Guide

## 0. Install (once)

SmartHost MEP is a **standalone Revit add-in**. It does not need pyRevit or any other add-in.

1. Download `SmartHostMEP-<version>.zip`: from the repository's **Releases** page, from the **Actions** tab (latest build > *Artifacts*), or from whoever sent it to you.
2. **Close Revit**, unzip the file anywhere, and double-click **`Install.bat`**.
   - It installs the add-in for every Revit **2022 / 2023 / 2024 / 2025 / 2026** found on the PC.
   - Per Windows user, so no administrator rights are needed. Files go to `%AppData%\Autodesk\Revit\Addins\<version>\`.
3. Start Revit. When Revit asks about loading the unsigned add-in *SmartHost MEP*, click **Always Load**.
4. A **SmartHost MEP** tab appears with two panels: **Mapper** (**Place Families**) and **Tools** (**Settings**, **Help**).

- **Manual install** (if IT policy blocks `Install.bat`): copy `<version>\SmartHostMEP.addin` and the folder `<version>\SmartHostMEP` into `%AppData%\Autodesk\Revit\Addins\<version>\`. Then right-click `SmartHostMEP\SmartHostMEP.dll` > *Properties* > tick **Unblock**.
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

1. Click **SmartHost MEP > Place Families**.
2. **Step 1:** pick the **DWG link/import**, and optionally *include nested blocks*. Click **Next >**. There is no level to pick here: every row's **Level** starts at the level the DWG is linked on (or the active plan's level) and can be changed per row, or for many rows at once, in the mapping window.
3. **Step 2, the mapping window.** It is organized top to bottom: a header with the DWG and live counts (mapped / instances to place / skipped / invalid); **1 · Filter** (Find, Show, Skip shown rows); **2 · Edit selected rows**; the grid with a **Symbol preview** panel on its right (hover over or select a row to see the CAD block's 2D symbol, size, count and chosen family; hovering the block name shows it as a tooltip too); and the footer (mapping file Load / Save / Auto-match on the left, **Preview** and **Run** on the right). The grid shows one row per **unique** CAD block name (not one row per instance), **grouped by category** (Electrical first) and sorted by name. Use **Find** and **Show** (category) to filter the rows; **Skip shown rows** sets every row currently shown to (Skip), e.g. all Architectural blocks at once.
   - Block names are simplified so instances group correctly. The `<file>.dwg.` prefix Revit adds is removed. For DWGs **exported from Revit**, the `-<element id>-<view name>` suffix is removed too, so `MAAP_Ceiling Mounted Luminaire - F1-7107100-GROUND FLOOR LIGHTING PLAN` becomes `MAAP_Ceiling Mounted Luminaire - F1`. Turn this off with `SimplifyBlockNames = false` in settings.ini.

| Column | What to do |
|---|---|
| **CAD Block** | Block name and number of instances, e.g. `SMOKE-DET (42)`. Read-only. |
| **Category** | Electrical, Mechanical, Plumbing, Architectural, Structural, Annotation or Other. Detected from the block name (e.g. *Luminaire*, *SMOKE-DET* → Electrical; *Door*, *Casework*, *Elevator* → Architectural; *Toilet* → Plumbing; *Grid Head* → Annotation). If the name says nothing, the chosen family's Revit category decides (e.g. Lighting Fixtures → Electrical). Change it if the guess is wrong; it is saved with the mapping. |
| **Revit Family** | Pick the family type (`Family : Type`). **Type in the box to search**: every word you type must appear, so `smo cei` finds *Smoke Detector : Ceiling*. Press **Enter** to take the first match, **Esc** to cancel. `(Skip)` = do not place (the default). |
| **Level** | The level this block is placed on. Starts at the DWG's level; pick another level to place that block on a different floor in the same run. |
| **Elevation From Level (mm)** | Height above the row's **Level**. Must be a number; invalid cells turn red and block Preview/Run. |
| Rotation (deg) | Optional. Added to the CAD block rotation (counter-clockwise). |
| Host Type | None (level-based), Ceiling, Slab (above), Slab (below), Wall, Reference Plane (auto-create), Face (ceiling/slab/roof) or Vertical plane (no wall). See below. |
| Detected Host (Preview) | Filled in by **Preview**: the host each block would use, e.g. `Floor: 250mm RC Slab - Third Floor (linked: STR.rvt)`. Read-only. |
| Facing | Down (default) or Up. Which side a family on a **Reference Plane** faces: Down for ceiling devices (lights, detectors), Up for floor devices (floor boxes). |

- The dropdown lists loaded family types in the electrical categories: Lighting Fixtures, Lighting Devices (switches), Electrical Fixtures, Electrical Equipment, Fire Alarm Devices, Communication Devices, Data Devices, Security Devices, Nurse Call Devices and Telephone Devices. A family from another category is added to the list automatically when a loaded mapping file uses it.
- **Families start at `(Skip)`:** every time the tool opens, the Revit Family of every row is `(Skip)`. Nothing is filled in from the last run or by name. Click **Auto-match** to pre-select families whose names closely match (e.g. `SMOKE-DET` → *Smoke Detector*, `SKT-DOUBLE` → *Duplex Receptacle*, `MCP` → *Manual Call Point*), or **Load...** a mapping file. Always check the pre-selections. **Clear All Families** sets every row back to `(Skip)`.
- **Remembered per project:** the grid is saved automatically when you click Preview or Run. The next time you open the mapping window in the same project, Elevation From Level, Host Type, Rotation, Facing, Level, Category and the Slab (above) / Ceiling values are restored; the families are not. The file is `%AppData%\SmartHostMEP\projects\<project>_<id>.xlsx`, in the normal mapping format.
- **Already placed?** Before a Run, if elements are already at the block locations (same family within the duplicate tolerance, or Comments `CAD: <block>`), the tool asks *"X elements already exist at these locations"*: **Skip them** (default), **Place anyway**, or **Cancel** to go back to the mapping window.
- **Load Mapping... / Save Mapping...** read and write the normal mapping file (XLSX or CSV, format below), e.g. to reuse one mapping across projects or share it with the team. Loading only changes the rows whose block names are in the file.

4. Click **Preview**. The tool runs the full placement, including host searches, and then **undoes it**. The result window shows exactly what *Run* would do: counts per family type, unmapped blocks, failures with reasons. **Close the result window to return to the mapping window** with your choices kept, adjust, and preview again.
5. Click **Run**. Everything is placed in **one transaction** named *SmartHost MEP: Place families*. A single **Ctrl+Z** removes all of it.

Host types:

| Host_Type | What it does |
|---|---|
| `non-hosted` = **None (level-based)** | Placed on the level at the elevation, rotated like the CAD block. |
| `ceiling` | Casts a ray straight up from the block and hosts on the first **ceiling** face (this model or linked models), up to the next level or 6 m. |
| `face` | Same, but also hosts on floor/roof undersides and beams (useful where there is no ceiling, e.g. car parks, plant rooms). |
| `slab above` = **Slab (above)** | Casts a ray straight up from the row's level and hosts on the **underside of the first floor slab** above it (the slab of the level above), **facing down**, with the CAD rotation as direction. Only **Floor** elements count (structural and architectural), in this model and in linked models; beams, ceilings and ducts are ignored. The search goes up to the level-to-level height + `SlabSearchToleranceMm` (500 mm), so never two floors up. **No slab at a block** (opening, or no slab) → hosted on a reference plane at the underside of that level's slab (or at the level above if the level has no slab), with a warning. **Not a face-based / work-plane-based family** → not placed: status `failed`, "family is not face-based". The Facing column is not used. |
| `slab below` = **Slab (below)** | For floor boxes / floor sockets: casts a ray down from 300 mm above the level and hosts on the **top face of the slab** at that level, **facing up**. Same fallbacks (reference plane at the slab top, or at the level). |
| `wall` | Hosts on the **nearest wall** to the block (this model or linked models), within the **Wall search distance** (500 mm, set above the grid), among walls that exist at the row's height. **Side:** the face on the side of the wall where the CAD point is. If the point is inside the wall thickness, the side the block symbol is drawn on (its centre of geometry) decides. **Position:** the CAD point projected perpendicularly onto that face, at level + Elevation From Level. **Orientation:** the CAD rotation is **ignored**. The family uses the wall direction (the tangent at that point, on curved walls), so it sits flat on the face and faces **out towards the block**. After placing, a family facing into the wall is flipped. Legacy wall-hosted families get *flip facing* and, if mirrored, *flip hand*. **No wall found:** a face-based device is stood upright on a vertical plane instead (as `vertical` below). **Needs Review** lists: *No wall within 500 mm*, *Moved more than 200 mm to reach the wall face*, and *Wall is in a linked model*. |
| `reference plane` = **Reference Plane (auto-create)** | Creates (or reuses) a **horizontal reference plane** at *level elevation + Elevation From Level*, named `SmartHost_<Level>_+<elevation>mm` (e.g. `SmartHost_Level 1_+2800mm`; up-facing planes end in `_Up`). The plane covers the DWG link's extents plus 1 m. The family is hosted on it, with the CAD block rotation as its direction and facing Down or Up (the **Facing** column). All rows with the same elevation and facing share one plane, and later runs reuse it. Only face-based / work-plane-based families can be hosted this way; others are placed level-based with a warning. |
| `vertical` = **Vertical plane (no wall)** | **No Revit host needed.** Each block gets a **vertical reference plane parallel to the nearest wall or column face**. **1. Find the nearest edge** within *Wall/column search* (600 mm, set above the grid): first the side faces of **walls and columns** (architectural and structural) in this model and in links, at the device height. If there are none, the **DWG line work** on the *DWG wall/column layers* (wildcards, default `*WALL*, *COL*, *A-WALL*, *S-COLS*`, or *All layers*). Lines shorter than 100 mm and hatch layers are ignored, and with *All layers* lines inside blocks are skipped. If nothing is found, the CAD block rotation is used and the block goes to Needs Review: "No wall/column within 600 mm - used block rotation". **Which edge:** only walls/columns the device sits along (the block point projects onto them, within 100 mm of their ends); a face parallel to the device (within 15°) wins over a closer perpendicular one; with only a non-parallel face, it is used and Needs Review says "Wall is N° off the block/symbol direction". The device direction comes from the symbol's flat back line when it has one (socket half circle, switch base line), else from the block rotation. **2. Orientation:** the plane runs along that edge, and the family faces away from the wall/column, toward the side the block is on (flipped after placing if needed). **3. Position** (*Position* above the grid): *Snap to face* (default) puts the plane on the face and the device on the block point projected onto it. *Through block point* puts the plane through the block point, parallel to the face. A snap of more than 200 mm goes to Needs Review. **4. Reuse:** blocks on the same face share one plane `SmartHost_V_<Level>_<n>` (same line within 5 mm and 0.5°). The device goes at level + Elevation From Level. If it lands more than 10 mm from its intended point, it is moved there. If that fails, it goes to Needs Review. |

The elevation is also the fallback height if a ceiling is not found.

**Place At (per row):** the default is *Symbol centre*, so the family lands on the drawn symbol even when the block's base point is elsewhere. Pick *Base point* when the insertion point is the exact location. A typical case is a wall light whose base point is on the wall line and whose circle is in the room: with *Base point* the family sits on the wall line, away from the circle. With *Base point*, when a block's base point is more than 150 mm from its symbol, the block is listed in Needs Review as "Block base point is N mm from its symbol - set Place At = Symbol centre" (not for Wall rows, where the base point is often on the wall on purpose). **Mirrored blocks** face the side their symbol is drawn on: the facing comes from the block's real +Y axis, not from its rotation.

**Final distance check (all host types):** after placing, every element's location is compared with its CAD block in plan. Anything farther than **Review if farther than (mm)** (above the grid, default 50 mm, saved with the mapping as `Review_Distance_mm`) from its **intended point** is listed in **Needs Review** with its Element ID. The intended point is the CAD block, or the wall face / snapped face point the tool chose. A warning also appears in the result window.

**Hosting checks (ceiling, face, slab above/below, wall):**

- `ceiling`, `face` and the slab types need a **face-based** family (placement type *Work Plane-Based*). A level-based family on these rows is **not placed**: it is logged as `failed` with "family is not face-based".
- After each face-hosted placement, the tool checks where Revit really put the instance. It must be hosted on the face found: for a slab or ceiling in a **Revit link**, *Host* = the link and *Host Face* set. If Revit hosted it on a reference plane or a level instead, the placement is retried once with a fresh face reference from the linked element's own geometry. If it still isn't on the face, the block is logged as `failed - not hosted on linked slab: Host is Reference Plane` (or similar) and nothing is left in the model for it.
- The **reference-plane fallback** of the slab types (no slab found at that point) is unchanged, and it is shown as a warning. It is a deliberate fallback, not a hosting failure.
- `floor` is a short name for **Slab (below)** (top of slab). For devices on a slab **soffit**, use `slab`, `slab above` or `face`.

- **Edit many rows at once:**
  1. Select rows with **Ctrl+click**, **Shift+click**, or **Ctrl+A** / *Select all shown* (all rows after Find / Show).
  2. In the bar above the grid, choose the values to set: **Host Type**, **Level**, **Elevation (mm)**, **Facing** and/or **Category**. Fields left at *(keep)*, or an empty elevation, are not changed.
  3. Click **Apply to selected rows**.

  Example: *Show: Electrical* → *Select all shown* → Host Type = *Reference Plane (auto-create)*, Elevation = 2800 → *Apply*.
- **Use reference planes for all rows** (checkbox above the grid) sets every row's Host Type to *Reference Plane (auto-create)* in one click. Untick it to restore the previous Host Types.
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

- While placing, a **progress bar** shows the block count and the time left. **Cancel** stops and rolls everything back; nothing is changed, and you return to the mapping window.
- The result window shows placed counts per family type, unmapped blocks, and failed/skipped blocks grouped by reason. Use **Open log** / **Log folder** to jump to the CSV log.
- At the bottom, a **Timings** table shows where the time went: DWG reading, host detection (index build, per block, ray fallback), duplicate check, family creation (single and batched), rotation, planes, parameters, and commit. If a run is slow, this table shows which phase to look at.
- A `smarthost_log_<date>.csv` (or `smarthost_preview_<date>.csv`) is saved in `Documents\\SmartHostMEP\\Logs\\<project>\\`. It has one row per block with:
  `Status, CAD_Block, Family, Type, Level, ElementId, Host, X_mm, Y_mm, Z_mm, Rotation_deg, Block_Scale, Mirrored, Message`.
  Coordinates are Revit internal coordinates in mm. To find an element, copy its ElementId into *Manage > Select by ID*.
- Each placed element's **Comments** parameter contains `CAD: <block name>`. You can use it in schedules and filters, e.g. to select everything the tool placed.
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

Click **SmartHost MEP > Tools > Settings** to open it in Notepad. Settings are stored in **`%AppData%\SmartHostMEP\settings.ini`**, which is created on the first run. Open it in Notepad and change the values; the next command you run uses them. There is no need to restart Revit.

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
| `WriteBlockNameToComments` | true | Write `CAD: <block>` into Comments. |
| `DebugHosting` | false | `true`: each block's log *Message* gets a `DEBUG` line: linked yes/no, link name, host element id and category, face normal, and the final *Host* of the placed instance. Example: `DEBUG linked=yes link=STR.rvt element=412233 (Floors) normal=(0.000,0.000,-1.000); host=Revit link STR.rvt, host face ok`. Existing `settings.ini` files don't have this line; add `DebugHosting = true` yourself. |
| `CenterGeometryOnCadPoint` | false | Vertical planes. `false`: the family's origin (insertion point) sits on the CAD point. `true`: the family's geometry (bounding box) is centred on the point along the plane instead. Replaces `CenterFamiliesOnCadPoint` (0.16–0.20), which is now ignored. |
| `LastMappingPath` | | Remembered automatically. |


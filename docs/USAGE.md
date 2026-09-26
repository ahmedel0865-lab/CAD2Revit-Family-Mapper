# Usage Guide

## 0. Install (once)

CAD2Revit is a **standalone Revit add-in**. It does not need pyRevit or any other add-in.

1. Download `CAD2Revit-<version>.zip`: from the repository's **Releases** page, from the **Actions** tab (latest build > *Artifacts*), or from whoever sent it to you.
2. **Close Revit**, unzip the file anywhere, and double-click **`Install.bat`**.
   - It installs the add-in for every Revit **2022 / 2023 / 2024 / 2025 / 2026** found on the PC.
   - Per Windows user, so no administrator rights are needed. Files go to `%AppData%\Autodesk\Revit\Addins\<version>\`.
3. Start Revit. When Revit asks about loading the unsigned add-in *CAD2Revit Family Mapper*, click **Always Load**.
4. A **CAD2Revit** tab appears with two panels: **Mapper** (**List Blocks**, **Place Families**) and **Tools** (**Settings**, **Help**).

- **Manual install** (if IT policy blocks `Install.bat`): copy `<version>\CAD2Revit.addin` and the folder `<version>\CAD2Revit` into `%AppData%\Autodesk\Revit\Addins\<version>\`. Then right-click `CAD2Revit\CAD2Revit.dll` > *Properties* > tick **Unblock**.
- **Update:** close Revit and run `Install.bat` from the new zip.
- **Uninstall:** close Revit and run `Uninstall.bat`.

> A pyRevit version of the same tool is also kept in this repository (`CAD2Revit.extension/`). It is optional; see the README.

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

1. Click **CAD2Revit > Place Families**.
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
| Host Type | None (level-based), Ceiling, Wall, Reference Plane (auto-create), Face (ceiling/slab/roof) or Vertical plane (no wall). See below. |
| Facing | Down (default) or Up. Which side a family on a **Reference Plane** faces: Down for ceiling devices (lights, detectors), Up for floor devices (floor boxes). |

- The dropdown lists loaded family types in the electrical categories: Lighting Fixtures, Lighting Devices (switches), Electrical Fixtures, Electrical Equipment, Fire Alarm Devices, Communication Devices, Data Devices, Security Devices, Nurse Call Devices and Telephone Devices. A family from another category is added to the list automatically when a loaded mapping file uses it.
- **Auto-select:** when a block name closely matches a family type, that family is pre-selected (e.g. `SMOKE-DET` → *Smoke Detector*, `SKT-DOUBLE` → *Duplex Receptacle*, `MCP` → *Manual Call Point*). Always check the pre-selections. **Auto-match** re-runs the matching for rows that are still `(Skip)`.
- **Remembered per project:** the grid is saved automatically when you click Preview or Run. The next time you open the mapping window in the same project, it is pre-filled. The file is `%AppData%\CAD2Revit\projects\<project>_<id>.xlsx`, in the normal mapping format.
- **Load Mapping... / Save Mapping...** read and write the normal mapping file (XLSX or CSV, format below), e.g. to reuse one mapping across projects or share it with the team. Loading only changes the rows whose block names are in the file.

4. Click **Preview**. The tool runs the full placement, including host searches, and then **undoes it**. The result window shows exactly what *Run* would do: counts per family type, unmapped blocks, failures with reasons. **Close the result window to return to the mapping window** with your choices kept, adjust, and preview again.
5. Click **Run**. Everything is placed in **one transaction** named *CAD2Revit: Place families*. A single **Ctrl+Z** removes all of it.

Host types:

| Host_Type | What it does |
|---|---|
| `non-hosted` = **None (level-based)** | Placed on the level at the elevation, rotated like the CAD block. |
| `ceiling` | Casts a ray straight up from the block and hosts on the first **ceiling** face (this model or linked models), up to the next level or 6 m. |
| `face` | Same, but also hosts on floor/roof undersides and beams (useful where there is no ceiling, e.g. car parks, plant rooms). |
| `wall` | Looks for the nearest **wall** face within 500 mm of the block, at the elevation height, and places the device on that face facing into the room. **If no wall is found**, a face-based device is stood upright on a vertical plane instead (as `vertical` below). |
| `reference plane` = **Reference Plane (auto-create)** | Creates (or reuses) a **horizontal reference plane** at *level elevation + Elevation From Level*, named `CAD2Revit_<Level>_+<elevation>mm` (e.g. `CAD2Revit_Level 1_+2800mm`; up-facing planes end in `_Up`). The plane covers the DWG link's extents plus 1 m. The family is hosted on it, with the CAD block rotation as its direction and facing Down or Up (the **Facing** column). All rows with the same elevation and facing share one plane, and later runs reuse it. Only face-based / work-plane-based families can be hosted this way; others are placed level-based with a warning. |
| `vertical` = **Vertical plane (no wall)** | **No wall needed.** Places a face-based device upright on a vertical work plane through the CAD point, at the elevation height. Use it for sockets, switches, call points and so on when the model has no Revit walls (only the DWG background). The device faces the CAD block's local **+Y** direction (a block drawn with the wall along X and the room on +Y faces into the room). If devices face the wrong way, set **Rotation** to 180 (or ±90). |

The elevation is also the fallback height if a ceiling is not found.

- **Edit many rows at once:**
  1. Select rows with **Ctrl+click**, **Shift+click**, or **Ctrl+A** / *Select all shown* (all rows after Find / Show).
  2. In the bar above the grid, choose the values to set: **Host Type**, **Level**, **Elevation (mm)**, **Facing** and/or **Category**. Fields left at *(keep)*, or an empty elevation, are not changed.
  3. Click **Apply to selected rows**.

  Example: *Show: Electrical* → *Select all shown* → Host Type = *Reference Plane (auto-create)*, Elevation = 2800 → *Apply*.
- **Use reference planes for all rows** (checkbox above the grid) sets every row's Host Type to *Reference Plane (auto-create)* in one click. Untick it to restore the previous Host Types.
- For choosing between **Ceiling** and **Reference Plane**, see the table in the [README](../README.md#ceiling-vs-reference-plane-which-host-to-use).

## 4. Mapping file format (Load / Save, List Blocks template)

**List Blocks** lists every block name with counts (and mirrored counts), and **Export template...** saves a mapping file you can fill in Excel. The mapping window reads and writes the same format:

| Column | Example | Notes |
|---|---|---|
| CAD_Block_Name | `SMOKE-DET` | Block name (case-insensitive). |
| Revit_Family_Name | `Smoke Detector` | Family name exactly as loaded in the project. **Empty = (Skip).** |
| Revit_Type_Name | `Ceiling` | Type name exactly as in the project. |
| Level | `Level 2` | Optional. Level name; empty = the level picked when running. |
| Offset_From_Level_mm | `2800` | Elevation from level. |
| Rotation_Adjustment_deg | `90` | Rotation adjustment. |
| Host_Type | `ceiling` | `non-hosted`, `ceiling`, `wall`, `reference plane`, `face` or `vertical` (the window's labels are accepted too). |
| Facing | `Down` | Optional. `Down` (default) or `Up`, for reference-plane rows. |
| Category | `Electrical` | Optional. Electrical / Mechanical / Plumbing / Architectural / Structural / Annotation / Other; empty = detected from the name. |

Header spelling is flexible (`Offset_From_Level (mm)`, `offset from level mm`, ... all work). CSV files saved with `;` as separator (European/Middle-East Excel locale) are also accepted. In an .xlsx file, the sheet named **Mapping** is used, or the first sheet if there is none with that name. A full example is in [`templates/mapping_template.xlsx`](../templates/mapping_template.xlsx).

## 5. Check the results

- The result window shows placed counts per family type, unmapped blocks, and failed/skipped blocks grouped by reason. Use **Open log** / **Log folder** to jump to the CSV log.
- A `cad2revit_log_<date>.csv` (or `cad2revit_preview_<date>.csv`) is saved in `Documents\\CAD2Revit\\Logs\\<project>\\`. It has one row per block with:
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

## 6. Settings

Click **CAD2Revit > Tools > Settings** to open it in Notepad. Settings are stored in **`%AppData%\CAD2Revit\settings.ini`**, which is created on the first run. Open it in Notepad and change the values; the next command you run uses them. There is no need to restart Revit.

| Setting | Default | Meaning |
|---|---|---|
| `DuplicateToleranceMm` | 50 | Plan distance within which an existing instance counts as a duplicate. |
| `DuplicateSameTypeOnly` | false | `false`: any type of the same family is a duplicate. `true`: only the same type. |
| `IncludeNestedBlocks` | false | Default for the "nested blocks" checkbox. |
| `SimplifyBlockNames` | true | Group block names: remove the `.dwg.` file prefix and, for Revit-exported DWGs, the `-<id>-<view>` suffix. |
| `HostSearchDistanceMm` | 6000 | Max search distance up to a ceiling/soffit (never past the next level). |
| `WallSearchDistanceMm` | 500 | Max distance from the CAD point to a wall face. |
| `SearchRevitLinks` | true | Also host on faces in linked Revit models. |
| `FallbackToUnhosted` | true | If no host is found, place unhosted at the row offset (`true`), or report as failed (`false`). |
| `WriteBlockNameToComments` | true | Write `CAD: <block>` into Comments. |
| `LastMappingPath` | | Remembered automatically. |

(The pyRevit version uses the same settings in `CAD2Revit.extension/lib/cad2revit/config.py`.)

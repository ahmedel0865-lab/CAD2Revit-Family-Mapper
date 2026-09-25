# Usage Guide

## 0. Install (once)

CAD2Revit is a **standalone Revit add-in**. It does not need pyRevit or any other add-in.

1. Download `CAD2Revit-<version>.zip`: from the repository's **Releases** page, from the **Actions** tab (latest build > *Artifacts*), or from whoever sent it to you.
2. **Close Revit**, unzip the file anywhere, and double-click **`Install.bat`**.
   - It installs the add-in for every Revit **2022 / 2023 / 2024 / 2025 / 2026** found on the PC.
   - Per Windows user, so no administrator rights are needed. Files go to `%AppData%\Autodesk\Revit\Addins\<version>\`.
3. Start Revit. When Revit asks about loading the unsigned add-in *CAD2Revit Family Mapper*, click **Always Load**.
4. A **CAD2Revit** tab appears with a **Mapper** panel containing **List Blocks** and **Place Families**.

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

## 3. List the blocks and build the mapping

1. Click **CAD2Revit > List Blocks** and pick the DWG. A window lists every block name, how many times it appears, and how many of those are mirrored.
2. Click **Export template...** and save it as **.xlsx** (or .csv).
3. Fill in the template in Excel:

| Column | Example | Notes |
|---|---|---|
| CAD_Block_Name | `SMOKE-DET` | Exact name from List Blocks (case-insensitive). |
| Revit_Family_Name | `Smoke Detector` | Family name exactly as loaded in the project. **Leave empty to ignore this block.** |
| Revit_Type_Name | `Ceiling` | Type name exactly as in the project. |
| Offset_From_Level_mm | `2800` | Height above the target level. Used for non-hosted and wall-hosted placement, and as the fallback height if a ceiling is not found. |
| Rotation_Adjustment_deg | `90` | Added to the CAD block rotation (counter-clockwise). Use it when the family and the CAD symbol are drawn facing different directions. |
| Host_Type | `ceiling` | `ceiling`, `face`, `wall` or `non-hosted` (see below). |

Host types:

| Host_Type | What it does |
|---|---|
| `non-hosted` (or `none`, blank) | Placed on the level at `Offset_From_Level_mm`, rotated like the CAD block. |
| `ceiling` | Casts a ray straight up from the block and hosts on the first **ceiling** face (this model or linked models), up to the next level or 6 m. |
| `face` | Same, but also hosts on floor/roof undersides and beams (useful where there is no ceiling, e.g. car parks, plant rooms). |
| `wall` | Looks for the nearest **wall** face within 500 mm of the block, at `Offset_From_Level_mm` height, and places the device on that face facing into the room. |

Header spelling is flexible (`Offset_From_Level (mm)`, `offset from level mm`, ... all work). CSV files saved with `;` as separator (European/Middle-East Excel locale) are also accepted. In an .xlsx file, the sheet named **Mapping** is used, or the first sheet if there is none with that name.

A full example is in [`templates/mapping_template.xlsx`](../templates/mapping_template.xlsx) / [`.csv`](../templates/mapping_template.csv).

## 4. Preview, then run

1. Click **CAD2Revit > Place Families**. One dialog asks for:
   - the DWG link/import,
   - the mapping file (the last one used is remembered),
   - the target level (defaults to the level of the active plan view),
   - whether to include nested blocks.
2. Click **Preview**. The tool runs the full placement, including host searches, and then **undoes it**. The result window shows exactly what *Run* would do: counts per family type, unmapped blocks, failures with reasons. A `cad2revit_preview_<date>.csv` is written next to the mapping file.
3. Fix the mapping and repeat until the preview looks right.
4. Click **Place Families > Run**. Everything is placed in **one transaction** named *CAD2Revit: Place families*. A single **Ctrl+Z** removes all of it.

## 5. Check the results

- The result window shows placed counts per family type, unmapped blocks, and failed/skipped blocks grouped by reason. Use **Open log** / **Log folder** to jump to the CSV log.
- A `cad2revit_log_<date>.csv` is saved next to the mapping file (or in *Documents* if that folder is read-only). It has one row per block with:
  `Status, CAD_Block, Family, Type, ElementId, Host, X_mm, Y_mm, Z_mm, Rotation_deg, Block_Scale, Mirrored, Message`.
  Coordinates are Revit internal coordinates in mm. To find an element, copy its ElementId into *Manage > Select by ID*.
- Each placed element's **Comments** parameter contains `CAD: <block name>`. You can use it in schedules and filters, e.g. to select everything the tool placed.
- Running the tool again skips blocks that already have an instance of the same family within 50 mm on the same level (status `duplicate`). So after adding blocks to the DWG, re-running only adds the new ones.

Statuses in the log:

| Status | Meaning |
|---|---|
| `placed` | Created (in a preview: would be created). |
| `duplicate` | An instance of the same family already exists there, so the block was skipped. |
| `unmapped` | The block name is not in the mapping file (or its family cell is empty). |
| `skipped` | Mapped, but the family/type is not loaded in the project. |
| `failed` | Revit refused the placement, or no host was found and fallback is off. The message says why. |

## 6. Settings

Settings are stored in **`%AppData%\CAD2Revit\settings.ini`**, which is created on the first run. Open it in Notepad and change the values; the next command you run uses them. There is no need to restart Revit.

| Setting | Default | Meaning |
|---|---|---|
| `DuplicateToleranceMm` | 50 | Plan distance within which an existing instance counts as a duplicate. |
| `DuplicateSameTypeOnly` | false | `false`: any type of the same family is a duplicate. `true`: only the same type. |
| `IncludeNestedBlocks` | false | Default for the "nested blocks" checkbox. |
| `HostSearchDistanceMm` | 6000 | Max search distance up to a ceiling/soffit (never past the next level). |
| `WallSearchDistanceMm` | 500 | Max distance from the CAD point to a wall face. |
| `SearchRevitLinks` | true | Also host on faces in linked Revit models. |
| `FallbackToUnhosted` | true | If no host is found, place unhosted at the row offset (`true`), or report as failed (`false`). |
| `WriteBlockNameToComments` | true | Write `CAD: <block>` into Comments. |
| `LastMappingPath` | | Remembered automatically. |

(The pyRevit version uses the same settings in `CAD2Revit.extension/lib/cad2revit/config.py`.)

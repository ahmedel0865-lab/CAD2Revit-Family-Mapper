# SmartHost MEP

Smart Revit add-in that converts AutoCAD blocks into hosted MEP families (slab, ceiling, wall, beam, reference plane) with automatic host detection.

> Formerly **CAD2Revit Family Mapper**. Installing SmartHost MEP removes the old add-in, and models made with it keep working (its reference planes and Comments are still recognised).

[![Build](https://github.com/ahmedel0865-lab/SmartHost-MEP/actions/workflows/build.yml/badge.svg)](https://github.com/ahmedel0865-lab/SmartHost-MEP/actions/workflows/build.yml)
![Revit 2022-2026](https://img.shields.io/badge/Revit-2022%E2%80%932026-2060B0)
![License MIT](https://img.shields.io/badge/license-MIT-green)

**Turn the blocks in a linked DWG into real Revit families, in one window.**
Built for electrical / MEP shop drawings (lighting, power, fire alarm, ELV). It's a standalone Revit add-in, so no pyRevit, Dynamo or Excel is needed.

Instead of placing hundreds of light fixtures, sockets and detectors by hand over a CAD background, you map each CAD block to a Revit family type once. The tool places every instance at the right location, rotation, level and height, hosted on ceilings, walls or reference planes as you choose.

```mermaid
flowchart LR
    A[Link the DWG] --> B[Place Families]
    B --> C[Mapping window<br/>DWG · Level · Host default<br/>one row per block: family · host · elevation]
    C --> D[Preview<br/>nothing is changed]
    D -->|adjust| C
    D --> E[Run<br/>one Ctrl+Z undoes it]
    E --> F[Summary + CSV log]
```

## Contents
- [The SmartHost MEP ribbon](#the-smarthost-mep-ribbon)
- [Features](#features)
- [Requirements](#requirements) · [Installation](#installation) · [Quick start](#quick-start)
- [Ceiling vs Slab vs Reference Plane](#ceiling-vs-slab-vs-reference-plane-which-host-to-use)
- [Mapping file](#mapping-file) · [Project structure](#project-structure) · [Building from source](#building-from-source)
- [Performance](#performance) · [Limitations](#limitations-short) · [Roadmap](#roadmap)
- Guides: [Usage](docs/USAGE.md) · [Testing on a sample](docs/TESTING.md) · [Limitations](docs/LIMITATIONS.md) · [Changelog](CHANGELOG.md)

## The SmartHost MEP ribbon

| Panel | Button | What it does |
|---|---|---|
| Host Placement | **Place Families** | Pick the DWG, then map, preview and place everything in the mapping window. |
| Host Placement | **Fix Host Line** | Click a placed element, then the DWG line it should sit on: it moves onto a vertical plane on that line. Option: every element of the same block on that line. |
| Tools | **Settings** | Opens `settings.ini` (tolerances, host search distances, fallbacks) in Notepad. |
| Tools | **Help** | Version, user guide, and quick links to the logs and the saved project mappings. |

## Features

**Mapping window**
- **One window, top to bottom**: header (DWG, level, block types and instances), setup row (**DWG**, **Level**, **Host default**), search box with **All / Mapped / Unmapped** chips, the grid, a live summary (*12 mapped, 6 skipped, 590 elements will be placed*), a collapsible **Advanced** section and the footer (Load / Save; **Preview**, **Run**, Cancel). Preview and Run show their progress bar and Cancel inside the window.
- **Follows Revit's light or dark theme** (Revit 2024+). Remembers its size, position, column widths, shown columns and whether Advanced is open.
- **2D symbol preview** (*Columns > Symbol preview panel*): hover over or select a block to see its CAD symbol, size and count. Hovering the block name shows it as a tooltip too.
- One row per unique CAD block (e.g. `SMOKE-DET (42)`), **grouped by category**: Electrical, Mechanical, Plumbing, Architectural, Structural, Annotation, Other.
- **Search** (Ctrl+F) and **All / Mapped / Unmapped** filter chips. Skipped rows are greyed out.
- Grid columns: **Block** (name and count), **Revit Family : Type**, **Host**, **Elevation (mm)**. Rotation, Facing, Level, Category, Place At, Score, Source View, Top 3 Candidates and Detected Host are under **Columns**.
- **Inline validation**: an invalid elevation or rotation gets a red cell with the reason as tooltip; Preview and Run stay disabled until it is fixed.
- Keys: **Enter** = Preview, **Ctrl+Enter** = Run, **Esc** = Cancel, **Ctrl+F** = search, **Del** = skip the selected rows.
- **Searchable family dropdown** (electrical categories): type part of a name to filter.
- **Revit Family starts at `(Skip)`** every time the tool opens, so you pick the families for this run yourself. **Clear All Families** sets every row back to `(Skip)`.
- **Auto-match by symbol**: compares each block's 2D symbol with the symbol of every loaded family (its plan view, *Front* or *Back* view, or the outline of its 3D geometry), at any size, rotated or mirrored, and selects the family scoring 80 or more. The grid shows the **Score**, the **Source View** and the **Top 3 Candidates** to choose from. Close calls are selected and listed in Needs Review as *Ambiguous symbol match*. Families you pick by hand are remembered and used first next time. Only loadable families are opened, and their symbols are cached in `%TEMP%\SmartHostMEP\SymbolCache`. Blocks without line work are still matched by name (`SMOKE-DET` → *Smoke Detector*).
- **Level + elevation per row**: different blocks can go on different levels in one run.
- **Edit many rows at once**: select rows with **Ctrl+click** (add/remove one row), **Shift+click** (a range) or **Ctrl+A**. A bar appears above the grid to set **Family**, **Host** and **Elevation** on all of them, or change a cell in one selected row to change them all. Right-click: *Apply this row to selected*, *Skip selected*, *Copy settings from this row*, *Paste settings to selected*.
- Groups the per-instance block names of **DWGs exported from Revit** (`Family - Type-<id>-<view>`) into one row per type.
- **Remembers per project** Elevation From Level, Host Type, Rotation, Facing, Level and the Slab (above) / Ceiling search range and fallback height, but not the families. Load / Save mappings as **.xlsx or .csv**; **Load...** is the only way families are filled in from a file.

**Placement and hosting**
- Places families at the block insertion points with the CAD rotation (plus a per-row adjustment). DWG units, link position, rotation and shared coordinates are handled automatically.
- Hosts per row: **None (level-based)**, **Ceiling**, **Slab (above)**, **Slab (below)**, **Wall**, **Reference Plane (auto-create)**, **Face** (ceilings/slabs/roofs/beams) or **Vertical plane** (no wall needed), including hosts in linked Revit models.
- **Wall** hosting puts the family on the wall face **on the side the CAD block is drawn**, at the CAD point projected onto that face, flat on the wall and facing out towards the block. The CAD rotation is ignored; the wall direction is used, including on curved walls. The nearest wall within the **Wall search distance** (500 mm, set in the window) at the row's height is used, in this model and in links. Blocks with no wall in range, moved more than 200 mm to reach the face, or on a wall in a link are listed in **Needs Review**.
- **Vertical plane** placement. Each block is checked in this order:
  1. A **Revit wall or column** face (this model or a link) within the *Wall/column search* (600 mm), at the device height. The closest one that the device sits along is used: the block point projects onto it, not past its end or into a door opening. Block rotation and symbol shape play no part.
  2. Otherwise, the **best-scoring DWG line** near the block, on **any layer**. Candidates are straight lines longer than 300 mm within the *DWG line search* (300 mm, set in the window) of the block **outline** (the symbol's bounding box, not just the insertion point). Left out: the block's own geometry and other mapped or electrical blocks, wires and tails (lines that start or end at the outline), dimensions, hatch lines and arcs such as door swings. Each candidate gets a score:
     - **touch**: the line touches the outline, or passes within 20 mm of the insertion point (biggest bonus);
     - **distance**: closer to the outline scores more;
     - **length**: longer lines score more, capped at 3000 mm;
     - **continuity**: other blocks touching the same line (within 5 mm and 0.5°) add to the score.

     The weights are in `settings.ini` (`LineScoreTouch`, `LineScoreDistance`, `LineScoreLength`, `LineScoreContinuity`, `LineScoreContinuityMax`, `LineScoreAmbiguousPercent`). The highest score wins. If the next best line scores within 10 %, the best one is still used and the element is listed in **Needs Review** as "Ambiguous wall line".
  3. Otherwise, the **block rotation**, listed in Needs Review as "No wall line found".
  - **The plane** is on the chosen line and runs along it. The block point is projected onto it, and the family faces away from the line, toward the block side.
  - **Position** (set in the window): **Snap to face** (default) puts the device on the face, at the block point projected onto it. **Through block point** keeps the block point. A snap of more than 200 mm is listed in Needs Review.
  - Planes are named `SmartHost_V_<Level>_<n>`, drawn from the level up to level + 3000 mm. Elements on the same line share one plane (within 5 mm and 0.5°). Walls, columns and DWG lines are read once and indexed in a grid, all in the run's single transaction.
  - **Detected Host** (Preview) says where each plane came from: `Vertical plane: Revit wall (linked)`, `Touching line (layer: A-WALL)`, `Nearest line (score 74)` or `Block rotation`.
  - **Show detection** (checkbox in the window): after Preview or Run, the line chosen for each block is drawn as magenta detail lines in the plan view (line style `SmartHost Detection`), with a short line from the block to where it went. The next Preview or Run removes them.
  - **Fix Host Line** (ribbon): click a placed element, then the DWG line it should sit on. The element moves onto a plane on that line (an existing one is reused), facing away from the line toward its CAD block, and its Needs Review mark in Comments is removed. Tick *Apply to all elements of the same block on this line* to move the others too. Repeat, then Esc to finish; Ctrl+Z undoes one fix.
  - The family goes on the block point at level + elevation, facing the block. If it lands more than 10 mm off, it is moved back. If it still can't be corrected, it is listed in **Needs Review**.
- **Place At** (per row): **Symbol centre** (the centre of the drawn symbol, default) or **Base point** (the block's insertion point). Symbol centre suits blocks drawn away from their base point, for example a wall light whose base point is on the wall line and whose circle is in the room. Pick Base point when the insertion point is the exact location. With Base point, blocks whose base point is far from the symbol are flagged in **Needs Review** ("Block base point is N mm from its symbol"). **Mirrored blocks** face the side their symbol is drawn on.
- **Final distance check, all host types**: any element placed farther than the **review distance** (50 mm, set in the window) from its **intended point** is listed in **Needs Review** with its Element ID. The intended point is the CAD block, or the face point it was snapped to.
- **Slab (above)**: hosts on the **bottom face of the nearest slab or beam above** each block, facing down, keeping the CAD rotation (sloped slabs included). Floors, roofs (sloped slabs are often modelled as roofs) and Structural Framing (beams) count, in this model and in linked models; ceilings and ducts are ignored. Whichever bottom face is closest above the point wins, so a drop beam under the slab is chosen over the slab. A beam is only used where the point is under its bottom face; otherwise the next host up is used. The search goes from the row's level up to the **Slab search range** (default 5000 mm), never higher, even if the level above is higher. **No slab or beam in range** (open area, slab opening, missing structural link): the family goes on one reference plane per level, `SmartHost_<Level>_+3000mm`, facing down, at the **Fallback reference plane height** (default 3000 mm). Level-based families are placed level-based at that Elevation From Level. Both values are at the top of the mapping window and are saved with the mapping. A level-based family under a slab or beam is placed level-based at its underside height.
- **Ceiling**: works the same way as Slab (above) and uses the same search code. It hosts on the **bottom face of the nearest ceiling above** each block, in this model and in linked (architectural) models, within the same search range. With no ceiling in range (no ceiling there, an opening, a missing architectural link), it uses the same fallback plane `SmartHost_<Level>_+3000mm`, or level-based at that height, logged as `No ceiling within 5000 mm - placed on reference plane at +3000 mm`. The two values at the top of the mapping window (*Search range* and *Fallback reference plane height*) apply to both Slab (above) and Ceiling.
- **Counts**: Preview and the result window show how many Slab (above) blocks were hosted on a slab, hosted on a beam, put on the reference plane, or placed level-based, and how many Ceiling blocks were hosted on a ceiling, put on the reference plane, or placed level-based.
- **Needs Review** window: after a run, every Slab (above) and Ceiling element that went on the fallback plane (or level-based fallback) is listed with Element ID, Family : Type, CAD Block, X, Y (mm) and Reason. Clicking a row selects the element in Revit and zooms to it. **Select All in Revit**, **Copy IDs** (comma-separated, for Manage > Select by ID) and **Export to Excel** are there too. **Slab (below)**: hosts on the **top of the slab** at the level, facing up (floor boxes). Where a block sits over a slab opening, the family goes on a reference plane at the slab's height, with a warning.
- **Detected Host (Preview)** column: after Preview, every row shows the host it would use, e.g. `Floor: 250mm RC Slab - Third Floor (linked: STR.rvt)`.
- **Reference Plane (auto-create)**: named horizontal planes at level + elevation (e.g. `SmartHost_Level 1_+2800mm`), shared per elevation, facing Down or Up.
- If no host is found, falls back to unhosted placement (or reports a failure, your choice).

**Safety and output**
- **Preview** runs the full placement and undoes it, so its counts match a real run.
- **Duplicate protection** per level: before a Run, if elements are already at the block locations (same family, or Comments written by an earlier run), the tool asks *"X elements already exist at these locations"*: **Skip them** (default), **Place anyway** or **Cancel**.
- **One transaction**: a single Ctrl+Z undoes a whole run.
- **Fast on big drawings**: hosts are found from face/wall indexes built once (not one ray per block), and level-based families are created in batches. A progress bar with **Cancel** is shown (Cancel rolls everything back), and a **Timings** table shows where the time went. See [Performance](#performance).
- Summary per family type, unmapped blocks and failures with reasons; a **CSV log** of every block with Element ID, level, host, coordinates and rotation.
- Leaves the **Comments** parameter empty. To have the block name written there (`SmartHost: <block>`), set `WriteComments = true` in settings.ini.

## Requirements
- Autodesk Revit **2022, 2023, 2024, 2025 or 2026** on Windows
- Nothing else: no pyRevit, no Dynamo, no Excel

## Installation
1. Download **`SmartHostMEP-<version>.zip`** from the [download](download/) folder, the [Releases](https://github.com/ahmedel0865-lab/SmartHost-MEP/releases) page, or the latest run in the [Actions](https://github.com/ahmedel0865-lab/SmartHost-MEP/actions) tab (*Artifacts*).
2. Close Revit, unzip, and double-click **`Install.bat`**. It installs for every Revit 2022–2026 on the PC, for the current user; no admin rights are needed.
3. Start Revit and click **Always Load** when asked about the add-in. A **SmartHost MEP** tab appears.

Uninstall: `Uninstall.bat`. Manual install and details: [docs/USAGE.md](docs/USAGE.md#0-install-once).

## Quick start
1. Load your families (face-based for hosted devices) and link the DWG in the target floor plan.
2. **SmartHost MEP > Place Families**. The mapping window opens straight away with a DWG already chosen: the one selected in Revit, else the one used last time. Change it with the **DWG** dropdown or **Pick in view**, and set the **Level** and **Host default** on the same row.
3. In the mapping window, pick a family for each CAD block (type to search; click **Auto-match** to pre-select families with the same symbol, or **Load...** a saved mapping), set the level, elevation and Host Type (see [Ceiling vs Slab vs Reference Plane](#ceiling-vs-slab-vs-reference-plane-which-host-to-use)). To set many rows at once, select them (Ctrl/Shift+click, Ctrl+A) and use the bar that appears above the grid, or right-click. Leave `(Skip)` for blocks you don't want.
4. **Preview** (Enter) > check the result and the *Detected Host* column > **Run** (Ctrl+Enter). One Ctrl+Z undoes it all.

<!-- Screenshots: replace the placeholders below with real captures (docs/images/). -->
| Mapping window (light) | Mapping window (dark) |
|---|---|
| ![Mapping window, light theme](docs/images/mapping-window-light.png) | ![Mapping window, dark theme](docs/images/mapping-window-dark.png) |
| ![Bulk edit of selected rows](docs/images/mapping-window-bulk-edit.png) | ![Result and Needs Review](docs/images/result-needs-review.png) |

Next time in the same project, the mapping window remembers elevations, host types, rotation, facing and levels. Every Revit Family starts at `(Skip)`, so you only re-pick the families (or use **Load...**).

- Full guide: [docs/USAGE.md](docs/USAGE.md)
- Test on a small sample first: [docs/TESTING.md](docs/TESTING.md)
- Known limitations: [docs/LIMITATIONS.md](docs/LIMITATIONS.md)
- Example mapping: [templates/mapping_template.xlsx](templates/mapping_template.xlsx) / [.csv](templates/mapping_template.csv)

## Ceiling vs Slab vs Reference Plane: which host to use?

**Slab (above)** fits when devices hang from the **structural slab soffit**: exposed or open ceilings, car parks, plant rooms, or wherever the ceilings aren't modelled yet but the structural model is linked. Devices follow the slab: if the structure changes, re-running finds the new soffit. **Slab (below)** is the same for floor boxes and floor sockets on top of the slab. The table below compares Ceiling and Reference Plane.

| Use **Ceiling** when... | Use **Reference Plane (auto-create)** when... |
|---|---|
| The model (or a linked architectural model) **has ceilings** at the right height. | There are **no ceilings** yet, or they are in a model you can't host on (e.g. a DWG background only). |
| You want devices to **follow the ceiling**: if the architect moves the ceiling, hosted lights and detectors move with it. | You want a **fixed height** from the level that you control, e.g. 2800 mm for all lights on Level 1, regardless of the architecture. |
| Ceilings are at different heights in different rooms, and each device should sit on its own ceiling. | Many devices share one height and you want **one tidy plane per height** that you can move later (moving the plane moves every device on it). |
| | Devices go on the **underside of a slab** or in **open ceilings** (car parks, plant rooms), or **face up** on the floor (floor boxes: set *Facing* = Up). |

Notes:
- All three need **face-based** (or work-plane-based) families. A level-based family set to Reference Plane is placed level-based at the elevation, with a warning in the log and the result window. On Slab (above) and Ceiling rows it is placed level-based at the host's underside height, or at the fallback height when there is no host. On Face / Slab (below) rows it is not placed ("family is not face-based").
- For hosts in **linked models**, each placed instance is checked: *Host* must be the link, not a reference plane. Otherwise the block is reported as failed. Set `DebugHosting = true` in `settings.ini` to get a per-block DEBUG line (link, element, normal, final host) in the log.
- Ceiling hosting looks straight up from each block for a ceiling within the search range (default 5000 mm). Anything without a ceiling above it goes on the fallback reference plane (default +3000 mm) and is listed in **Needs Review**.
- Reference planes are named `SmartHost_<Level>_+<elevation>mm` (`..._Up` for up-facing ones). They cover the DWG extents and are reused by later runs. The planes created in a run are removed by the same Ctrl+Z.

## Mapping file
The mapping window can **Load / Save** the mapping as a file, to reuse it across projects or share it with the team. Empty family = Skip:

| CAD_Block_Name | Revit_Family_Name | Revit_Type_Name | Offset_From_Level_mm | Rotation_Adjustment_deg | Host_Type |
|---|---|---|---|---|---|
| LIGHT-600x600 | Recessed Panel Light | 600x600 40W | 2800 | 0 | ceiling |
| SOCKET-DOUBLE | Duplex Receptacle | Standard | 300 | 0 | wall |
| SMOKE-DET | Smoke Detector | Ceiling | 2800 | 0 | ceiling |
| DB-PANEL | Lighting and Appliance Panelboard | 400A | 1500 | 180 | non-hosted |
| FLOOR-BOX | Floor Box | Standard | 0 | 0 | slab below |

`Host_Type` values: `non-hosted`, `ceiling`, `slab above`, `slab below`, `wall`, `reference plane`, `face` or `vertical`. The window's labels are accepted too.

Saved mappings also carry two columns, `Slab_Search_Range_mm` and `Slab_Fallback_Plane_mm`, with the same value on every row (the Slab (above) values from the top of the mapping window). Files without them use 5000 and 3000.

## Project structure
```
addin/                            standalone Revit add-in (C#)  <- main product
  src/SmartHostMEP/                  Revit add-in
    App.cs                        ribbon: Host Placement (Place Families, Fix Host Line), Tools (Settings, Help)
    Commands/                     the ribbon commands
    Revit/                        DWG reader, host finder, reference/vertical planes, placer
    UI/                           mapping window (WPF), step-1 dialog, result window
  src/SmartHostMEP.Core/             Revit-free logic (unit tested): CSV/XLSX, mapping, name
                                  matching, block names & categories, report, settings
  tests/SmartHostMEP.Core.Tests/     unit tests (run without Revit)
  package/                        .addin manifest, Install.bat, install.ps1
  tools/package.sh                build all Revit versions + zip
templates/                        mapping_template.xlsx / .csv
docs/                             USAGE.md, TESTING.md, LIMITATIONS.md
```

## Building from source
Needs the .NET 8 SDK (Windows, macOS or Linux). Revit does not need to be installed; the Revit API reference assemblies come from the `Nice3point.Revit.Api` NuGet packages.
```
cd addin
dotnet test tests/SmartHostMEP.Core.Tests          # unit tests
dotnet build src/SmartHostMEP -c Release -p:RevitVersion=2024   # one Revit version
bash tools/package.sh                           # all versions -> dist/SmartHostMEP-<version>.zip
```
Or open `addin/SmartHostMEP.sln` in Visual Studio 2022. Pushing a tag like `v0.4.0` makes CI publish the zip as a GitHub Release.

## Performance

Every Preview/Run ends with a **Timings** table in the result window (also in the CSV log, rows with Status `timing`). It shows the time per phase: DWG reading, host detection, duplicate check, creation, rotation, parameters and commit.

What v0.12 changed, **for 1,000 blocks** (counts of Revit API work, from the code):

| Work | v0.11 | v0.12 |
|---|---|---|
| Slab / ceiling / face host detection | 1,000 ray casts (`ReferenceIntersector.Find`) | host geometry read **once** + 1,000 in-memory lookups |
| Wall host detection | up to **16,000** rays (16 per block) | location-line index lookup, then plan math on the 1–2 nearest walls (side, projection, tangent); face offsets computed once per wall |
| Level-based creation | 1,000 `NewFamilyInstance` + up to 1,000 `RotateElement` | **one** `NewFamilyInstances2` per mapping row and level, rotation included |
| Regenerations | 1 per reference-plane block, plus 1 per newly activated type | **1**, before the loop |
| Duplicate grid | rebuilt from all instances for every level used | built **once** |
| DWG geometry | Fine detail | Coarse detail |

Measured without Revit (`addin/src/SmartHostMEP.Core`, .NET 8, 1,000 random block points):
- face index over 400 slab bays with openings: built in about 5–8 ms, and 1,000 lookups take about 3 ms;
- duplicate grid of 20,000 existing instances: built in about 25–40 ms, and 1,000 queries take about 2 ms.

Wall-clock times inside Revit depend on the model and haven't been measured yet (Revit can't run in the build environment). Run a Preview on your drawing, and the Timings table gives the numbers for your project. Please share them so this section can list real before/after times.

## Limitations (short)
- **Block attributes** (circuit number, panel name, etc.) are not readable through the Revit API, so they are not copied yet.
- **Anonymous dynamic blocks** (`*U123`) and **exploded blocks** cannot be mapped.
- **Mirrored** blocks are placed with rotation only and flagged in the log.
- **Legacy (non face-based) hosted families** cannot host on linked models. Use face-based families.

See [docs/LIMITATIONS.md](docs/LIMITATIONS.md) for details and workarounds.

## Roadmap
- [x] Excel (.xlsx) mapping support
- [x] Wall-hosted placement (sockets, switches)
- [x] Standalone Revit add-in (no pyRevit)
- [x] In-Revit mapping window with searchable families and per-project memory
- [x] Slab soffit / slab top hosting, including structural links
- [ ] Copy block attributes via an AutoCAD Data Extraction CSV matched by location
- [ ] Auto-assign circuits / panel parameters
- [ ] Save and reuse mapping profiles per project
- [ ] Multiple DWGs / levels in one run

## Contributing
Issues and pull requests are welcome. Please test changes on a copy of a model and include the Revit version you tested with.

## License
[MIT](LICENSE)

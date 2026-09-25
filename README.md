# CAD2Revit Family Mapper

A **standalone Revit add-in** that converts AutoCAD blocks in a linked DWG into Revit family instances. It's built for MEP / electrical shop drawings (lighting, power, fire alarm, ELV). **pyRevit is not required.**

Instead of manually placing hundreds of light fixtures, sockets and detectors over a CAD background, you map each CAD block to a Revit family type once in an Excel/CSV file. The tool then places them all at the correct location, rotation and level, hosted on ceilings or walls where required.

## Features
- **Mapping window**: one row per unique CAD block (e.g. `SMOKE-DET (42)`), sorted by name, with a Find box, a **searchable** family dropdown (electrical categories), elevation, rotation and host type, all in Revit
- Groups the per-instance block names of **DWGs exported from Revit** (`Family - Type-<id>-<view>`) into one row per type
- **Auto-selects** families whose names closely match the block name (`SMOKE-DET` → *Smoke Detector*)
- **Remembers the last mapping per project**, so the grid is pre-filled next time
- Load / Save mappings as **.xlsx or .csv** (no Excel installation needed); List Blocks exports a ready-to-fill template
- Places families at block insertion points, keeping the CAD rotation (+ per-row adjustment)
- Handles DWG units, link position, rotation and shared coordinates automatically
- Hosting per row: **None (level-based)**, **Ceiling**, **Wall**, **Reference Plane (auto-create)**, **Face** (ceilings/slabs/roofs/beams) or **Vertical plane** (no wall needed), including hosts in linked Revit models
- **Reference Plane (auto-create)**: named horizontal planes at level + elevation (e.g. `CAD2Revit_Level 1_+2800mm`), shared per elevation, facing Down or Up, with a one-click *Use reference planes for all rows*
- Falls back to unhosted placement (or reports a failure) when no host is found
- **Preview** does the full placement and then undoes it, so its counts match a real run, then returns to the mapping window
- Duplicate protection per level, so re-running only adds new blocks
- One transaction: undo everything with a single Ctrl+Z
- Writes the source block name into each element's Comments parameter
- Summary per family type, unmapped blocks, failures with reasons
- CSV log of every block with its Element ID, host, coordinates and rotation

## Requirements
- Autodesk Revit **2022, 2023, 2024, 2025 or 2026** on Windows
- Nothing else: no pyRevit, no Dynamo, no Excel

## Installation
1. Download **`CAD2Revit-<version>.zip`** from the [download](download/) folder, the [Releases](https://github.com/ahmedel0865-lab/CAD2Revit-Family-Mapper/releases) page, or the latest run in the [Actions](https://github.com/ahmedel0865-lab/CAD2Revit-Family-Mapper/actions) tab (*Artifacts*).
2. Close Revit, unzip, and double-click **`Install.bat`**. It installs for every Revit 2022–2026 on the PC, for the current user; no admin rights are needed.
3. Start Revit and click **Always Load** when asked about the add-in. A **CAD2Revit** tab appears.

Uninstall: `Uninstall.bat`. Manual install and details: [docs/USAGE.md](docs/USAGE.md#0-install-once).

## Quick start
1. Load your families (face-based for hosted devices) and link the DWG in the target floor plan.
2. **CAD2Revit > Place Families** > pick the DWG and target level > **Next**.
3. In the mapping window, pick a family for each CAD block (type to search; close matches are pre-selected), set the elevation and Host Type (see [Ceiling vs Reference Plane](#ceiling-vs-reference-plane-which-host-to-use)), and leave `(Skip)` for blocks you don't want.
4. **Preview** > check the result > **Run**. One Ctrl+Z undoes it all.

Next time in the same project, the mapping window opens pre-filled.

- Full guide: [docs/USAGE.md](docs/USAGE.md)
- Test on a small sample first: [docs/TESTING.md](docs/TESTING.md)
- Known limitations: [docs/LIMITATIONS.md](docs/LIMITATIONS.md)
- Example mapping: [templates/mapping_template.xlsx](templates/mapping_template.xlsx) / [.csv](templates/mapping_template.csv)

## Ceiling vs Reference Plane: which host to use?

| Use **Ceiling** when... | Use **Reference Plane (auto-create)** when... |
|---|---|
| The model (or a linked architectural model) **has ceilings** at the right height. | There are **no ceilings** yet, or they are in a model you can't host on (e.g. a DWG background only). |
| You want devices to **follow the ceiling**: if the architect moves the ceiling, hosted lights and detectors move with it. | You want a **fixed height** from the level that you control, e.g. 2800 mm for all lights on Level 1, regardless of the architecture. |
| Ceilings are at different heights in different rooms, and each device should sit on its own ceiling. | Many devices share one height and you want **one tidy plane per height** that you can move later (moving the plane moves every device on it). |
| | Devices go on the **underside of a slab** or in **open ceilings** (car parks, plant rooms), or **face up** on the floor (floor boxes: set *Facing* = Up). |

Notes:
- Both need **face-based** (or work-plane-based) families. A level-based family set to Reference Plane is placed level-based at the elevation, with a warning in the log and the result window.
- Ceiling hosting looks straight up from each block and needs a ceiling within the search distance. Anything without a ceiling above it falls back to the row's elevation, unhosted.
- Reference planes are named `CAD2Revit_<Level>_+<elevation>mm` (`..._Up` for up-facing ones). They cover the DWG extents and are reused by later runs. The planes created in a run are removed by the same Ctrl+Z.

## Mapping file
The mapping window can **Load / Save** the mapping as a file, to reuse it across projects or share it with the team. The format is the same one List Blocks exports (empty family = Skip):

| CAD_Block_Name | Revit_Family_Name | Revit_Type_Name | Offset_From_Level_mm | Rotation_Adjustment_deg | Host_Type |
|---|---|---|---|---|---|
| LIGHT-600x600 | Recessed Panel Light | 600x600 40W | 2800 | 0 | ceiling |
| SOCKET-DOUBLE | Duplex Receptacle | Standard | 300 | 0 | wall |
| SMOKE-DET | Smoke Detector | Ceiling | 2800 | 0 | ceiling |
| DB-PANEL | Lighting and Appliance Panelboard | 400A | 1500 | 180 | non-hosted |

## Project structure
```
addin/                            standalone Revit add-in (C#)  <- main product
  src/CAD2Revit/                  Revit add-in: ribbon, commands, DWG reader,
                                  host finder, placer, dialogs, WPF mapping window
  src/CAD2Revit.Core/             Revit-free logic: CSV/XLSX, mapping, name matching,
                                  per-project memory, report, settings
  tests/CAD2Revit.Core.Tests/     unit tests (run without Revit)
  package/                        .addin manifest, Install.bat, install.ps1
  tools/package.sh                build all Revit versions + zip
CAD2Revit.extension/              optional pyRevit version of the same tool
templates/                        mapping_template.xlsx / .csv
docs/                             USAGE.md, TESTING.md, LIMITATIONS.md
tests/                            unit tests for the pyRevit version
```

## Building from source
Needs the .NET 8 SDK (Windows, macOS or Linux). Revit does not need to be installed; the Revit API reference assemblies come from the `Nice3point.Revit.Api` NuGet packages.
```
cd addin
dotnet test tests/CAD2Revit.Core.Tests          # unit tests
dotnet build src/CAD2Revit -c Release -p:RevitVersion=2024   # one Revit version
bash tools/package.sh                           # all versions -> dist/CAD2Revit-<version>.zip
```
Or open `addin/CAD2Revit.sln` in Visual Studio 2022. Pushing a tag like `v0.4.0` makes CI publish the zip as a GitHub Release.

## Optional: pyRevit version
If you already use pyRevit, the placement engine is also available as a pyRevit extension in `CAD2Revit.extension/`. It uses a mapping file instead of the mapping window.
```
pyrevit extend ui CAD2Revit https://github.com/ahmedel0865-lab/CAD2Revit-Family-Mapper.git
```
You only need one of the two. Don't install both, or you'll get two CAD2Revit tabs.

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
- [ ] Copy block attributes via an AutoCAD Data Extraction CSV matched by location
- [ ] Auto-assign circuits / panel parameters
- [ ] Save and reuse mapping profiles per project
- [ ] Multiple DWGs / levels in one run

## Contributing
Issues and pull requests are welcome. Please test changes on a copy of a model and include the Revit version you tested with.

## License
[MIT](LICENSE)

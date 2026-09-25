# CAD2Revit Family Mapper

A **standalone Revit add-in** that converts AutoCAD blocks in a linked DWG into Revit family instances. It's built for MEP / electrical shop drawings (lighting, power, fire alarm, ELV). **pyRevit is not required.**

Instead of manually placing hundreds of light fixtures, sockets and detectors over a CAD background, you map each CAD block to a Revit family type once in an Excel/CSV file. The tool then places them all at the correct location, rotation and level, hosted on ceilings or walls where required.

## Features
- Lists every block name found in a linked or imported DWG, with counts (and mirrored counts)
- Exports a ready-to-fill mapping template (**XLSX** or CSV)
- Reads the mapping from **.xlsx or .csv** (no Excel installation needed)
- Places families at block insertion points, keeping the CAD rotation (+ per-row adjustment)
- Handles DWG units, link position, rotation and shared coordinates automatically
- Hosting per row: **ceiling**, **face** (ceilings/slabs/roofs/beams), **wall**, or **non-hosted**, including hosts in linked Revit models
- Falls back to unhosted placement (or reports a failure) when no host is found
- One dialog: DWG, mapping file, level, **Preview** or **Run**
- Preview does the full placement and then undoes it, so its counts match a real run
- Duplicate protection per level, so re-running only adds new blocks
- One transaction: undo everything with a single Ctrl+Z
- Writes the source block name into each element's Comments parameter
- Summary per family type, unmapped blocks, failures with reasons
- CSV log of every block with its Element ID, host, coordinates and rotation

## Requirements
- Autodesk Revit **2022, 2023, 2024, 2025 or 2026** on Windows
- Nothing else: no pyRevit, no Dynamo, no Excel

## Installation
1. Download **`CAD2Revit-<version>.zip`** from the [Releases](https://github.com/ahmedel0865-lab/CAD2Revit-Family-Mapper/releases) page, or from the latest run in the [Actions](https://github.com/ahmedel0865-lab/CAD2Revit-Family-Mapper/actions) tab (*Artifacts*).
2. Close Revit, unzip, and double-click **`Install.bat`**. It installs for every Revit 2022–2026 on the PC, for the current user; no admin rights are needed.
3. Start Revit and click **Always Load** when asked about the add-in. A **CAD2Revit** tab appears.

Uninstall: `Uninstall.bat`. Manual install and details: [docs/USAGE.md](docs/USAGE.md#0-install-once).

## Quick start
1. Load your families (face-based for hosted devices) and link the DWG in the target floor plan.
2. **CAD2Revit > List Blocks** > *Export template...* (.xlsx).
3. Fill in family, type, offset, rotation adjustment and host type for each block. Leave the family empty for blocks to ignore.
4. **CAD2Revit > Place Families** > pick DWG, mapping and level > **Preview** > **Run**.

- Full guide: [docs/USAGE.md](docs/USAGE.md)
- Test on a small sample first: [docs/TESTING.md](docs/TESTING.md)
- Known limitations: [docs/LIMITATIONS.md](docs/LIMITATIONS.md)
- Example mapping: [templates/mapping_template.xlsx](templates/mapping_template.xlsx) / [.csv](templates/mapping_template.csv)

## Mapping file
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
                                  host finder, placer, dialogs
  src/CAD2Revit.Core/             Revit-free logic: CSV/XLSX, mapping, report, settings
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
Or open `addin/CAD2Revit.sln` in Visual Studio 2022. Pushing a tag like `v0.3.0` makes CI publish the zip as a GitHub Release.

## Optional: pyRevit version
If you already use pyRevit, the same tool is available as a pyRevit extension in `CAD2Revit.extension/`:
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
- [ ] Copy block attributes via an AutoCAD Data Extraction CSV matched by location
- [ ] Auto-assign circuits / panel parameters
- [ ] Save and reuse mapping profiles per project
- [ ] Multiple DWGs / levels in one run

## Contributing
Issues and pull requests are welcome. Please test changes on a copy of a model and include the Revit version you tested with.

## License
[MIT](LICENSE)

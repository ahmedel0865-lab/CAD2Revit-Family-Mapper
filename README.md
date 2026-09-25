# CAD2Revit Family Mapper

A pyRevit extension that converts AutoCAD blocks in a linked DWG into Revit family instances, built for MEP / electrical shop drawings (lighting, power, fire alarm, ELV).

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
- Works on Revit 2022 – 2026 (IronPython and CPython pyRevit engines)

## Requirements
- Autodesk Revit 2022, 2023, 2024, 2025 or 2026
- [pyRevit](https://github.com/pyrevitlabs/pyRevit) 4.8 or newer (5.x recommended for Revit 2025+)

## Installation
**Option A (pyRevit CLI):**
```
pyrevit extend ui CAD2Revit https://github.com/ahmedel0865-lab/CAD2Revit-Family-Mapper.git
```

**Option B (manual):**
1. Download or clone this repository.
2. In Revit: pyRevit tab > Settings > Custom Extension Directories > add the repository folder.
3. Click Reload. A new **CAD2Revit** tab appears.

## Quick start
1. Load your families (face-based for hosted devices) and link the DWG in the target floor plan.
2. **List Blocks** > export the mapping template (.xlsx).
3. Fill in family, type, offset, rotation adjustment and host type for each block. Leave the family empty for blocks to ignore.
4. **Place Families** > pick DWG, mapping and level > **Preview** > **Run**.

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
CAD2Revit.extension/
  CAD2Revit.tab/Mapper.panel/
    List Blocks.pushbutton/script.py      list block names, export template
    Place Families.pushbutton/script.py   dialog > place > summary > log
  lib/cad2revit/
    config.py        settings (tolerances, hosting options)
    compat.py        Revit 2022-2026 API differences
    dwg_reader.py    block names, points, rotation, scale from the DWG
    tables.py        CSV + XLSX reader/writer (pure Python)
    mapping.py       loads and validates the mapping file
    hosting.py       finds ceiling / slab / wall faces (incl. Revit links)
    placer.py        places the families (one transaction)
    report.py        summary + CSV log
    ui.py            dialogs (main_dialog.xaml)
templates/           mapping_template.xlsx / .csv
docs/                USAGE.md, TESTING.md, LIMITATIONS.md
tests/               unit tests (run without Revit)
```

## Limitations (short)
- **Block attributes** (circuit number, panel name, etc.) are not readable through the Revit API, so they are not copied yet.
- **Anonymous dynamic blocks** (`*U123`) and **exploded blocks** cannot be mapped.
- **Mirrored** blocks are placed with rotation only and flagged in the log.
- **Legacy (non face-based) hosted families** cannot host on linked models. Use face-based families.

See [docs/LIMITATIONS.md](docs/LIMITATIONS.md) for details and workarounds.

## Roadmap
- [x] Excel (.xlsx) mapping support
- [x] Wall-hosted placement (sockets, switches)
- [ ] Copy block attributes via an AutoCAD Data Extraction CSV matched by location
- [ ] Auto-assign circuits / panel parameters
- [ ] Save and reuse mapping profiles per project
- [ ] Multiple DWGs / levels in one run

## Development
Unit tests for the parts that don't need Revit:
```
python -m unittest discover -s tests -v
```

## Contributing
Issues and pull requests are welcome. Please test changes on a copy of a model and include the Revit version you tested with.

## License
[MIT](LICENSE)

# CAD2Revit Family Mapper

A pyRevit extension that converts AutoCAD blocks in a linked DWG into Revit family instances, built for MEP / electrical shop drawings (lighting, power, fire alarm, ELV).

Instead of manually placing hundreds of light fixtures, sockets, and detectors over a CAD background, you map each CAD block to a Revit family type once in a CSV file, and the tool places them all at the correct location and rotation.

## Features
- Lists every block name found in a linked or imported DWG, with counts
- Exports a ready-to-fill mapping template (CSV / Excel)
- Places families at block insertion points, keeping the CAD rotation
- Handles link position and rotation automatically (reads Revit model coordinates)
- Level-based placement with elevation offset, or face-based hosting on ceilings
- Preview mode that shows what will be placed without changing the model
- Duplicate protection, so re-running does not double-place
- One transaction: undo everything with a single Ctrl+Z
- Writes the source block name into each element's Comments parameter
- CSV log of every placed element with its Element ID

## Requirements
- Autodesk Revit 2022 or newer
- [pyRevit](https://github.com/pyrevitlabs/pyRevit) 4.8 or newer

## Installation
**Option A (pyRevit CLI):**
```
pyrevit extend ui CAD2Revit https://github.com/<your-username>/CAD2Revit-Family-Mapper.git
```

**Option B (manual):**
1. Download or clone this repository.
2. In Revit: pyRevit tab > Settings > Custom Extension Directories > add the repository folder.
3. Click Reload. A new **CAD2Revit** tab appears.

## Quick start
1. Link your DWG and load your families.
2. **List Blocks** > export the mapping template.
3. Fill in family, type, offset, and host type for each block.
4. **Place Families** > Preview > Run.

See [docs/USAGE.md](docs/USAGE.md) for the full guide and [templates/mapping_template.csv](templates/mapping_template.csv) for an example.

## Project structure
```
CAD2Revit.extension/
  CAD2Revit.tab/Mapper.panel/
    List Blocks.pushbutton/script.py
    Place Families.pushbutton/script.py
  lib/cad2revit/
    config.py       settings (tolerances, options)
    dwg_reader.py   reads block names, points, rotation from the DWG
    mapping.py      loads and validates the mapping CSV
    placer.py       places the families
    ui.py           selection dialogs
    utils.py        CSV and helper functions
templates/mapping_template.csv
docs/USAGE.md
```

## Limitations
- **Block attributes** (circuit number, panel name, etc.) are not readable through the Revit API, so they are not copied yet.
- Wall-hosted families are placed as level-based, not hosted to walls.
- Face hosting searches for ceilings only (in the model and in Revit links).
- Mirrored blocks are placed with rotation only; they are flagged in the log for review.
- Nested blocks are ignored unless `INCLUDE_NESTED_BLOCKS = True` in `config.py`.

## Roadmap
- [ ] Copy block attributes via an AutoCAD Data Extraction CSV matched by location
- [ ] Wall-hosted placement (sockets, switches)
- [ ] Auto-assign circuits / panel parameters
- [ ] Excel (.xlsx) mapping support
- [ ] Save and reuse mapping profiles per project

## Contributing
Issues and pull requests are welcome. Please test changes on a copy of a model and include the Revit version you tested with.

## License
[MIT](LICENSE)

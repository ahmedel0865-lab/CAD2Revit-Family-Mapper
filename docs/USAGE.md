# Usage Guide

## 1. Prepare the Revit project
1. Load all the families you want to place (Insert > Load Family).
2. Link the DWG (Insert > Link CAD). Use **Current view only = off** and correct positioning.
3. Make a copy of the model for your first test.

## 2. List the blocks
Click **CAD2Revit > Mapper > List Blocks**, pick the DWG, and export the template.
Always copy block names from this list, since Revit may report them slightly differently from AutoCAD.

## 3. Fill the mapping CSV
| Column | Example | Notes |
|---|---|---|
| CAD_Block_Name | `SMOKE-DET` | Exact name from List Blocks (case-insensitive) |
| Revit_Family_Name | `Smoke Detector` | Family name as loaded in the project |
| Revit_Type_Name | `Ceiling` | Type name |
| Offset_From_Level_mm | `2800` | Used for level-based placement when no host face is used |
| Rotation_Adjustment_deg | `90` | Added to the CAD block rotation |
| Host_Type | `none`, `ceiling`, `floor` or `face` | See **Host types** below |

Rows with a blank family name are ignored.

### Host types
| Host_Type | Searches for | Typical use |
|---|---|---|
| `none` | nothing: level-based at `Offset_From_Level_mm` | panels, floor boxes drawn as level-based |
| `ceiling` | the nearest **ceiling** underside above the block | lights, detectors in a ceiling |
| `floor` | the nearest **floor / slab** underside above the block (the slab of the level above) | devices fixed to a concrete soffit, no ceiling |
| `face` | the nearest **ceiling or slab** underside above, whichever comes first (categories set by `HOST_CATEGORIES` in `lib/cad2revit/config.py`) | mixed areas, some with ceilings and some without |

How the host is found:
- A ray is cast straight **up** from the block's position, starting 10 mm above the selected level (`HOST_RAY_START_MM`), up to `HOST_SEARCH_DISTANCE_MM` (6000 mm).
- Only **undersides** count, i.e. faces whose normal points down. The top of the slab the level sits on is never used, so a device on Level 1 hosts on the underside of the Level 2 slab, not on the Level 1 floor.
- Faces in **linked Revit models** are found too (e.g. ceilings in the architectural link, slabs in the structural link).

### Sloped ceilings and slabs
Sloped (and flat) ceilings and slabs are supported:
- The family is placed at the **exact hit point** on the face, so it sits on the slope instead of at a flat height.
- The CAD block rotation is projected into the face plane (`ref_dir = d - n (d.n)`), so the family lies flat on the slope and points the same way as the CAD symbol. Along and across the slope the plan direction is exact; in between it can differ by a few degrees on steep slopes.
- The face normal is evaluated at the hit point, so curved or warped faces work too. For linked faces it is transformed by the link's position.

### Which family templates work
| Family type | Placed how | Sloped face |
|---|---|---|
| **Face-based** (e.g. *Generic Model face based*, face-based lighting/detector templates) | Hosted on the ceiling/slab face, in this model or a link | Yes, tilted with the face |
| **Work-plane-based** | Hosted on the face like face-based | Yes, tilted with the face |
| **Level-based** (not hosted) | Level-based, but the elevation is taken from the face height at that point (the CSV offset is ignored, and the log says so) | Height follows the slope; the family itself stays horizontal |
| **Legacy ceiling-hosted** | Hosted on ceilings in this model only (not in links) | Height follows the host; use face-based families where possible |

If no host is found above a block (e.g. an atrium, or outside the building), the family is placed on the level at `Offset_From_Level_mm`, and the log says `host: none`.

## 4. Preview, then run
Click **Place Families**, choose the DWG, the CSV, the level, then **Preview**.
When the counts look right, run again with **Run**. Everything is one transaction, so a single Ctrl+Z undoes it.

## 5. Check the log
A `cad2revit_log_<date>.csv` is saved next to your mapping file with every placed element's ID.
For hosted rows the message column says what was found, e.g. `host: linked ceiling, slope 12.5 deg`, `host: floor, slope 0.0 deg`, or `host: none - no ceiling/slab found within 6000 mm above the level`.
Running the tool twice will not create duplicates (tolerance set in `lib/cad2revit/config.py`).

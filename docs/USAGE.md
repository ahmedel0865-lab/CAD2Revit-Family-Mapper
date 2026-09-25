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
| Offset_From_Level_mm | `2800` | Used for level-based placement |
| Rotation_Adjustment_deg | `90` | Added to the CAD block rotation |
| Host_Type | `none` or `face` | `face` = host on the nearest ceiling above the point |

Rows with a blank family name are ignored.

## 4. Preview, then run
Click **Place Families**, choose the DWG, the CSV, the level, then **Preview**.
When the counts look right, run again with **Run**. Everything is one transaction, so a single Ctrl+Z undoes it.

## 5. Check the log
A `cad2revit_log_<date>.csv` is saved next to your mapping file with every placed element's ID.
Running the tool twice will not create duplicates (tolerance set in `lib/cad2revit/config.py`).

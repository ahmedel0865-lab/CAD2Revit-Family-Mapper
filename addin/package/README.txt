CAD2Revit Family Mapper - standalone Revit add-in (no pyRevit needed)
=====================================================================

INSTALL
  1. Close Revit.
  2. Unzip this folder anywhere (e.g. Downloads).
  3. Double-click Install.bat.
     It installs CAD2Revit for every Revit 2022 / 2023 / 2024 / 2025 / 2026 found
     on this PC, for your Windows user only (no administrator rights needed).
  4. Start Revit. When Revit asks about loading an unsigned add-in
     ("CAD2Revit Family Mapper"), click "Always Load".
  5. A new "CAD2Revit" tab appears with "List Blocks" and "Place Families".

  Manual install (if Install.bat is blocked by IT policy):
    Copy  <version>\CAD2Revit.addin  and the folder  <version>\CAD2Revit
    into  %AppData%\Autodesk\Revit\Addins\<version>\
    then right-click CAD2Revit\CAD2Revit.dll > Properties > tick "Unblock".

UNINSTALL
  Double-click Uninstall.bat (close Revit first).

USE
  1. Load your families (face-based for ceiling/wall devices) and link the DWG
     in the floor plan of the target level.
  2. CAD2Revit > List Blocks > Export template...  (xlsx)
  3. Fill in Revit_Family_Name, Revit_Type_Name, Offset_From_Level_mm,
     Rotation_Adjustment_deg and Host_Type (ceiling / face / wall / non-hosted).
     Leave the family empty for blocks you do not want placed.
     Example: templates\mapping_template.xlsx
  4. CAD2Revit > Place Families > choose DWG, mapping file and level > Preview.
     When the preview looks right: Place Families > Run.
     One Ctrl+Z undoes the whole run.
  5. A log (cad2revit_log_<date>.csv) is saved next to the mapping file.

SETTINGS
  %AppData%\CAD2Revit\settings.ini  (created on first run; open in Notepad)

Full guide: https://github.com/ahmedel0865-lab/CAD2Revit-Family-Mapper

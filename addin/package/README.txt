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
  2. CAD2Revit > Place Families > choose the DWG and level > Next.
  3. Mapping window: one row per CAD block name. Pick the Revit family for
     each block (type in the box to search; close matches are pre-selected),
     set Elevation From Level (mm), optionally Rotation and Host Type.
     (Skip) = do not place.
  4. Preview (nothing is changed; closing the result returns to the grid),
     then Run. One Ctrl+Z undoes the whole run.
  5. The grid is remembered per project. Load/Save Mapping reads and writes
     xlsx/csv files (example: templates\mapping_template.xlsx).
  6. Logs: Documents\CAD2Revit\Logs\<project>\

SETTINGS
  %AppData%\CAD2Revit\settings.ini  (created on first run; open in Notepad)

Full guide: https://github.com/ahmedel0865-lab/CAD2Revit-Family-Mapper

# Known limitations

## Block attributes (circuit, panel, wattage...)
The Revit API does **not** expose AutoCAD block attributes, dynamic-block properties, or object handles for linked/imported DWGs. Revit only sees each block's name and geometry. Attribute values therefore cannot be copied yet.
Planned approach: export attributes from AutoCAD (`DATAEXTRACTION`, or a small AutoLISP routine) to a CSV with block name + insertion X/Y + attributes, then match each placed element to a CSV row by name and location.

## Exploded blocks
Exploded symbols are just lines/arcs, and there is nothing to identify them as devices. Re-block them in AutoCAD (e.g. `BLOCK` + `QSELECT`/`SELECTSIMILAR`) before linking.

## Dynamic blocks
A dynamic block whose parameters were changed (stretched, flipped, visibility state...) becomes an **anonymous block** (`*U123`). Revit reports that anonymous name, not the dynamic block's real name, so these cannot be mapped reliably. List Blocks warns about them and leaves them out of the template.
Fix in AutoCAD: use plain blocks for devices, or convert each variant to a named block (`BCONVERT`, or `EXPLODE` once and re-`BLOCK` under a real name). Unmodified dynamic blocks usually keep their name.

## Nested blocks
Only top-level blocks are read by default. Tick *Include nested blocks* (or set `IncludeNestedBlocks = true` in settings.ini) to also read blocks inside blocks. Then **both** the parent and the children are listed, so map only the level you want and leave the other empty.

## Mirrored blocks
Revit families are placed with the CAD rotation only; mirroring is not applied. Mirrored blocks are flagged in the log ("CAD block is mirrored") so you can check them. For symmetric devices (most lights, detectors) this does not matter.

## Scale
Block scale is recorded in the log (`Block_Scale`) but not applied. Family size comes from the family type.

## Hosting
- **Face-based families** host on ceilings, walls, slabs and beams in this model **and in linked Revit models**.
- **Legacy wall-/ceiling-based families** (non face-based) can only host on elements in the **same** model. If their host is in a link, they are reported as failed. Use face-based families where possible.
- Wall search looks up to 500 mm from the CAD point (`WallSearchDistanceMm` in settings.ini). If the block's insertion point is inside the wall thickness, the device goes on the nearest face.
- Curtain walls and in-place families are searched like any other wall/ceiling, but results can vary.
- The `wall` host ignores `Rotation_Adjustment_deg`: the device faces out of the wall.
- `vertical` (and `wall` when no wall is found) hosts face-based devices on **reference planes** named `CAD2Revit vertical <id>`, one per wall line (devices on the same line share a plane, and later runs reuse them). They show as short dashed lines in plan; hide them with *Visibility/Graphics > Annotation Categories > Reference Planes*. If you delete a plane, its devices are deleted with it. The standalone add-in only; the pyRevit version treats `vertical` as not recognised.
- If no host is found and `FallbackToUnhosted = true`, the element is placed unhosted at the row's offset. For `wall` rows, face-based devices stand upright on a vertical plane. For `ceiling`/`face` rows, a face-based family lies on the level's work plane (facing up).

## Duplicate check
An existing instance of the **same family** within 50 mm in plan, between the target level and the next level up, counts as a duplicate. Instances in linked models are not checked. If you change the mapping to a *different family* for a block, the old instances are not detected: delete them first (filter by Comments = `CAD: ...`).

## Other
- Only one DWG and one level per run. Run once per floor.
- Views/sheets are not affected; the tool uses a temporary 3D view for host searches and deletes it at the end of the run.
- Very large drawings (tens of thousands of blocks) are fine, but hosted rows take longer because each block casts rays.
- Formulas in the .xlsx mapping file are read using the last value Excel saved. Files produced by tools that don't store calculated values may read those cells as empty.

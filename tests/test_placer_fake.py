# -*- coding: utf-8 -*-
"""Control-flow tests for placer.place_all against a fake Revit API."""
import math
import os
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "CAD2Revit.extension", "lib"))

import fake_revit as F  # noqa: E402
F.install()

from cad2revit import placer, report  # noqa: E402
from cad2revit.mapping import MapRow  # noqa: E402

FT = 304.8


class Block(object):
    def __init__(self, name, x_mm, y_mm, rot_deg=0.0):
        self.name = name
        self.point = F.XYZ(x_mm / FT, y_mm / FT, 0)
        self.rotation = math.radians(rot_deg)
        self.mirrored = False
        self.scale_x = self.scale_y = 1.0

    @property
    def key(self):
        return self.name.lower()


class Hit(object):
    def __init__(self, pt, normal, linked=False):
        self.reference, self.point, self.face_normal, self.normal = "ref", pt, normal, normal
        self.element, self.is_linked = object(), linked

    def describe(self):
        return u"Ceilings (linked)" if self.is_linked else u"Ceilings"


class FakeFinder(object):
    """Ceiling at +2800 mm everywhere with x >= 0; wall face at x = 0."""

    def __init__(self, doc, view):
        pass

    def find_above(self, mode, x, y, level_z, max_up):
        if x < 0 or 2800 / FT > max_up:
            return None
        return Hit(F.XYZ(x, y, level_z + 2800 / FT), F.XYZ(0, 0, -1), linked=True)

    def find_wall(self, x, y, z, max_dist, start):
        if abs(x) > max_dist:
            return None
        return Hit(F.XYZ(0, y, z), F.XYZ(1 if x >= 0 else -1, 0, 0))


placer.HostFinder = FakeFinder


def row(block, fam_id, ptype, host, offset=0, rot=0, legacy_linked=False):
    r = MapRow(block, "Fam%d" % fam_id, "T", offset, rot, host)
    r.symbol = F.FamilySymbol(fam_id * 10, F.Family(fam_id, ptype))
    return r


class PlacerTest(unittest.TestCase):
    def setUp(self):
        P = F.FamilyPlacementType
        self.doc = F.Doc()
        self.l1, self.l2 = F.Level(1, 0.0), F.Level(2, 4000 / FT)
        self.doc.elements += [self.l1, self.l2]
        self.mapping = {
            "light": row("LIGHT", 1, P.WorkPlaneBased, "ceiling"),
            "skt": row("SKT", 2, P.WorkPlaneBased, "wall", offset=300),
            "db": row("DB", 3, P.OneLevelBased, "none", offset=1500, rot=90),
            "legacy": row("LEGACY", 4, P.OneLevelBasedHosted, "ceiling"),
            "missing": row("MISSING", 5, P.OneLevelBased, "none"),
        }
        self.mapping["missing"].symbol = None
        self.blocks = [
            Block("LIGHT", 1000, 1000, 30), Block("LIGHT", -1000, 0),   # 2nd: no ceiling
            Block("SKT", 100, 5000), Block("SKT", 3000, 0),             # 2nd: no wall
            Block("DB", 5000, 5000), Block("LEGACY", 2000, 2000),       # legacy + linked host
            Block("MISSING", 0, 0), Block("TEXT", 0, 0), Block("TEXT", 1, 1),
        ]

    def run_place(self, level=None, dry=False):
        return placer.place_all(self.doc, self.blocks, self.mapping, level or self.l1, dry_run=dry)

    def statuses(self, results):
        return report.summarize(results)["status"]

    def test_first_run(self):
        res = self.run_place()
        self.assertEqual(self.statuses(res),
                         {"placed": 5, "failed": 1, "skipped": 1, "unmapped": 2})
        insts = [e for e in self.doc.elements if isinstance(e, F.FamilyInstance)]
        how = sorted((i.Symbol.Family.Id.Value, i.how) for i in insts)
        self.assertEqual(how, [(1, "face"), (1, "levelplane"), (2, "face"),
                               (2, "levelplane"), (3, "level")])
        # Level-based panel: offset set and rotated by CAD 0 + adjustment 90.
        db = [i for i in insts if i.how == "level"][0]
        self.assertAlmostEqual(db.params["INSTANCE_ELEVATION_PARAM"].value, 1500 / FT)
        self.assertAlmostEqual(db.rotation, math.pi / 2)
        # Unhosted fallback gets the row's offset; wall socket sits on the face at 300 mm.
        skt = [i for i in insts if i.how == "face" and i.Symbol.Family.Id.Value == 2][0]
        self.assertAlmostEqual(skt.Location.Point.X, 0.0)
        self.assertAlmostEqual(skt.Location.Point.Z, 300 / FT)
        failed = [r for r in res if r.status == "failed"][0]
        self.assertIn("Revit link", failed.message)
        light_fallback = [r for r in res if r.status == "placed" and "no ceiling" in r.message]
        self.assertEqual(len(light_fallback), 1)

    def test_rerun_is_duplicate_free_and_levels_are_separate(self):
        self.run_place()
        n = len(self.doc.elements)
        res2 = self.run_place()
        self.assertEqual(self.statuses(res2).get("placed", 0), 0)
        self.assertEqual(self.statuses(res2)["duplicate"], 5)
        self.assertEqual(len(self.doc.elements), n)
        # Same layout on the level above is NOT a duplicate.
        res3 = self.run_place(level=self.l2)
        self.assertEqual(self.statuses(res3)["placed"], 5)

    def test_preview_changes_nothing(self):
        res = self.run_place(dry=True)
        self.assertEqual(self.statuses(res)["placed"], 5)
        self.assertTrue(all(r.element_id is None for r in res))
        self.assertFalse(any(isinstance(e, F.FamilyInstance) for e in self.doc.elements))


if __name__ == "__main__":
    unittest.main()

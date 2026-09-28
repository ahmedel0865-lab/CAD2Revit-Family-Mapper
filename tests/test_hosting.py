# -*- coding: utf-8 -*-
"""Tests for cad2revit.hosting (runs without Revit: python -m unittest discover -s tests)."""
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "CAD2Revit.extension", "lib"))
from cad2revit import hosting  # noqa: E402


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def length(a):
    return math.sqrt(dot(a, a))


class HostingTests(unittest.TestCase):
    def test_categories_for_host_types(self):
        self.assertEqual(hosting.categories_for("ceiling", ["ceiling", "floor"]), ["ceiling"])
        self.assertEqual(hosting.categories_for("floor", ["ceiling"]), ["floor"])
        self.assertEqual(hosting.categories_for("face", ["Ceiling", " floor ", "roof"]), ["ceiling", "floor"])
        self.assertEqual(hosting.categories_for("none", ["ceiling", "floor"]), [])

    def test_flat_underside_keeps_cad_rotation(self):
        rd = hosting.ref_direction(math.radians(30), (0, 0, -1))
        self.assertAlmostEqual(rd[0], math.cos(math.radians(30)))
        self.assertAlmostEqual(rd[1], math.sin(math.radians(30)))
        self.assertAlmostEqual(rd[2], 0)

    def test_sloped_face_direction_lies_in_face_plane(self):
        # Ceiling sloping 20 deg, rising towards +X: underside normal points down and tilts towards +X.
        s = math.radians(20)
        n = (math.sin(s), 0.0, -math.cos(s))
        for deg in (0, 45, 90, 135, 200, 300):
            rd = hosting.ref_direction(math.radians(deg), n)
            self.assertAlmostEqual(length(rd), 1.0)
            self.assertAlmostEqual(dot(rd, n), 0.0)          # in the face plane
            # Plan heading: exact along/across the slope, within a few degrees otherwise
            # (orthogonal projection onto a 20 deg plane).
            plan = math.degrees(math.atan2(rd[1], rd[0])) % 360
            diff = abs((plan - deg + 180) % 360 - 180)
            if deg % 90 == 0:
                self.assertAlmostEqual(diff, 0.0, places=6)
            else:
                self.assertLess(diff, 5.0)

    def test_rotation_parallel_to_normal_uses_cross_product(self):
        # Vertical face (normal +X) and CAD rotation 0 (+X): projection is zero.
        rd = hosting.ref_direction(0.0, (1.0, 0.0, 0.0))
        self.assertAlmostEqual(length(rd), 1.0)
        self.assertAlmostEqual(dot(rd, (1.0, 0.0, 0.0)), 0.0)
        self.assertAlmostEqual(rd[2], 0.0)                   # n x Z is horizontal

    def test_slope_angle(self):
        self.assertAlmostEqual(hosting.slope_deg((0, 0, -1)), 0.0)
        s = math.radians(25)
        self.assertAlmostEqual(hosting.slope_deg((math.sin(s), 0, -math.cos(s))), 25.0)
        self.assertAlmostEqual(hosting.slope_deg((1, 0, 0)), 90.0)

    def test_nearest_underside_skips_top_faces_and_far_hits(self):
        top_of_own_slab = (0.05, (0, 0, 1), "top")       # normal up: slab the level sits on
        soffit_above = (10.0, (0, 0, -1), "soffit")
        sloped_soffit = (9.0, (0.3, 0, -0.95), "sloped")
        too_far = (30.0, (0, 0, -1), "far")
        self.assertEqual(hosting.pick_nearest_underside([top_of_own_slab, soffit_above], 20), "soffit")
        self.assertEqual(hosting.pick_nearest_underside([soffit_above, sloped_soffit, too_far], 20), "sloped")
        self.assertIsNone(hosting.pick_nearest_underside([top_of_own_slab, too_far], 20))
        self.assertIsNone(hosting.pick_nearest_underside([], 20))

    def test_describe_for_log(self):
        self.assertEqual(hosting.describe(None, False, None), u"host: none")
        self.assertEqual(hosting.describe("ceiling", False, (0, 0, -1)), u"host: ceiling, slope 0.0 deg")
        s = math.radians(12.5)
        self.assertEqual(hosting.describe("floor", True, (math.sin(s), 0, -math.cos(s))),
                         u"host: linked floor, slope 12.5 deg")


if __name__ == "__main__":
    unittest.main()

# -*- coding: utf-8 -*-
import os
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "CAD2Revit.extension", "lib"))

from cad2revit import report  # noqa: E402
from cad2revit.mapping import MapRow  # noqa: E402


class FakeBlock(object):
    def __init__(self, name, mirrored=False, sx=1.0, sy=1.0):
        self.name, self.mirrored, self.scale_x, self.scale_y = name, mirrored, sx, sy


class ReportTest(unittest.TestCase):
    def setUp(self):
        light = MapRow("L", "Panel", "600", 2800, 0, "ceiling")
        det = MapRow("S", "Smoke", "Std", 2800, 0, "ceiling")
        R = report.Result
        self.results = [
            R("TEXT", None, report.UNMAPPED, message="not in mapping file", count=7),
            R(FakeBlock("L"), light, report.PLACED, 101, point=(1.0, 2.0, 9.186), rotation=-1.5707963),
            R(FakeBlock("L", mirrored=True, sx=2, sy=1), light, report.PLACED, 102, point=(0, 0, 0), rotation=0),
            R(FakeBlock("S"), det, report.FAILED, message="no ceiling found"),
            R(FakeBlock("S"), det, report.FAILED, message="no ceiling found"),
            R(FakeBlock("S"), det, report.DUPLICATE, message="exists"),
        ]

    def test_summary(self):
        s = report.summarize(self.results)
        self.assertEqual(s["by_type"], [("Panel : 600", 2)])
        self.assertEqual(s["unmapped"], [("TEXT", 7)])
        self.assertEqual(s["status"], {"unmapped": 7, "placed": 2, "failed": 2, "duplicate": 1})
        self.assertEqual(report.group_problems(s["problems"]),
                         [("failed", "S", "no ceiling found", 2)])

    def test_log_rows(self):
        rows = report.log_rows(self.results)
        self.assertEqual(len(rows[0]), len(report.LOG_HEADER))
        self.assertEqual(rows[0][0:2], ["unmapped", "TEXT"])
        self.assertEqual(rows[0][-1], "7 instance(s). not in mapping file")
        self.assertEqual(rows[1][4], 101)
        self.assertEqual(rows[1][6:10], [u"304.8", u"609.6", u"2799.9", u"270.00"])
        self.assertEqual(rows[2][10:12], [u"2 x 1", u"yes"])
        self.assertEqual(rows[3][4], u"")


if __name__ == "__main__":
    unittest.main()

# -*- coding: utf-8 -*-
"""Unit tests for the pure-Python parts (no Revit needed).

Run from the repository root:   python -m unittest discover -s tests
"""
import codecs
import os
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "CAD2Revit.extension", "lib"))

from cad2revit import tables, mapping  # noqa: E402

TEMPLATES = os.path.join(HERE, "..", "templates")


class TablesTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def _write(self, name, text):
        p = os.path.join(self.tmp, name)
        with codecs.open(p, "w", "utf-8-sig") as f:
            f.write(text)
        return p

    def test_csv_quotes_blank_lines_and_padding(self):
        p = self._write("a.csv", u'A,B,C\r\n"x, y","say ""hi""",1\r\n\r\nshort\n')
        rows = tables.read_table(p)
        self.assertEqual(rows[0], {"A": u"x, y", "B": u'say "hi"', "C": u"1"})
        self.assertEqual(rows[1], {"A": u"short", "B": u"", "C": u""})
        self.assertEqual(len(rows), 2)

    def test_csv_semicolon_locale(self):
        p = self._write("b.csv", u"A;B\nfoo;2,5\n")
        self.assertEqual(tables.read_table(p), [{"A": u"foo", "B": u"2,5"}])

    def test_csv_roundtrip_unicode(self):
        p = os.path.join(self.tmp, "c.csv")
        tables.write_csv(p, ["Name", "Val"], [[u"إنارة", 3], [u'a,"b"', None]])
        self.assertEqual(tables.read_table(p),
                         [{"Name": u"إنارة", "Val": u"3"}, {"Name": u'a,"b"', "Val": u""}])

    def test_xlsx_roundtrip(self):
        p = os.path.join(self.tmp, "d.xlsx")
        tables.write_xlsx(p, ["Block", "Offset"], [[u"SMOKE & HEAT <1>", 2800], [u"إنارة", 0.5]])
        self.assertEqual(tables.read_table(p),
                         [{"Block": u"SMOKE & HEAT <1>", "Offset": u"2800"},
                          {"Block": u"إنارة", "Offset": u"0.5"}])

    def test_xlsx_shared_strings_and_gaps(self):
        # Hand-built workbook the way Excel writes it: shared strings, a skipped
        # column (B empty in row 2) and a sheet not called "Mapping".
        import zipfile
        p = os.path.join(self.tmp, "e.xlsx")
        ns = 'xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"'
        zf = zipfile.ZipFile(p, "w")
        zf.writestr("xl/workbook.xml",
                    '<workbook %s xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
                    '<sheets><sheet name="Sheet1" sheetId="1" r:id="rId7"/></sheets></workbook>' % ns)
        zf.writestr("xl/_rels/workbook.xml.rels",
                    '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                    '<Relationship Id="rId7" Type="x" Target="/xl/worksheets/data.xml"/></Relationships>')
        zf.writestr("xl/sharedStrings.xml",
                    '<sst %s><si><t>H1</t></si><si><t>H2</t></si><si><t>H3</t></si>'
                    '<si><r><t>ri</t></r><r><t>ch</t></r></si></sst>' % ns)
        zf.writestr("xl/worksheets/data.xml",
                    '<worksheet %s><sheetData>'
                    '<row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c>'
                    '<c r="C1" t="s"><v>2</v></c></row>'
                    '<row r="3"><c r="A3" t="s"><v>3</v></c><c r="C3"><v>90.0</v></c></row>'
                    '</sheetData></worksheet>' % ns)
        zf.close()
        self.assertEqual(tables.read_table(p), [{"H1": u"rich", "H2": u"", "H3": u"90"}])


class MappingTest(unittest.TestCase):
    def test_aliases_hosts_and_numbers(self):
        rows = [
            {"CAD_Block_Name": "LIGHT-600", "Revit_Family_Name": "Panel", "Revit_Type_Name": "600",
             "Offset_From_Level (mm)": "2800", "Rotation_Adjustment (deg)": "90", "Host_Type": "Ceiling"},
            {"CAD_Block_Name": "SKT", "Revit_Family_Name": "Socket", "Revit_Type_Name": "Twin",
             "Offset_From_Level (mm)": "300", "Rotation_Adjustment (deg)": "", "Host_Type": "wall"},
            {"CAD_Block_Name": "PNL", "Revit_Family_Name": "Panel Board", "Revit_Type_Name": "DB",
             "Offset_From_Level (mm)": "1,5", "Rotation_Adjustment (deg)": "", "Host_Type": "Non-hosted"},
            {"CAD_Block_Name": "UNUSED", "Revit_Family_Name": "", "Revit_Type_Name": "",
             "Offset_From_Level (mm)": "", "Rotation_Adjustment (deg)": "", "Host_Type": ""},
        ]
        m, errors = mapping.parse_rows(rows)
        self.assertEqual(errors, [])
        self.assertEqual(sorted(m), ["light-600", "pnl", "skt"])
        self.assertEqual((m["light-600"].offset_mm, m["light-600"].rot_deg, m["light-600"].host),
                         (2800.0, 90.0, "ceiling"))
        self.assertEqual(m["skt"].host, "wall")
        self.assertEqual((m["pnl"].offset_mm, m["pnl"].host), (1.5, "none"))

    def test_errors(self):
        rows = [
            {"Block": "A", "Family": "F", "Type": "T", "Offset": "x", "Host": ""},
            {"Block": "B", "Family": "F", "Type": "", "Offset": "", "Host": ""},
            {"Block": "C", "Family": "F", "Type": "T", "Offset": "", "Host": "roof"},
            {"Block": "c", "Family": "F", "Type": "T2", "Offset": "", "Host": ""},
        ]
        m, errors = mapping.parse_rows(rows)
        self.assertEqual(list(m), ["c"])
        self.assertEqual(m["c"].type_name, "T2")
        self.assertEqual(len(errors), 4)

    def test_missing_columns(self):
        m, errors = mapping.parse_rows([{"Block": "A"}])
        self.assertEqual(m, {})
        self.assertIn("Missing column", errors[0])

    def test_shipped_templates_parse(self):
        for name in ("mapping_template.csv", "mapping_template.xlsx"):
            m, errors = mapping.load_mapping(None, os.path.join(TEMPLATES, name))
            self.assertEqual(errors, [], name)
            self.assertGreaterEqual(len(m), 4, name)


if __name__ == "__main__":
    unittest.main()

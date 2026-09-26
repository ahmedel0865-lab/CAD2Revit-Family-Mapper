using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CAD2Revit.Core;
using Xunit;

namespace CAD2Revit.Core.Tests
{
    public class TablesTests : IDisposable
    {
        readonly string _tmp = Path.Combine(Path.GetTempPath(), "c2r_" + Guid.NewGuid().ToString("N"));
        public TablesTests() => Directory.CreateDirectory(_tmp);
        public void Dispose() => Directory.Delete(_tmp, true);

        string Write(string name, string text)
        {
            var p = Path.Combine(_tmp, name);
            File.WriteAllText(p, text, new UTF8Encoding(true));
            return p;
        }

        [Fact]
        public void Csv_QuotesBlankLinesAndPadding()
        {
            var rows = Tables.ReadTable(Write("a.csv", "A,B,C\r\n\"x, y\",\"say \"\"hi\"\"\",1\r\n\r\nshort\n"));
            Assert.Equal(2, rows.Count);
            Assert.Equal("x, y", rows[0]["A"]);
            Assert.Equal("say \"hi\"", rows[0]["B"]);
            Assert.Equal("", rows[1]["C"]);
        }

        [Fact]
        public void Csv_SemicolonLocale()
        {
            var rows = Tables.ReadTable(Write("b.csv", "A;B\nfoo;2,5\n"));
            Assert.Equal("2,5", rows.Single()["B"]);
        }

        [Fact]
        public void Csv_RoundTripUnicode()
        {
            var p = Path.Combine(_tmp, "c.csv");
            Tables.WriteCsv(p, new[] { "Name", "Val" }, new List<IList<object>> { new object[] { "إنارة", 3 }, new object[] { "a,\"b\"", null } });
            var rows = Tables.ReadTable(p);
            Assert.Equal("إنارة", rows[0]["Name"]);
            Assert.Equal("3", rows[0]["Val"]);
            Assert.Equal("a,\"b\"", rows[1]["Name"]);
        }

        [Fact]
        public void Xlsx_RoundTrip()
        {
            var p = Path.Combine(_tmp, "d.xlsx");
            Tables.WriteXlsx(p, new[] { "Block", "Offset" },
                new List<IList<object>> { new object[] { "SMOKE & HEAT <1>", 2800 }, new object[] { "إنارة", 0.5 } });
            var rows = Tables.ReadTable(p);
            Assert.Equal("SMOKE & HEAT <1>", rows[0]["Block"]);
            Assert.Equal("2800", rows[0]["Offset"]);
            Assert.Equal("0.5", rows[1]["Offset"]);
        }

        [Fact]
        public void Xlsx_SharedStringsGapsAndOtherSheetName()
        {
            var p = Path.Combine(_tmp, "e.xlsx");
            const string ns = "xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"";
            using (var zip = ZipFile.Open(p, ZipArchiveMode.Create))
            {
                void Add(string name, string xml)
                {
                    using var w = new StreamWriter(zip.CreateEntry(name).Open());
                    w.Write(xml);
                }
                Add("xl/workbook.xml", $"<workbook {ns} xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    "<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId7\"/></sheets></workbook>");
                Add("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId7\" Type=\"x\" Target=\"/xl/worksheets/data.xml\"/></Relationships>");
                Add("xl/sharedStrings.xml", $"<sst {ns}><si><t>H1</t></si><si><t>H2</t></si><si><t>H3</t></si>" +
                    "<si><r><t>ri</t></r><r><t>ch</t></r></si></sst>");
                Add("xl/worksheets/data.xml", $"<worksheet {ns}><sheetData>" +
                    "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c><c r=\"C1\" t=\"s\"><v>2</v></c></row>" +
                    "<row r=\"3\"><c r=\"A3\" t=\"s\"><v>3</v></c><c r=\"C3\"><v>90.0</v></c></row></sheetData></worksheet>");
            }
            var row = Tables.ReadTable(p).Single();
            Assert.Equal("rich", row["H1"]);
            Assert.Equal("", row["H2"]);
            Assert.Equal("90", row["H3"]);
        }
    }

    public class MappingTests
    {
        static Dictionary<string, string> R(params string[] kv)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
            return d;
        }

        [Fact]
        public void AliasesHostsAndNumbers()
        {
            string[] H = { "CAD_Block_Name", "Revit_Family_Name", "Revit_Type_Name", "Offset_From_Level (mm)", "Rotation_Adjustment (deg)", "Host_Type" };
            Dictionary<string, string> Row(params string[] v) => R(H.Zip(v, (a, b) => new[] { a, b }).SelectMany(x => x).ToArray());
            var m = Mapping.Parse(new List<Dictionary<string, string>>
            {
                Row("LIGHT-600", "Panel", "600", "2800", "90", "Ceiling"),
                Row("SKT", "Socket", "Twin", "300", "", "wall"),
                Row("PNL", "Panel Board", "DB", "1,5", "", "Non-hosted"),
                Row("UNUSED", "", "", "", "", ""),
            });
            Assert.Empty(m.Errors);
            Assert.Equal(3, m.Rows.Count);
            var light = m.Rows["light-600"];   // case-insensitive lookup
            Assert.Equal(2800, light.OffsetMm);
            Assert.Equal(90, light.RotationDeg);
            Assert.Equal(HostMode.Ceiling, light.Host);
            Assert.Equal(HostMode.Wall, m.Rows["SKT"].Host);
            Assert.Equal(1.5, m.Rows["PNL"].OffsetMm);
            Assert.Equal(HostMode.None, m.Rows["PNL"].Host);
        }

        [Fact]
        public void Errors()
        {
            var m = Mapping.Parse(new List<Dictionary<string, string>>
            {
                R("Block", "A", "Family", "F", "Type", "T", "Offset", "x", "Host", ""),
                R("Block", "B", "Family", "F", "Type", "", "Offset", "", "Host", ""),
                R("Block", "C", "Family", "F", "Type", "T", "Offset", "", "Host", "roof"),
                R("Block", "c", "Family", "F", "Type", "T2", "Offset", "", "Host", ""),
            });
            Assert.Single(m.Rows);
            Assert.Equal("T2", m.Rows["C"].TypeName);
            Assert.Equal(4, m.Errors.Count);
        }

        [Fact]
        public void MissingColumns()
        {
            var m = Mapping.Parse(new List<Dictionary<string, string>> { R("Block", "A") });
            Assert.Empty(m.Rows);
            Assert.Contains("Missing column", m.Errors[0]);
        }

        [Theory]
        [InlineData("mapping_template.csv")]
        [InlineData("mapping_template.xlsx")]
        public void ShippedTemplatesParse(string name)
        {
            var m = Mapping.Load(Path.Combine(AppContext.BaseDirectory, "templates", name));
            Assert.Empty(m.Errors);
            Assert.True(m.Rows.Count >= 4);
        }
    }

    public class ReportTests
    {
        static List<PlacementResult> Sample()
        {
            var light = new MapRow { Block = "L", Family = "Panel", TypeName = "600" };
            var det = new MapRow { Block = "S", Family = "Smoke", TypeName = "Std" };
            return new List<PlacementResult>
            {
                new PlacementResult { BlockName = "TEXT", Status = Status.Unmapped, Message = "not in mapping file", Count = 7, HasBlock = false },
                new PlacementResult { BlockName = "L", Row = light, Status = Status.Placed, ElementId = 101, Point = new[] { 1.0, 2.0, 9.186 }, Rotation = -Math.PI / 2 },
                new PlacementResult { BlockName = "L", Row = light, Status = Status.Placed, ElementId = 102, Point = new[] { 0.0, 0, 0 }, Rotation = 0, Mirrored = true, ScaleX = 2 },
                new PlacementResult { BlockName = "S", Row = det, Status = Status.Failed, Message = "no ceiling found" },
                new PlacementResult { BlockName = "S", Row = det, Status = Status.Failed, Message = "no ceiling found" },
                new PlacementResult { BlockName = "S", Row = det, Status = Status.Duplicate, Message = "exists" },
            };
        }

        [Fact]
        public void Summary()
        {
            var s = Report.Summarize(Sample());
            Assert.Equal("Panel : 600", s.ByType.Single().Key);
            Assert.Equal(2, s.ByType.Single().Value);
            Assert.Equal(7, s.Unmapped.Single().Value);
            Assert.Equal(7, s.Get(Status.Unmapped));
            Assert.Equal(2, s.Get(Status.Failed));
            Assert.Equal(1, s.Get(Status.Duplicate));
            var g = Report.GroupProblems(s.Problems).Single();
            Assert.Equal(2, g.Item4);
            var text = Report.SummaryText(s, true);
            Assert.Contains("2 would be placed", text);
            Assert.Empty(s.Warnings);
            var warned = Sample();
            warned[1].Message = "WARNING: family is not face-based - placed level-based";
            var s2 = Report.Summarize(warned);
            Assert.Single(s2.Warnings);
            Assert.Contains("with warnings", Report.SummaryText(s2, false));
            Assert.Contains("no ceiling found", text);
        }

        [Fact]
        public void LogRows()
        {
            var rows = Report.LogRows(Sample());
            Assert.Equal(Report.LogHeader.Length, rows[0].Count);
            Assert.Equal("unmapped", rows[0][0]);
            Assert.Equal("7 instance(s). not in mapping file", rows[0][13]);
            Assert.Equal(101L, rows[1][5]);
            Assert.Equal(new object[] { "304.8", "609.6", "2799.9", "270.00" }, rows[1].Skip(7).Take(4).ToArray());
            Assert.Equal("2 x 1", rows[2][11]);
            Assert.Equal("yes", rows[2][12]);
            Assert.Equal("", rows[0][11]);
        }
    }

    public class SettingsTests
    {
        [Fact]
        public void RoundTripAndDefaults()
        {
            var p = Path.Combine(Path.GetTempPath(), "c2r_" + Guid.NewGuid().ToString("N"), "settings.ini");
            var s = Settings.Load(p);                    // creates the file with defaults
            Assert.True(File.Exists(p));
            Assert.Equal(50.0, s.DuplicateToleranceMm);
            s.WallSearchDistanceMm = 750;
            s.FallbackToUnhosted = false;
            s.LastMappingPath = @"C:\Jobs\map.xlsx";
            Settings.TrySave(s, p);
            var t = Settings.Load(p);
            Assert.Equal(750.0, t.WallSearchDistanceMm);
            Assert.False(t.FallbackToUnhosted);
            Assert.Equal(@"C:\Jobs\map.xlsx", t.LastMappingPath);
            Directory.Delete(Path.GetDirectoryName(p), true);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using SmartHostMEP.Core;
using Xunit;

namespace SmartHostMEP.Core.Tests
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

    public class SimpleReportTests
    {
        static PlacementResult R(string block, string family, Status status, string msg = "", int count = 1) =>
            new PlacementResult
            {
                BlockName = block, Status = status, Message = msg, Count = count,
                Row = family == null ? null : new MapRow { Block = block, Family = family, TypeName = "T" },
            };

        [Fact]
        public void HeadlineGroupsByFamilyAndIsGreenOnlyWhenAllPlaced()
        {
            var all = SimpleReport.From(new[]
            {
                R("L1", "Panel", Status.Placed), R("L2", "Panel", Status.Placed), R("S", "Smoke", Status.Placed),
            }, preview: false);
            Assert.Equal("Placed 3 of 3 families", all.Headline);
            Assert.True(all.AllPlaced);
            Assert.Equal(new[] { "Panel", "Smoke" }, all.ByFamily.Select(kv => kv.Key));
            Assert.Equal(new[] { 2, 1 }, all.ByFamily.Select(kv => kv.Value));
            Assert.Empty(all.Warnings);

            var some = SimpleReport.From(new[]
            {
                R("L1", "Panel", Status.Placed), R("S", "Smoke", Status.Failed, "no ceiling found"),
                R("TEXT", null, Status.Unmapped, "not mapped (Skip)", count: 7),
            }, preview: true);
            Assert.Equal("Would place 1 of 9 families", some.Headline);
            Assert.False(some.AllPlaced);
            Assert.False(new SimpleReport().AllPlaced);
        }

        [Fact]
        public void WarningsArePlainOneLinePerProblem()
        {
            var r = SimpleReport.From(new[]
            {
                R("LIGHT", "Panel", Status.Placed, "no ceiling found within 6000 mm above the level - placed unhosted"),
                R("LIGHT", "Panel", Status.Placed, "no ceiling found within 6000 mm above the level - placed unhosted"),
                R("SMOKE", "Smoke", Status.Failed,
                  "family is not face-based (placement type OneLevelBased) - it cannot be hosted on a ceiling; " +
                  "use a family made from a face-based template; DEBUG linked=yes element=123 (Floors) normal=(0,0,-1)"),
                R("EXIT", "Exit", Status.Skipped, "family/type not loaded"),
                R("DUP", "Panel", Status.Duplicate, "exists"),
                R("OK", "Panel", Status.Placed, ""),
            }, preview: false);
            Assert.Equal(new[]
            {
                "SMOKE: family is not face-based, it cannot be hosted on a ceiling, not placed",
                "EXIT: family 'Exit' is not loaded in this project, not placed",
                "LIGHT (x2): no ceiling found above, placed on level",
                "DUP: already in the model at this spot, skipped",
            }, r.Warnings);
            Assert.DoesNotContain(r.Warnings, w => w.Contains("123") || w.Contains("DEBUG") || w.Contains("mm"));
        }

        [Theory]
        [InlineData("WARNING: no slab above this point within 3500 mm (slab opening or no slab) - hosted on a reference plane at 3200 mm (underside of the slab above)",
                    "no slab above (slab opening or no slab), hosted on a reference plane")]
        [InlineData("failed - not hosted on linked slab: Host is Reference Plane", "not hosted on linked slab: Host is Reference Plane")]
        [InlineData("host: linked ceiling, slope 12.5 deg", "host: linked ceiling")]
        [InlineData("DEBUG linked=no host=none", null)]
        [InlineData("", null)]
        public void PlainReason(string message, string expected) =>
            Assert.Equal(expected, SimpleReport.PlainReason(message));
    }

    public class SlabOptionsTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "c2r_slab_" + Guid.NewGuid().ToString("N"));
        public SlabOptionsTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch (Exception) { } }

        static MapRow Row() => new MapRow { Block = "L", Family = "Light", TypeName = "600", Host = HostMode.SlabAbove };

        [Theory]
        [InlineData("m.csv")]
        [InlineData("m.xlsx")]
        public void SavedWithTheMapping(string file)
        {
            var path = Path.Combine(_dir, file);
            Mapping.Save(path, new[] { Row() }, new SlabOptions { SearchRangeMm = 4200, FallbackPlaneMm = 2750 });
            var m = Mapping.Load(path);
            Assert.Empty(m.Errors);
            Assert.True(m.HasSlabOptions);
            Assert.Equal(4200, m.Slab.SearchRangeMm);
            Assert.Equal(2750, m.Slab.FallbackPlaneMm);
            Assert.Equal(HostMode.SlabAbove, m.Rows["L"].Host);
        }

        [Fact]
        public void OldFilesUseTheDefaults()
        {
            var path = Path.Combine(_dir, "old.csv");
            File.WriteAllText(path, "CAD_Block_Name,Revit_Family_Name,Revit_Type_Name,Host_Type\nL,Light,600,slab above\n");
            var m = Mapping.Load(path);
            Assert.False(m.HasSlabOptions);
            Assert.Equal(5000, m.Slab.SearchRangeMm);
            Assert.Equal(3000, m.Slab.FallbackPlaneMm);
        }

        [Fact]
        public void InvalidRangeFallsBackToDefault()
        {
            var path = Path.Combine(_dir, "bad.csv");
            File.WriteAllText(path, "CAD_Block_Name,Revit_Family_Name,Revit_Type_Name,Slab_Search_Range_mm\nL,Light,600,-5\n");
            var m = Mapping.Load(path);
            Assert.Equal(5000, m.Slab.SearchRangeMm);
            Assert.Single(m.Errors);
        }

        [Fact]
        public void FallbacksAreCountedNotListedAsWarnings()
        {
            var row = Row();
            var msg = SlabSearch.FallbackMessage(5000, 3000, false);
            var results = new List<PlacementResult>
            {
                new PlacementResult { BlockName = "L", Row = row, Status = Status.Placed, Message = msg, SlabFallback = true },
                new PlacementResult { BlockName = "L", Row = row, Status = Status.Placed, Message = msg, SlabFallback = true },
                new PlacementResult { BlockName = "L", Row = row, Status = Status.Placed },
            };
            var simple = SimpleReport.From(results, preview: true);
            Assert.Equal(2, simple.SlabFallbacks);
            Assert.Equal(new[] { "No slab/beam within 5000 mm - placed on reference plane at +3000 mm (2 families)" }, simple.SlabFallbackLines);
            Assert.Empty(simple.Warnings);
            Assert.Equal("Would place 3 of 3 families", simple.Headline);

            var s = Report.Summarize(results);
            Assert.Equal(2, s.SlabFallbacks);
            Assert.Contains("2 Slab (above) / Ceiling block(s) would be placed at the fallback height", Report.SummaryText(s, true));
        }
    }

    public class NeedsReviewTests
    {
        [Fact]
        public void ListsOnlyFallbackElementsWithReasonAndIds()
        {
            var row = new MapRow { Block = "L", Family = "Light", TypeName = "600", Host = HostMode.SlabAbove };
            var msg = SlabSearch.FallbackMessage(5000, 3000, false);
            var results = new List<PlacementResult>
            {
                new PlacementResult { BlockName = "L", Row = row, Status = Status.Placed, ElementId = 11, Point = new[] { 1.0, 2.0, 9.8 },
                                      Message = "level 'X' not found - placed on Level 1; " + msg, SlabFallback = true, SlabHost = SlabHost.Plane },
                new PlacementResult { BlockName = "L", Row = row, Status = Status.Placed, ElementId = 12, Point = new[] { 0.0, 0, 0 },
                                      Message = SlabSearch.FallbackMessage(5000, 3000, true), SlabFallback = true, SlabHost = SlabHost.LevelBased },
                new PlacementResult { BlockName = "L", Row = row, Status = Status.Placed, ElementId = 13, SlabHost = SlabHost.Beam },
                new PlacementResult { BlockName = "L", Row = row, Status = Status.Placed, ElementId = 14, SlabHost = SlabHost.Slab },
                new PlacementResult { BlockName = "L", Row = row, Status = Status.Failed, ElementId = null, SlabFallback = true },
            };
            var items = NeedsReview.From(results);
            Assert.Equal(new long?[] { 11, 12 }, items.Select(i => i.ElementId));
            Assert.Equal("No slab/beam within 5000 mm", items[0].Reason);
            Assert.Equal("Light : 600", items[0].FamilyType);
            Assert.Equal("305, 610", items[0].XY);
            Assert.Equal("11,12", NeedsReview.CopyIds(items));
            var rows = NeedsReview.Rows(items);
            Assert.Equal(NeedsReview.Header.Length, rows[0].Count);
            Assert.Equal(11L, rows[0][0]);

            var s = Report.Summarize(results);
            Assert.Equal(new[] { "Slab (above): 1 hosted on slab, 1 hosted on beam, 1 on reference plane, 1 level-based" }, s.HostCounts);
            Assert.Equal(s.HostCounts, SimpleReport.From(results, false).HostCountLines);
            Assert.Empty(SimpleReport.From(new[] { new PlacementResult { Status = Status.Placed } }, false).HostCountLines);
        }

        [Fact]
        public void CeilingFallbacksShareTheSameListAndCounts()
        {
            var slabRow = new MapRow { Block = "S", Family = "Smoke", TypeName = "Std", Host = HostMode.SlabAbove };
            var ceilRow = new MapRow { Block = "L", Family = "Light", TypeName = "600", Host = HostMode.Ceiling };
            var ceilMsg = SlabSearch.FallbackMessage(5000, 3000, false, HostMode.Ceiling);
            Assert.Equal("No ceiling within 5000 mm - placed on reference plane at +3000 mm", ceilMsg);
            Assert.Equal("No ceiling within 4000 mm - placed level-based at +3000 mm",
                         SlabSearch.FallbackMessage(4000, 3000, true, HostMode.Ceiling));
            var results = new List<PlacementResult>
            {
                new PlacementResult { BlockName = "L", Row = ceilRow, Status = Status.Placed, ElementId = 1, SlabHost = SlabHost.Ceiling },
                new PlacementResult { BlockName = "L", Row = ceilRow, Status = Status.Placed, ElementId = 2, SlabHost = SlabHost.Ceiling },
                new PlacementResult { BlockName = "L", Row = ceilRow, Status = Status.Placed, ElementId = 3, Message = ceilMsg,
                                      SlabFallback = true, SlabHost = SlabHost.Plane, Point = new[] { 0.0, 0, 0 } },
                new PlacementResult { BlockName = "S", Row = slabRow, Status = Status.Placed, ElementId = 4, SlabHost = SlabHost.Slab },
            };
            Assert.Equal(new[]
            {
                "Slab (above): 1 hosted on slab, 0 hosted on beam, 0 on reference plane, 0 level-based",
                "Ceiling: 2 hosted on ceiling, 1 on reference plane, 0 level-based",
            }, Report.HostCountLines(results));
            var review = NeedsReview.From(results);
            Assert.Equal("No ceiling within 5000 mm", review.Single().Reason);
            Assert.Equal(new[] { ceilMsg + " (1 family)" }, SimpleReport.From(results, false).SlabFallbackLines);
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

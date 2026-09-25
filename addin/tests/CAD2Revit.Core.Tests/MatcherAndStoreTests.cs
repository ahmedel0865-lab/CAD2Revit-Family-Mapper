using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CAD2Revit.Core;
using Xunit;

namespace CAD2Revit.Core.Tests
{
    public class FamilyMatcherTests
    {
        static readonly string[] Types =
        {
            "Downlight : 150mm 12W",
            "Recessed Panel Light : 1200x300",
            "Recessed Panel Light : 600x600 40W",
            "Smoke Detector : Wall",
            "Smoke Detector : Ceiling",
            "Heat Detector : Standard",
            "Duplex Receptacle : Standard",
            "Lighting Switch : 1 Gang",
            "Manual Call Point : Standard",
            "Lighting and Appliance Panelboard : 400A",
            "CCTV Camera : Dome",
            "Data Outlet : Cat6 Double",
        };

        [Theory]
        [InlineData("SMOKE-DET", "Smoke Detector : Wall")]          // tie -> shorter label
        [InlineData("smoke_detector", "Smoke Detector : Wall")]
        [InlineData("LIGHT-600x600", "Recessed Panel Light : 600x600 40W")]
        [InlineData("HEAT-DET", "Heat Detector : Standard")]
        [InlineData("SKT-DOUBLE", "Duplex Receptacle : Standard")]
        [InlineData("SW-1G", "Lighting Switch : 1 Gang")]
        [InlineData("MCP", "Manual Call Point : Standard")]
        [InlineData("CCTV-CAM", "CCTV Camera : Dome")]
        [InlineData("DATA-OUTLET", "Data Outlet : Cat6 Double")]
        [InlineData("DB-PANEL", "Lighting and Appliance Panelboard : 400A")]
        public void PicksCloseMatch(string block, string expected)
        {
            int i = FamilyMatcher.BestMatch(block, Types, out var score);
            Assert.True(i >= 0, $"{block}: no match (score {score})");
            Assert.Equal(expected, Types[i]);
        }

        [Theory]
        [InlineData("TEXT-TAG")]
        [InlineData("A-DOOR-SINGLE")]
        [InlineData("*U123")]
        [InlineData("")]
        [InlineData("LIGHT-XYZ-999-ABC")]   // only 1 of 4 tokens matches
        public void NoMatchForUnrelatedBlocks(string block)
        {
            Assert.Equal(-1, FamilyMatcher.BestMatch(block, Types, out _));
        }
    }

    public class ProjectStoreTests
    {
        [Fact]
        public void KeyIsStableAndDistinct()
        {
            var a = ProjectStore.KeyFor(@"C:\Jobs\Tower A\MEP_Model.rvt", "MEP_Model");
            Assert.Equal(a, ProjectStore.KeyFor(@"c:\jobs\tower a\mep_model.rvt", "x"), ignoreCase: true);   // same file on Windows
            Assert.StartsWith("MEP_Model_", a);
            Assert.NotEqual(a, ProjectStore.KeyFor(@"C:\Jobs\Tower B\MEP_Model.rvt", "MEP_Model"));
            Assert.StartsWith("Project1_", ProjectStore.KeyFor("", "Project1"));
            Assert.StartsWith("Hospital_E_", ProjectStore.KeyFor("RSN://srv/Hospital_E.rvt", "t"));
            Assert.DoesNotContain(":", ProjectStore.KeyFor("", "a:b*c?"));
            Assert.EndsWith(".xlsx", ProjectStore.MappingPathFor(a));
        }
    }

    public class MappingSaveTests
    {
        [Theory]
        [InlineData("m.xlsx")]
        [InlineData("m.csv")]
        public void SaveAndLoadKeepsSkipsHostsAndNumbers(string file)
        {
            var dir = Path.Combine(Path.GetTempPath(), "c2r_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, file);
            Mapping.Save(path, new List<MapRow>
            {
                new MapRow { Block = "SMOKE-DET", Family = "Smoke Detector", TypeName = "Ceiling", OffsetMm = 2800, RotationDeg = 0, Host = HostMode.Ceiling },
                new MapRow { Block = "SKT", Family = "Duplex Receptacle", TypeName = "Standard", OffsetMm = 300.5, RotationDeg = 90, Host = HostMode.Wall },
                new MapRow { Block = "TEXT-TAG", Family = "", TypeName = "" },
            });
            var m = Mapping.Load(path);
            Assert.Empty(m.Errors);
            Assert.Equal(2, m.Rows.Count);
            Assert.Equal(HostMode.Ceiling, m.Rows["smoke-det"].Host);
            Assert.Equal(300.5, m.Rows["SKT"].OffsetMm);
            Assert.Equal(90, m.Rows["SKT"].RotationDeg);
            Assert.Equal(HostMode.Wall, m.Rows["SKT"].Host);
            Assert.Contains("TEXT-TAG", m.SkippedBlocks);
            Directory.Delete(dir, true);
        }

        [Fact]
        public void HostTextRoundTrips()
        {
            foreach (HostMode h in Enum.GetValues(typeof(HostMode)))
                Assert.Equal(h, Mapping.ParseHost(Mapping.HostText(h)));
            Assert.Null(Mapping.ParseHost("roof"));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using CAD2Revit.Core;
using Xunit;

namespace CAD2Revit.Core.Tests
{
    public class BlockCategoryTests
    {
        [Theory]
        // Real simplified names from the EL101 Revit-exported DWG
        [InlineData("MAAP_Ceiling Mounted Luminaire - F1", BlockCategories.Electrical)]
        [InlineData("MAAP_DECORATIVE CHANDLIER - CH1", BlockCategories.Electrical)]
        [InlineData("254409 - Secondary Toilet", BlockCategories.Plumbing)]
        [InlineData("Casework 16 - Casework 1", BlockCategories.Architectural)]
        [InlineData("Center Opening Elevator Door - ELEVATOR DOOR 1000 x 2200", BlockCategories.Architectural)]
        [InlineData("Elevator - 3D - 2000 x 2100 mm", BlockCategories.Architectural)]
        [InlineData("Grid - M_Grid Head - Circle", BlockCategories.Annotation)]
        [InlineData("LOUVER5 - LOUVER", BlockCategories.Architectural)]
        [InlineData("inclined3 - inclined", BlockCategories.Architectural)]
        [InlineData("MAAP_120_30 - F8", BlockCategories.Other)]
        // Typical CAD electrical blocks
        [InlineData("SMOKE-DET", BlockCategories.Electrical)]
        [InlineData("SKT-DOUBLE", BlockCategories.Electrical)]
        [InlineData("SW-1G", BlockCategories.Electrical)]
        [InlineData("MCP", BlockCategories.Electrical)]
        [InlineData("DB-PANEL", BlockCategories.Electrical)]
        [InlineData("CCTV-CAM", BlockCategories.Electrical)]
        [InlineData("EXIT-SIGN", BlockCategories.Electrical)]
        [InlineData("WALL LIGHT", BlockCategories.Electrical)]
        // Things that must NOT be electrical
        [InlineData("FIRE DOOR", BlockCategories.Architectural)]
        [InlineData("EXIT DOOR", BlockCategories.Architectural)]
        [InlineData("WATER HEATER", BlockCategories.Plumbing)]
        [InlineData("SUPPLY DIFFUSER 600", BlockCategories.Mechanical)]
        [InlineData("COLUMN C1", BlockCategories.Structural)]
        [InlineData("NORTH ARROW", BlockCategories.Annotation)]
        [InlineData("*U123", BlockCategories.Other)]
        [InlineData("", BlockCategories.Other)]
        public void Classify(string name, string expected)
        {
            Assert.Equal(expected, BlockCategories.Classify(name));
        }

        [Fact]
        public void OrderAndNormalize()
        {
            Assert.Equal(0, BlockCategories.Order("electrical"));
            Assert.True(BlockCategories.Order("Architectural") > BlockCategories.Order("Plumbing"));
            Assert.Equal("Electrical", BlockCategories.Normalize(" electrical "));
            Assert.Equal("Other", BlockCategories.Normalize("Landscape"));
            Assert.Null(BlockCategories.Normalize(""));
            Assert.Equal("Electrical", BlockCategories.FromRevitCategory("Lighting Fixtures"));
            Assert.Equal("Electrical", BlockCategories.FromRevitCategory("Fire Alarm Devices"));
            Assert.Null(BlockCategories.FromRevitCategory("Generic Models"));
        }
    }

    public class LevelColumnTests
    {
        [Fact]
        public void LevelAndCategoryAreSavedAndLoaded()
        {
            var path = Path.Combine(Path.GetTempPath(), "c2r_" + Guid.NewGuid().ToString("N") + ".xlsx");
            Mapping.Save(path, new List<MapRow>
            {
                new MapRow { Block = "LIGHT", Family = "L", TypeName = "T", LevelName = "Level 2", OffsetMm = 2800, Category = "Electrical" },
                new MapRow { Block = "DOOR", Family = "", TypeName = "" },   // skipped, category auto
            });
            var m = Mapping.Load(path);
            var header = Tables.ReadXlsxRows(path)[0];
            File.Delete(path);
            Assert.Empty(m.Errors);
            Assert.Equal("Level 2", m.Rows["LIGHT"].LevelName);
            Assert.Equal("Electrical", m.Rows["LIGHT"].Category);
            Assert.Equal("Architectural", m.Categories["DOOR"]);
            Assert.Contains("DOOR", m.SkippedBlocks);
            // Level sits right before the elevation column.
            Assert.Equal(header.IndexOf("Level") + 1, header.IndexOf("Offset_From_Level_mm"));
        }

        [Fact]
        public void OldFilesWithoutLevelUseTheRunLevel()
        {
            var m = Mapping.Parse(new List<Dictionary<string, string>>
            {
                new Dictionary<string, string> { ["CAD_Block_Name"] = "A", ["Revit_Family_Name"] = "F", ["Revit_Type_Name"] = "T" },
            });
            Assert.Equal("", m.Rows["A"].LevelName);
            Assert.Equal("", m.Rows["A"].Category);
        }
    }
}

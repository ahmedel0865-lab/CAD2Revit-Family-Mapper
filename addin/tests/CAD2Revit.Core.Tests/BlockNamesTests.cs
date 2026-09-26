using System.Collections.Generic;
using System.Linq;
using CAD2Revit.Core;
using Xunit;

namespace CAD2Revit.Core.Tests
{
    public class BlockNamesTests
    {
        // Real names from a Revit-exported DWG (EL101 ground floor lighting plan).
        const string P = "EL101-GROUND FLOOR LIGHTING PLAN.dwg.";
        const string V = "-GROUND FLOOR LIGHTING PLAN";
        static readonly string[] Revit =
        {
            P + "254409 - Secondary Toilet-4297264" + V,
            P + "254409 - Secondary Toilet-4297607" + V,
            P + "254409 - Secondary Toilet-V7" + V,
            P + "254409 - Secondary Toilet-V8" + V,
            P + "Casework 16 - Casework 1-3598564" + V,
            P + "Casework 17 - Casework 1-3598896" + V,
            P + "Grid - M_Grid Head - Circle-2340036" + V,
            P + "MAAP_120_30 - F8-6293193" + V,
            P + "MAAP_Ceiling Mounted Luminaire - Ceiling Luminaire_dwg-7107099" + V,
            P + "MAAP_Ceiling Mounted Luminaire - F1-7107100" + V,
            P + "MAAP_Ceiling Mounted Luminaire - F1-7107188" + V,
            P + "MAAP_DECORATIVE CHANDLIER - CH1-7112482" + V,
        };

        [Fact]
        public void RevitExportedNamesAreGrouped()
        {
            var map = BlockNames.Simplify(Revit);
            Assert.Equal("254409 - Secondary Toilet", map[Revit[0]]);
            Assert.Equal("254409 - Secondary Toilet", map[Revit[1]]);
            Assert.Equal("254409 - Secondary Toilet-V7", map[Revit[2]]);     // a different type, kept apart
            Assert.Equal("Casework 16 - Casework 1", map[Revit[4]]);
            Assert.Equal("Grid - M_Grid Head - Circle", map[Revit[6]]);
            Assert.Equal("MAAP_120_30 - F8", map[Revit[7]]);
            Assert.Equal("MAAP_Ceiling Mounted Luminaire - Ceiling Luminaire_dwg", map[Revit[8]]);
            Assert.Equal("MAAP_Ceiling Mounted Luminaire - F1", map[Revit[9]]);
            Assert.Equal(map[Revit[9]], map[Revit[10]]);
            Assert.Equal(10, map.Values.Distinct().Count());  // 12 raw names -> 10 rows
        }

        [Fact]
        public void PlainAutoCadNamesOnlyLoseTheFilePrefix()
        {
            var raw = new[] { "plan.dwg.SMOKE-DET", "plan.dwg.LIGHT-600x600", "plan.dwg.DL-1200-X", "plan.dwg.SKT-DOUBLE", "*U12" };
            var map = BlockNames.Simplify(raw);
            Assert.Equal(new[] { "SMOKE-DET", "LIGHT-600x600", "DL-1200-X", "SKT-DOUBLE", "*U12" }, raw.Select(r => map[r]).ToArray());
        }

        [Fact]
        public void SimplifiedNamesMatchFamilies()
        {
            var name = BlockNames.Simplify(Revit)[Revit[9]];
            var types = new List<string> { "MAAP_Ceiling Mounted Luminaire : Ceiling Luminaire_dwg", "MAAP_Ceiling Mounted Luminaire : F1", "MAAP_DECORATIVE CHANDLIER : CH1" };
            Assert.Equal(1, FamilyMatcher.BestMatch(name, types, out _));
        }
    }
}

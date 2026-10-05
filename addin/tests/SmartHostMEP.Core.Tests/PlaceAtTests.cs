using System;
using SmartHostMEP.Core;
using Xunit;

namespace SmartHostMEP.Core.Tests
{
    /// <summary>Blocks drawn away from their base point (Place At) and mirrored blocks (facing).</summary>
    public class PlaceAtTests
    {
        [Theory]
        [InlineData("", PlaceAt.SymbolCentre)]
        [InlineData("Base point", PlaceAt.BasePoint)]
        [InlineData("insertion point", PlaceAt.BasePoint)]
        [InlineData("Symbol centre", PlaceAt.SymbolCentre)]
        [InlineData("symbol center", PlaceAt.SymbolCentre)]
        [InlineData("CENTER", PlaceAt.SymbolCentre)]
        public void ParsesPlaceAt(string text, PlaceAt expected) => Assert.Equal(expected, Mapping.ParsePlaceAt(text));

        [Fact]
        public void UnknownPlaceAtIsNull() => Assert.Null(Mapping.ParsePlaceAt("left corner"));

        [Fact]
        public void SymbolCentreIsTheDefault()
        {
            Assert.Equal(PlaceAt.SymbolCentre, new MapRow().PlaceAt);
            // A mapping saved before Place_At existed (no column) loads as Symbol centre.
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c2r_placeat_old_" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                System.IO.File.WriteAllText(path, "CAD_Block_Name,Revit_Family_Name,Revit_Type_Name\nSOCKET,F,T\n");
                var m = Mapping.Load(path);
                Assert.Empty(m.Errors);
                Assert.Equal(PlaceAt.SymbolCentre, m.Rows["SOCKET"].PlaceAt);
            }
            finally { try { System.IO.File.Delete(path); } catch { } }
        }

        [Fact]
        public void PlaceAtIsSavedAndLoaded()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c2r_placeat_" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                Mapping.Save(path, new[]
                {
                    new MapRow { Block = "WALL-LIGHT", Family = "F", TypeName = "T", Host = HostMode.Vertical, PlaceAt = PlaceAt.SymbolCentre },
                    new MapRow { Block = "SOCKET", Family = "F", TypeName = "T", Host = HostMode.Wall, PlaceAt = PlaceAt.BasePoint },
                });
                var m = Mapping.Load(path);
                Assert.Empty(m.Errors);
                Assert.Equal(PlaceAt.SymbolCentre, m.Rows["WALL-LIGHT"].PlaceAt);
                Assert.Equal(PlaceAt.BasePoint, m.Rows["SOCKET"].PlaceAt);
            }
            finally { try { System.IO.File.Delete(path); } catch { } }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(90)]
        [InlineData(180)]
        [InlineData(270)]
        [InlineData(37)]
        public void NormalBlockFacesItsPlusY(double deg)
        {
            double a = deg * Math.PI / 180;
            // Normal block: X = (cos, sin), Y = (-sin, cos).
            double fa = VerticalPlacement.FacingAngleFromYAxis(-Math.Sin(a), Math.Cos(a));
            var f = VerticalPlacement.Facing(fa);
            Assert.True(Math.Abs(f.X + Math.Sin(a)) < 1e-9 && Math.Abs(f.Y - Math.Cos(a)) < 1e-9);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(90)]
        [InlineData(215)]
        public void MirroredBlockFacesItsRealPlusY(double deg)
        {
            double a = deg * Math.PI / 180;
            // Mirrored block (Y flipped): X = (cos, sin), Y = (sin, -cos). The device must face the
            // flipped +Y, i.e. the side the mirrored symbol is drawn on - not the rotation's +Y.
            double yx = Math.Sin(a), yy = -Math.Cos(a);
            var f = VerticalPlacement.Facing(VerticalPlacement.FacingAngleFromYAxis(yx, yy));
            Assert.True(Math.Abs(f.X - yx) < 1e-9 && Math.Abs(f.Y - yy) < 1e-9);
            var fromRotation = VerticalPlacement.Facing(a);
            Assert.True(f.Dot(fromRotation) < -0.999);   // the old behaviour faced the opposite way
        }
    }
}

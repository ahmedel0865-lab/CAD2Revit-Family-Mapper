using SmartHostMEP.Core;
using Xunit;

namespace SmartHostMEP.Core.Tests
{
    public class ExistingIndexTests
    {
        const double Tol = 50 / 304.8;   // 50 mm in feet

        [Theory]
        [InlineData("CAD: LIGHT-01", "LIGHT-01")]
        [InlineData("CAD2Revit: Host = Reference Plane | CAD: SMOKE DET", "SMOKE DET")]   // older versions
        [InlineData("SmartHost: SMOKE DET", "SMOKE DET")]
        [InlineData("SmartHost: Host = Reference Plane | SmartHost: SMOKE DET", "SMOKE DET")]
        [InlineData("SmartHost: Host = Reference Plane", null)]
        [InlineData("  cad: A  ", "A")]
        [InlineData("some note", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        [InlineData("CAD: ", null)]
        public void BlockFromComment(string comments, string expected) =>
            Assert.Equal(expected, ExistingIndex.BlockFromComment(comments));

        [Fact]
        public void SameFamilyAtSameSpotIsFound()
        {
            var ix = new ExistingIndex(Tol);
            ix.Add(7, null, 10, 20, 3);
            Assert.True(ix.Contains(7, "X", 10.1, 20, 0, 10));
            Assert.False(ix.Contains(8, "X", 10, 20, 0, 10));      // other family, no comment
            Assert.False(ix.Contains(7, "X", 11, 20, 0, 10));      // 300 mm away
            Assert.False(ix.Contains(7, "X", 10, 20, 5, 10));      // other level band
        }

        [Fact]
        public void CadCommentMatchesEvenWithAnotherFamily()
        {
            var ix = new ExistingIndex(Tol);
            ix.Add(7, "CAD2Revit: Host = Reference Plane | CAD: LIGHT-01", 10, 20, 3);
            Assert.True(ix.Contains(99, "light-01", 10, 20, 0, 10));
            Assert.False(ix.Contains(99, "LIGHT-02", 10, 20, 0, 10));
            Assert.False(ix.Contains(99, null, 10, 20, 0, 10));
        }

        [Fact]
        public void UnknownFamilyOnlyCountsByComment()
        {
            var ix = new ExistingIndex(Tol);
            ix.Add(0, "CAD: A", 1, 1, 0);
            Assert.False(ix.Contains(0, "B", 1, 1, -1, 1));
            Assert.True(ix.Contains(5, "A", 1, 1, -1, 1));
        }

        [Fact]
        public void WarningText()
        {
            Assert.Equal("1 element already exists at these locations.", ExistingIndex.Warning(1));
            Assert.Equal("12 elements already exist at these locations.", ExistingIndex.Warning(12));
        }
    }
}

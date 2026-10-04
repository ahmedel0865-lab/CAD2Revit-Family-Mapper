using SmartHostMEP.Core;
using Xunit;

namespace SmartHostMEP.Core.Tests
{
    public class HostCheckTests
    {
        [Theory]
        [InlineData(HostKind.ReferencePlane, "failed - not hosted on linked slab: Host is Reference Plane")]
        [InlineData(HostKind.Level, "failed - not hosted on linked slab: Host is Level")]
        [InlineData(HostKind.None, "failed - not hosted on linked slab: Host is none")]
        [InlineData(HostKind.Element, "failed - not hosted on linked slab: Host is an element of this model, not the Revit link")]
        public void LinkedSlabNotOnLinkFails(HostKind actual, string expected)
        {
            Assert.Equal(expected, HostCheck.Problem(actual, true, true, "slab"));
        }

        [Fact]
        public void LinkedSlabOnLinkWithFaceIsOk()
        {
            Assert.Null(HostCheck.Problem(HostKind.LinkInstance, true, true, "slab"));
        }

        [Fact]
        public void MissingHostFaceFails()
        {
            Assert.Equal("failed - not hosted on linked ceiling: the instance has no host face",
                         HostCheck.Problem(HostKind.LinkInstance, true, false, "ceiling"));
        }

        [Fact]
        public void HostModelSlab()
        {
            Assert.Null(HostCheck.Problem(HostKind.Element, false, true, "slab"));
            Assert.Equal("failed - not hosted on slab: Host is Reference Plane",
                         HostCheck.Problem(HostKind.ReferencePlane, false, true, "slab"));
        }

        [Theory]
        [InlineData(HostMode.SlabAbove, "slab")]
        [InlineData(HostMode.SlabBelow, "slab")]
        [InlineData(HostMode.Ceiling, "ceiling")]
        [InlineData(HostMode.Face, "face")]
        [InlineData(HostMode.Wall, "wall")]
        public void What(HostMode mode, string expected) => Assert.Equal(expected, HostCheck.What(mode));

        [Fact]
        public void NotFaceBasedMessage()
        {
            Assert.StartsWith("family is not face-based (placement type OneLevelBased)",
                              HostCheck.NotFaceBased("OneLevelBased", "slab"));
        }

        [Fact]
        public void DebugLineLinked()
        {
            Assert.Equal("DEBUG linked=yes link=STR.rvt element=123456 (Floors) normal=(0.000,0.000,-1.000); " +
                         "host=Revit link STR.rvt, host face ok",
                         HostCheck.DebugLine(true, true, "STR.rvt", 123456, "Floors", new[] { 0.0, 0, -1 },
                                             "Revit link STR.rvt, host face ok"));
        }

        [Fact]
        public void DebugLineNoHost()
        {
            Assert.Equal("DEBUG no host face found; host=Reference Plane SmartHost MEP (no face host)",
                         HostCheck.DebugLine(false, false, null, null, null, null, "Reference Plane SmartHost MEP (no face host)"));
            Assert.Equal("DEBUG linked=no element=7 (Ceilings) normal=?; host=none",
                         HostCheck.DebugLine(true, false, "", 7, "Ceilings", null, ""));
        }
    }
}

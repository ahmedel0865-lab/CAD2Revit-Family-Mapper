using Autodesk.Revit.DB;

namespace CAD2Revit.Revit
{
    /// <summary>Revit API differences between 2022 and 2026.</summary>
    public static class Compat
    {
        /// <summary>ElementId as a number. Revit 2024 added the 64-bit Value and
        /// Revit 2026 removed the old IntegerValue.</summary>
        public static long IdValue(ElementId id)
        {
#if REVIT2024_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }
    }
}

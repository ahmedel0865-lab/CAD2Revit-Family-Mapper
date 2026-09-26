using System;
using System.Collections.Generic;
using System.Linq;

namespace CAD2Revit.Core
{
    /// <summary>Formats rows as a fixed-width text table (for the result window).</summary>
    public static class TextTable
    {
        public static string Format(IList<string> header, IEnumerable<IList<object>> rows)
        {
            var all = new List<string[]> { header.ToArray() };
            all.AddRange(rows.Select(r => r.Select(Tables.ToText).ToArray()));
            int cols = header.Count;
            var widths = Enumerable.Range(0, cols).Select(c => all.Max(r => c < r.Length ? r[c].Length : 0)).ToArray();
            string Line(string[] r) => string.Join("  ", Enumerable.Range(0, cols)
                .Select(c => (c < r.Length ? r[c] : "").PadRight(widths[c]))).TrimEnd();
            var lines = new List<string> { Line(all[0]), string.Join("  ", widths.Select(w => new string('-', w))) };
            lines.AddRange(all.Skip(1).Select(Line));
            return string.Join(Environment.NewLine, lines);
        }
    }
}

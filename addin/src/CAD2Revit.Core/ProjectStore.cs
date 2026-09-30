using System;
using System.IO;
using System.Linq;
using System.Text;

namespace CAD2Revit.Core
{
    /// <summary>
    /// Where the last mapping of each Revit project is remembered:
    /// %AppData%\CAD2Revit\projects\&lt;project name&gt;_&lt;hash&gt;.xlsx
    /// The hash (of the full model path) keeps projects with the same file name apart.
    /// </summary>
    public static class ProjectStore
    {
        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CAD2Revit", "projects");

        /// <summary>Stable key for a model (central path for workshared models, else file
        /// path, else the title of an unsaved model).</summary>
        public static string KeyFor(string modelPath, string title)
        {
            var basis = string.IsNullOrWhiteSpace(modelPath) ? (title ?? "Untitled") : modelPath;
            basis = basis.Trim();
            var name = basis.Replace('\\', '/');
            name = name.Substring(name.LastIndexOf('/') + 1);
            if (name.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            var invalid = Path.GetInvalidFileNameChars().Concat(new[] { ':', '*', '?', '"', '<', '>', '|' }).ToArray();
            var clean = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim();
            if (clean.Length == 0) clean = "project";
            if (clean.Length > 60) clean = clean.Substring(0, 60);
            return clean + "_" + Fnv1a(basis.ToLowerInvariant()).ToString("x8");
        }

        public static string MappingPathFor(string key) => Path.Combine(Folder, key + ".xlsx");

        /// <summary>FNV-1a 32-bit: stable across runs and .NET versions (string.GetHashCode is not).</summary>
        static uint Fnv1a(string s)
        {
            uint h = 2166136261;
            foreach (var b in Encoding.UTF8.GetBytes(s))
            {
                h ^= b;
                h *= 16777619;
            }
            return h;
        }
    }
}

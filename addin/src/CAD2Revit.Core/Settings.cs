using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CAD2Revit.Core
{
    /// <summary>
    /// User settings, stored as a plain key = value file at
    /// %AppData%\CAD2Revit\settings.ini (created with defaults on first run).
    /// Edit it in Notepad; changes apply the next time a command runs.
    /// </summary>
    public class Settings
    {
        // Duplicate protection
        public double DuplicateToleranceMm = 50.0;
        public bool DuplicateSameTypeOnly = false;     // false: any type of the same family counts
        // DWG reading
        public bool IncludeNestedBlocks = false;
        // Hosting
        public double HostSearchDistanceMm = 6000.0;   // never searches past the next level
        public double WallSearchDistanceMm = 500.0;
        public bool SearchRevitLinks = true;
        public bool FallbackToUnhosted = true;
        // Output
        public bool WriteBlockNameToComments = true;
        // Remembered between runs
        public string LastMappingPath = "";

        public static string DefaultPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CAD2Revit", "settings.ini");

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static Settings Load(string path = null)
        {
            path = path ?? DefaultPath;
            var s = new Settings();
            if (!File.Exists(path))
            {
                TrySave(s, path);
                return s;
            }
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                var key = line.Substring(0, eq).Trim().ToLowerInvariant();
                var val = line.Substring(eq + 1).Trim();
                s.Apply(key, val);
            }
            return s;
        }

        void Apply(string key, string val)
        {
            double D(double old) => double.TryParse(val, NumberStyles.Float, Inv, out var d) ? d : old;
            bool B(bool old) => bool.TryParse(val, out var b) ? b : val == "1" ? true : val == "0" ? false : old;
            switch (key)
            {
                case "duplicatetolerancemm": DuplicateToleranceMm = D(DuplicateToleranceMm); break;
                case "duplicatesametypeonly": DuplicateSameTypeOnly = B(DuplicateSameTypeOnly); break;
                case "includenestedblocks": IncludeNestedBlocks = B(IncludeNestedBlocks); break;
                case "hostsearchdistancemm": HostSearchDistanceMm = D(HostSearchDistanceMm); break;
                case "wallsearchdistancemm": WallSearchDistanceMm = D(WallSearchDistanceMm); break;
                case "searchrevitlinks": SearchRevitLinks = B(SearchRevitLinks); break;
                case "fallbacktounhosted": FallbackToUnhosted = B(FallbackToUnhosted); break;
                case "writeblocknametocomments": WriteBlockNameToComments = B(WriteBlockNameToComments); break;
                case "lastmappingpath": LastMappingPath = val; break;
            }
        }

        public string ToIni()
        {
            string F(double d) => d.ToString("0.###", Inv);
            string Bo(bool b) => b ? "true" : "false";
            var sb = new StringBuilder();
            sb.AppendLine("# CAD2Revit settings - edit in Notepad, then run the command again.");
            sb.AppendLine();
            sb.AppendLine("# Existing instance closer than this (mm, in plan) on the same level = duplicate, skipped.");
            sb.AppendLine("DuplicateToleranceMm = " + F(DuplicateToleranceMm));
            sb.AppendLine("# false: any type of the same family counts as a duplicate; true: only the same type.");
            sb.AppendLine("DuplicateSameTypeOnly = " + Bo(DuplicateSameTypeOnly));
            sb.AppendLine("# Default for the 'include nested blocks' checkbox.");
            sb.AppendLine("IncludeNestedBlocks = " + Bo(IncludeNestedBlocks));
            sb.AppendLine("# Max distance (mm) above the level to look for a ceiling/slab (never past the next level).");
            sb.AppendLine("HostSearchDistanceMm = " + F(HostSearchDistanceMm));
            sb.AppendLine("# Max distance (mm) from the CAD point to a wall face for Host_Type = wall.");
            sb.AppendLine("WallSearchDistanceMm = " + F(WallSearchDistanceMm));
            sb.AppendLine("# Also host on faces in linked Revit models.");
            sb.AppendLine("SearchRevitLinks = " + Bo(SearchRevitLinks));
            sb.AppendLine("# No host found: true = place unhosted at the row offset; false = report as failed.");
            sb.AppendLine("FallbackToUnhosted = " + Bo(FallbackToUnhosted));
            sb.AppendLine("# Write 'CAD: <block name>' into each placed element's Comments.");
            sb.AppendLine("WriteBlockNameToComments = " + Bo(WriteBlockNameToComments));
            sb.AppendLine();
            sb.AppendLine("# Remembered automatically.");
            sb.AppendLine("LastMappingPath = " + LastMappingPath);
            return sb.ToString();
        }

        public static bool TrySave(Settings s, string path = null)
        {
            try
            {
                path = path ?? DefaultPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, s.ToIni(), new UTF8Encoding(false));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

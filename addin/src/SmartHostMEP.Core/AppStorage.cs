using System;
using System.IO;

namespace SmartHostMEP.Core
{
    /// <summary>
    /// The per-user data folder: %AppData%\SmartHostMEP (settings.ini, projects\). On first use,
    /// settings and saved mappings from the folder of the tool's former name are copied over once;
    /// the old folder is left untouched.
    /// </summary>
    public static class AppStorage
    {
        public const string FolderName = "SmartHostMEP";
        /// <summary>Folder used before the rename (read once, for migration only).</summary>
        public const string OldFolderName = "CAD2Revit";

        static readonly object Gate = new object();
        static bool _migrated;

        public static string Root
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var root = Path.Combine(appData, FolderName);
                lock (Gate)
                {
                    if (!_migrated)
                    {
                        _migrated = true;
                        Migrate(Path.Combine(appData, OldFolderName), root);
                    }
                }
                return root;
            }
        }

        /// <summary>Copies <paramref name="oldRoot"/> to <paramref name="newRoot"/> when the new
        /// folder does not exist yet. Existing files are never overwritten. Returns true if it copied.</summary>
        public static bool Migrate(string oldRoot, string newRoot)
        {
            try
            {
                if (Directory.Exists(newRoot) || !Directory.Exists(oldRoot)) return false;
                Copy(oldRoot, newRoot);
                return true;
            }
            catch (Exception)
            {
                return false;   // migration is a convenience: defaults are used instead
            }
        }

        static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from))
            {
                var dest = Path.Combine(to, Path.GetFileName(f));
                if (!File.Exists(dest)) File.Copy(f, dest);
            }
            foreach (var d in Directory.GetDirectories(from))
                Copy(d, Path.Combine(to, Path.GetFileName(d)));
        }
    }
}

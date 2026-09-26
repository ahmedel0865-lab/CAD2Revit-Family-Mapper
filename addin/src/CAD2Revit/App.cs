using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace CAD2Revit
{
    /// <summary>Revit start-up: creates the CAD2Revit ribbon tab with its two buttons.</summary>
    public class App : IExternalApplication
    {
        public const string TabName = "CAD2Revit";

        public Result OnStartup(UIControlledApplication app)
        {
            try { app.CreateRibbonTab(TabName); } catch (Exception) { /* tab already exists */ }
            var panel = app.CreateRibbonPanel(TabName, "Mapper");
            var dll = Assembly.GetExecutingAssembly().Location;
            var version = Assembly.GetExecutingAssembly().GetName().Version;

            AddButton(panel, "CAD2Revit.ListBlocks", "List\nBlocks", dll, typeof(Commands.ListBlocksCommand).FullName, "ListBlocks",
                "List every block name in a linked/imported DWG (with counts) and export a mapping template (XLSX or CSV).");
            AddButton(panel, "CAD2Revit.PlaceFamilies", "Place\nFamilies", dll, typeof(Commands.PlaceFamiliesCommand).FullName, "PlaceFamilies",
                "Pick a DWG, map each CAD block to a Revit family, level and host in one window, Preview, then Run. " +
                $"(version {version.ToString(3)})");

            var tools = app.CreateRibbonPanel(TabName, "Tools");
            AddButton(tools, "CAD2Revit.Settings", "Settings", dll, typeof(Commands.SettingsCommand).FullName, "Settings",
                "Open settings.ini (duplicate tolerance, host search distances, fallbacks...) in Notepad. " +
                "Changes apply the next time you run a command.");
            AddButton(tools, "CAD2Revit.Help", "Help", dll, typeof(Commands.HelpCommand).FullName, "Help",
                "User guide, version, and quick links to the logs and saved project mappings.");
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

        static void AddButton(RibbonPanel panel, string name, string text, string dll, string className, string icon, string tip)
        {
            var data = new PushButtonData(name, text, dll, className)
            {
                ToolTip = tip,
                LargeImage = LoadIcon(icon + "32.png"),
                Image = LoadIcon(icon + "16.png"),
            };
            panel.AddItem(data);
        }

        static BitmapSource LoadIcon(string file)
        {
            try
            {
                var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CAD2Revit.Resources." + file);
                if (stream == null) return null;
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = stream;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}

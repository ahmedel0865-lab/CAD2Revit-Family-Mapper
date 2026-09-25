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
                "Replace DWG blocks with Revit families using a CSV/XLSX mapping file. Preview first, then Run. " +
                $"Settings: {Core.Settings.DefaultPath}   (version {version.ToString(3)})");
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

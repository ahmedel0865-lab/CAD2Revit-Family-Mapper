using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace SmartHostMEP
{
    /// <summary>Revit start-up: creates the "SmartHost MEP" ribbon tab with its buttons.</summary>
    public class App : IExternalApplication
    {
        public const string TabName = "SmartHost MEP";

        public Result OnStartup(UIControlledApplication app)
        {
            // Never fail start-up: a failed OnStartup removes the whole tab until Revit restarts.
            try
            {
                try { app.CreateRibbonTab(TabName); } catch (Exception) { /* tab already exists */ }
                var dll = Assembly.GetExecutingAssembly().Location;
                var version = Assembly.GetExecutingAssembly().GetName().Version;

                var panel = GetOrCreatePanel(app, "Host Placement");
                AddButton(panel, "SmartHostMEP.PlaceFamilies", "Place\nFamilies", dll, typeof(Commands.PlaceFamiliesCommand).FullName, "PlaceFamilies",
                    "Convert AutoCAD blocks into hosted MEP families: pick a DWG, map each block to a family, level and host " +
                    "(slab, ceiling, wall, beam, reference plane - found automatically), Preview, then Run. " +
                    $"(version {version.ToString(3)})");

                var tools = GetOrCreatePanel(app, "Tools");
                AddButton(tools, "SmartHostMEP.Settings", "Settings", dll, typeof(Commands.SettingsCommand).FullName, "Settings",
                    "Open settings.ini (duplicate tolerance, host search distances, fallbacks...) in Notepad. " +
                    "Changes apply the next time you run a command.");
                AddButton(tools, "SmartHostMEP.Help", "Help", dll, typeof(Commands.HelpCommand).FullName, "Help",
                    "SmartHost MEP user guide, version, and quick links to the logs and saved project mappings.");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("SmartHost MEP", "Could not build the SmartHost MEP ribbon tab:\n" + ex.Message);
            }
            return Result.Succeeded;
        }

        /// <summary>The panel on the SmartHost MEP tab, reused if it already exists (a second copy
        /// of the add-in may have created it first).</summary>
        static RibbonPanel GetOrCreatePanel(UIControlledApplication app, string name)
        {
            try
            {
                foreach (var p in app.GetRibbonPanels(TabName))
                    if (p.Name == name) return p;
                return app.CreateRibbonPanel(TabName, name);
            }
            catch (Exception)
            {
                return app.CreateRibbonPanel(TabName, "SmartHost MEP " + name);
            }
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
            try { panel.AddItem(data); } catch (Exception) { /* button already on this panel */ }
        }

        static BitmapSource LoadIcon(string file)
        {
            try
            {
                var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SmartHostMEP.Resources." + file);
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

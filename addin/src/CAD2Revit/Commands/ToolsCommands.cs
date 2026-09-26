using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CAD2Revit.Core;
using Settings = CAD2Revit.Core.Settings;

namespace CAD2Revit.Commands
{
    static class Shell
    {
        public const string GuideUrl = "https://github.com/ahmedel0865-lab/CAD2Revit-Family-Mapper#readme";

        public static void Open(string target, string args = null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target, args ?? "") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                TaskDialog.Show("CAD2Revit", "Could not open:\n" + target + "\n\n" + ex.Message);
            }
        }

        public static string LogsFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CAD2Revit", "Logs");
    }

    /// <summary>Opens settings.ini in Notepad (created with defaults if missing).</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class SettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            Settings.Load();   // makes sure the file exists
            Shell.Open("notepad.exe", "\"" + Settings.DefaultPath + "\"");
            return Result.Succeeded;
        }
    }

    /// <summary>Version, user guide, and quick links to the logs / settings / saved mappings.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class HelpCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            var td = new TaskDialog("CAD2Revit - Help")
            {
                MainInstruction = "CAD2Revit Family Mapper " + version,
                MainContent =
                    "1.  List Blocks: see every block in a DWG and export a mapping template.\n" +
                    "2.  Place Families: pick the DWG, map each block to a family, level and host, " +
                    "Preview, then Run (one Ctrl+Z undoes a run).\n\n" +
                    "Tip: select several rows in the mapping window to set Host Type, Level or Elevation for all of them.",
                FooterText = "Settings: " + Settings.DefaultPath,
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open the user guide", "README and step-by-step guide on GitHub");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Open the logs folder", Shell.LogsFolder);
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Open the saved project mappings", ProjectStore.Folder);
            switch (td.Show())
            {
                case TaskDialogResult.CommandLink1:
                    Shell.Open(Shell.GuideUrl);
                    break;
                case TaskDialogResult.CommandLink2:
                    Directory.CreateDirectory(Shell.LogsFolder);
                    Shell.Open(Shell.LogsFolder);
                    break;
                case TaskDialogResult.CommandLink3:
                    Directory.CreateDirectory(ProjectStore.Folder);
                    Shell.Open(ProjectStore.Folder);
                    break;
            }
            return Result.Succeeded;
        }
    }
}

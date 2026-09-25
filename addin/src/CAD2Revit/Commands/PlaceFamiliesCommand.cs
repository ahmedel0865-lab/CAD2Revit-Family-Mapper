using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CAD2Revit.Core;
using CAD2Revit.Revit;
using CAD2Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace CAD2Revit.Commands
{
    /// <summary>Dialog -> read mapping -> read DWG blocks -> place (or preview) -> summary + CSV log.</summary>
    [Transaction(TransactionMode.Manual)]
    public class PlaceFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = data.Application.ActiveUIDocument.Document;
            try
            {
                var settings = Core.Settings.Load();
                if (Items.Imports(doc).Count == 0)
                {
                    TaskDialog.Show("CAD2Revit", "No linked or imported DWG found in this project.");
                    return Result.Cancelled;
                }

                // 1. Dialog.
                PlaceOptions opts;
                using (var dlg = new PlaceDialog(doc, settings))
                {
                    if (dlg.ShowDialog() != DialogResult.OK || dlg.Result == null) return Result.Cancelled;
                    opts = dlg.Result;
                }
                settings.LastMappingPath = opts.MappingPath;
                Core.Settings.TrySave(settings);

                // 2. Mapping file.
                var mapping = Mapping.Load(opts.MappingPath);
                var errors = new List<string>(mapping.Errors);
                if (mapping.Rows.Count == 0)
                {
                    TaskDialog.Show("CAD2Revit", "No valid rows in the mapping file.\n\n" + string.Join("\n", errors));
                    return Result.Cancelled;
                }
                var symbols = Placer.ResolveSymbols(doc, mapping, errors);

                // 3. Blocks from the DWG.
                var blocks = DwgReader.Read(doc, opts.Import, opts.IncludeNested);
                if (blocks.Count == 0)
                {
                    TaskDialog.Show("CAD2Revit", "No blocks found in the selected DWG.");
                    return Result.Cancelled;
                }

                // 4. Place (Preview = same work, rolled back at the end).
                var results = new Placer(doc, settings).PlaceAll(blocks, mapping, symbols, opts.Level, opts.Preview);

                // 5. CSV log next to the mapping file (Documents if that folder is read-only).
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var name = $"cad2revit_{(opts.Preview ? "preview" : "log")}_{stamp}.csv";
                string logPath = null;
                foreach (var folder in new[] { Path.GetDirectoryName(opts.MappingPath),
                                               Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
                {
                    try
                    {
                        var p = Path.Combine(folder, name);
                        Tables.WriteCsv(p, Report.LogHeader, Report.LogRows(results));
                        logPath = p;
                        break;
                    }
                    catch (Exception) { }
                }

                // 6. Summary window.
                var summary = Report.Summarize(results);
                var text = Report.SummaryText(summary, opts.Preview);
                if (errors.Count > 0)
                    text = "Mapping warnings:\r\n  - " + string.Join("\r\n  - ", errors) + "\r\n\r\n" + text;
                text += "\r\n\r\n" + (logPath != null ? "Log saved: " + logPath : "Could not write the log file.");
                if (!opts.Preview && summary.Get(Status.Placed) > 0)
                    text += "\r\nUndo the whole run with a single Ctrl+Z (\"CAD2Revit: Place families\").";
                using (var form = new ResultForm(opts.Preview ? "CAD2Revit - Preview" : "CAD2Revit - Done", text, logPath))
                    form.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("CAD2Revit - error", ex.ToString());
                return Result.Failed;
            }
        }
    }
}

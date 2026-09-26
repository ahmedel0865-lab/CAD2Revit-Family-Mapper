using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CAD2Revit.Core;
using CAD2Revit.Revit;
using CAD2Revit.UI;
using DialogResult = System.Windows.Forms.DialogResult;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace CAD2Revit.Commands
{
    /// <summary>
    /// 1. Pick DWG + level.  2. Mapping window (one row per block name).
    /// 3. Preview (rolled back, then back to the mapping window) or Run.
    /// 4. Summary window + CSV log. The grid is remembered per project.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class PlaceFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var uiapp = data.Application;
            var doc = uiapp.ActiveUIDocument.Document;
            try
            {
                var settings = Core.Settings.Load();
                if (Items.Imports(doc).Count == 0)
                {
                    TaskDialog.Show("CAD2Revit", "No linked or imported DWG found in this project.");
                    return Result.Cancelled;
                }

                // 1. DWG (the starting level comes from the DWG; rows can change it).
                PlaceOptions opts;
                using (var dlg = new PlaceDialog(doc, settings))
                {
                    if (dlg.ShowDialog() != DialogResult.OK || dlg.Result == null) return Result.Cancelled;
                    opts = dlg.Result;
                }
                if (opts.Level == null)
                {
                    TaskDialog.Show("CAD2Revit", "This model has no levels.");
                    return Result.Cancelled;
                }

                // 2. Blocks from the DWG.
                var blocks = DwgReader.Read(doc, opts.Import, opts.IncludeNested);
                if (settings.SimplifyBlockNames) DwgReader.SimplifyNames(blocks);
                if (blocks.Count == 0)
                {
                    TaskDialog.Show("CAD2Revit", "No blocks found in the selected DWG.\n\n" +
                        "If the drawing was exploded before linking, it contains no blocks.");
                    return Result.Cancelled;
                }

                var session = BuildSession(doc, opts, blocks, settings, out var startupNotes);

                // 3. Mapping window -> Preview / Run loop.
                while (true)
                {
                    var win = new MappingWindow(session);
                    new WindowInteropHelper(win).Owner = uiapp.MainWindowHandle;
                    if (startupNotes != null)
                    {
                        var notes = startupNotes;
                        win.ContentRendered += (s, e) => System.Windows.MessageBox.Show(win, notes, "CAD2Revit");
                        startupNotes = null;
                    }
                    win.ShowDialog();
                    if (win.Action == MappingAction.Cancel)
                        return Result.Cancelled;
                    bool preview = win.Action == MappingAction.Preview;

                    // Remember this grid for the project (next time it is pre-filled).
                    try
                    {
                        Directory.CreateDirectory(ProjectStore.Folder);
                        Mapping.Save(session.ProjectMappingPath, session.Rows.Select(r => r.ToMapRow()));
                    }
                    catch (Exception) { /* remembering is a convenience only */ }

                    var mapping = session.ToMapping();
                    var errors = new List<string>();
                    var symbols = Placer.ResolveSymbols(doc, mapping, errors);
                    var results = new Placer(doc, settings).PlaceAll(blocks, mapping, symbols, opts.Level, preview,
                                                                    dwgExtents: DwgExtents(opts.Import));
                    var logPath = WriteLog(doc, results, preview);

                    var summary = Report.Summarize(results);
                    var text = Report.SummaryText(summary, preview);
                    if (errors.Count > 0)
                        text = "Warnings:\r\n  - " + string.Join("\r\n  - ", errors) + "\r\n\r\n" + text;
                    text += "\r\n\r\n" + (logPath != null ? "Log saved: " + logPath : "Could not write the log file.");
                    if (preview)
                        text += "\r\n\r\nClose this window to return to the mapping. Click Run there to place the families.";
                    else if (summary.Get(Status.Placed) > 0)
                        text += "\r\nUndo the whole run with a single Ctrl+Z (\"CAD2Revit: Place families\").";

                    using (var form = new ResultForm(preview ? "CAD2Revit - Preview" : "CAD2Revit - Done", text, logPath))
                        form.ShowDialog();
                    if (!preview) return Result.Succeeded;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("CAD2Revit - error", ex.ToString());
                return Result.Failed;
            }
        }

        /// <summary>One grid row per unique block name, pre-filled from the project's last
        /// mapping, then by name matching for blocks that were never mapped.</summary>
        static MappingSession BuildSession(Document doc, PlaceOptions opts, List<BlockRef> blocks,
                                           Core.Settings settings, out string notes)
        {
            notes = null;
            var session = new MappingSession
            {
                DwgName = doc.GetElement(opts.Import.GetTypeId())?.Name ?? "DWG",
                LevelName = opts.Level.Name,
                InstanceCount = blocks.Count,
                Settings = settings,
                ProjectMappingPath = ProjectStore.MappingPathFor(ProjectStore.KeyFor(ModelPath(doc), doc.Title)),
            };
            FamilyCatalog.Load(doc, session);
            session.LevelNames = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.ProjectElevation).Select(l => l.Name).ToList();
            foreach (var kv in DwgReader.CountByName(blocks).OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                session.Rows.Add(new BlockRow(kv.Key, kv.Value, session.Options, opts.Level.Name));

            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(session.ProjectMappingPath))
            {
                var saved = Mapping.Load(session.ProjectMappingPath);
                var (_, messages) = session.Apply(saved);
                known.UnionWith(saved.Rows.Keys);
                known.UnionWith(saved.SkippedBlocks);
                var missing = messages.Where(m => m.Contains("is not loaded")).ToList();
                if (missing.Count > 0)
                    notes = "Some families from this project's last mapping are no longer loaded:\n\n" +
                            string.Join("\n", missing.Take(15)) + (missing.Count > 15 ? "\n..." : "");
            }
            session.AutoMatch(row => !known.Contains(row.BlockName));
            foreach (var row in session.Rows) MappingSession.UseFamilyCategory(row);
            return session;
        }

        /// <summary>Plan extents of the DWG link (minX, minY, maxX, maxY in feet), or null.</summary>
        static double[] DwgExtents(ImportInstance import)
        {
            try
            {
                var bb = import.get_BoundingBox(null);
                if (bb == null) return null;
                var tf = bb.Transform ?? Transform.Identity;
                var corners = new[]
                {
                    tf.OfPoint(new XYZ(bb.Min.X, bb.Min.Y, 0)), tf.OfPoint(new XYZ(bb.Max.X, bb.Min.Y, 0)),
                    tf.OfPoint(new XYZ(bb.Min.X, bb.Max.Y, 0)), tf.OfPoint(new XYZ(bb.Max.X, bb.Max.Y, 0)),
                };
                return new[] { corners.Min(c => c.X), corners.Min(c => c.Y), corners.Max(c => c.X), corners.Max(c => c.Y) };
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Central model path for workshared models, else the file path ("" if unsaved).</summary>
        static string ModelPath(Document doc)
        {
            try
            {
                if (doc.IsWorkshared)
                {
                    var central = doc.GetWorksharingCentralModelPath();
                    if (central != null) return ModelPathUtils.ConvertModelPathToUserVisiblePath(central);
                }
            }
            catch (Exception) { }
            return doc.PathName ?? "";
        }

        /// <summary>CSV log in Documents\CAD2Revit\Logs (temp folder if that fails).</summary>
        static string WriteLog(Document doc, List<PlacementResult> results, bool preview)
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var project = ProjectStore.KeyFor(ModelPath(doc), doc.Title);
            var name = $"cad2revit_{(preview ? "preview" : "log")}_{stamp}.csv";
            foreach (var folder in new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CAD2Revit", "Logs", project),
                Path.Combine(Path.GetTempPath(), "CAD2Revit"),
            })
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    var p = Path.Combine(folder, name);
                    Tables.WriteCsv(p, Report.LogHeader, Report.LogRows(results));
                    return p;
                }
                catch (Exception) { }
            }
            return null;
        }
    }
}

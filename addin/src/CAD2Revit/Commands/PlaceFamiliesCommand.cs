using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            RevitOwner.MainHandle = uiapp.MainWindowHandle;
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
                    if (dlg.ShowDialog(RevitOwner.Win32) != DialogResult.OK || dlg.Result == null) return Result.Cancelled;
                    opts = dlg.Result;
                }
                if (opts.Level == null)
                {
                    TaskDialog.Show("CAD2Revit", "This model has no levels.");
                    return Result.Cancelled;
                }

                // 2. Blocks from the DWG.
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var blocks = DwgReader.Read(doc, opts.Import, opts.IncludeNested);
                if (settings.SimplifyBlockNames) DwgReader.SimplifyNames(blocks);
                var readDwgTime = sw.Elapsed;
                if (blocks.Count == 0)
                {
                    TaskDialog.Show("CAD2Revit", "No blocks found in the selected DWG.\n\n" +
                        "If the drawing was exploded before linking, it contains no blocks.");
                    return Result.Cancelled;
                }

                sw.Restart();
                var session = BuildSession(doc, opts, blocks, settings);
                var setupTime = sw.Elapsed;

                // 3. Mapping window -> Preview / Run loop.
                while (true)
                {
                    var win = new MappingWindow(session);
                    RevitOwner.Attach(win);
                    win.ShowDialog();
                    if (win.Action == MappingAction.Cancel)
                        return Result.Cancelled;
                    bool preview = win.Action == MappingAction.Preview;

                    // Remember this grid for the project (next time it is pre-filled).
                    try
                    {
                        Directory.CreateDirectory(ProjectStore.Folder);
                        Mapping.Save(session.ProjectMappingPath, session.Rows.Select(r => r.ToMapRow()), session.Slab);
                    }
                    catch (Exception) { /* remembering is a convenience only */ }

                    // Timings of this Preview/Run (DWG reading and window setup happened once, at the start).
                    var timer = new PhaseTimer();
                    timer.Add(Phases.ReadDwg, readDwgTime);
                    timer.Add(Phases.Setup, setupTime);
                    var total = System.Diagnostics.Stopwatch.StartNew();
                    MappingResult mapping;
                    Dictionary<MapRow, FamilySymbol> symbols;
                    var errors = new List<string>();
                    using (timer.Time(Phases.LoadMapping))
                    {
                        mapping = session.ToMapping();
                        symbols = Placer.ResolveSymbols(doc, mapping, errors);
                    }
                    // Run: warn when elements were already placed at these locations.
                    var placer = new Placer(doc, settings);
                    if (!preview)
                    {
                        int existing;
                        using (timer.Time(Phases.DupIndex)) existing = placer.CountExisting(blocks, mapping, symbols, opts.Level);
                        if (existing > 0)
                        {
                            var choice = AskExisting(existing);
                            if (choice == ExistingChoice.Cancel) continue;   // back to the mapping window
                            placer.CheckDuplicates = choice == ExistingChoice.Skip;
                        }
                    }
                    List<PlacementResult> results;
                    try
                    {
                        using (var progress = new ProgressWindow(preview ? "CAD2Revit - Preview" : "CAD2Revit - Placing families",
                                                                 uiapp.MainWindowHandle))
                            results = placer.PlaceAll(blocks, mapping, symbols, opts.Level, preview,
                                                                         progress.Report, DwgExtents(opts.Import), timer, opts.Import);
                    }
                    catch (OperationCanceledException)
                    {
                        TaskDialog.Show("CAD2Revit", "Cancelled. Everything was rolled back - nothing was changed in the model.");
                        continue;   // back to the mapping window
                    }
                    string logPath;
                    total.Stop();
                    timer.Add(Phases.Total, total.Elapsed + readDwgTime + setupTime);
                    using (timer.Time(Phases.WriteLog)) logPath = WriteLog(doc, results, preview, timer);
                    if (preview) session.SetDetectedHosts(results);

                    // Show detection: the wall/column faces used for vertical planes, as red detail
                    // lines. Removed afterwards unless the user keeps them.
                    DetectionLines lines = null;
                    string linesNote = null;
                    if (session.Slab.ShowDetection)
                    {
                        try
                        {
                            lines = DetectionLines.Draw(doc, uiapp.ActiveUIDocument.ActiveView, opts.Level, placer.Detected.Values);
                            if (lines != null)
                            {
                                uiapp.ActiveUIDocument.RefreshActiveView();
                                linesNote = $"Show detection: {lines.Count} red detail lines drawn in view '{lines.ViewName}'.";
                            }
                            else linesNote = "Show detection: no wall/column was detected, or no plan view to draw in.";
                        }
                        catch (Exception ex) { linesNote = "Show detection: could not draw the lines (" + ex.Message + ")."; }
                    }
                    using (lines)
                    {

                        // Details (behind "Show details"): the full technical summary, timings and log path.
                        var summary = Report.Summarize(results);
                        var text = Report.SummaryText(summary, preview);
                        if (errors.Count > 0)
                            text = "Warnings:\r\n  - " + string.Join("\r\n  - ", errors) + "\r\n\r\n" + text;
                        if (placer.DetectionSummary.Length > 0) text += "\r\n\r\n" + placer.DetectionSummary;
                        if (linesNote != null) text += "\r\n" + linesNote;
                        text += "\r\n\r\nTimings (where the time goes):\r\n" + timer.Format();
                        text += "\r\n\r\n" + (logPath != null ? "Log saved: " + logPath : "Could not write the log file.");

                        var simple = SimpleReport.From(results, preview);
                        string footnote = preview
                            ? "Close this window to return to the mapping, then click Run to place the families."
                            : summary.Get(Status.Placed) > 0 ? "Undo the whole run with a single Ctrl+Z." : null;

                        using (var form = new ResultForm(simple, text, logPath, footnote))
                            form.ShowDialog(RevitOwner.Win32);
                        if (!preview)
                        {
                            // Slab (above) blocks that found no slab/beam: list them so they can be fixed.
                            var review = NeedsReview.From(results);
                            if (review.Count > 0)
                            {
                                var uidoc = uiapp.ActiveUIDocument;
                                using (var form = new NeedsReviewForm(review, ids =>
                                {
                                    var elementIds = ids.Select(Compat.ToId).ToList();
                                    uidoc.Selection.SetElementIds(elementIds);
                                    uidoc.ShowElements(elementIds);
                                }))
                                    form.ShowDialog(RevitOwner.Win32);
                            }
                        }
                        if (lines != null && AskKeepLines(lines.Count)) lines.Keep();
                    }   // lines not kept are removed here
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

        /// <summary>"Keep the detection lines?" - Delete (default, also on Cancel) / Keep.</summary>
        static bool AskKeepLines(int count)
        {
            var td = new TaskDialog("CAD2Revit - detection lines")
            {
                MainInstruction = $"Keep the {count} detection lines?",
                MainContent = "The red detail lines show the wall faces and column sides the tool detected and used " +
                              "to orient the vertical reference planes.",
                AllowCancellation = true,
                CommonButtons = TaskDialogCommonButtons.None,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Delete them", "Remove the lines (recommended)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Keep them", "Leave the lines in the view");
            td.DefaultButton = TaskDialogResult.CommandLink1;
            return td.Show() == TaskDialogResult.CommandLink2;
        }

        enum ExistingChoice { Skip, PlaceAnyway, Cancel }

        /// <summary>"X elements already exist at these locations": Skip them (default) / Place anyway / Cancel.</summary>
        static ExistingChoice AskExisting(int count)
        {
            var td = new TaskDialog("CAD2Revit - already placed")
            {
                MainInstruction = ExistingIndex.Warning(count),
                MainContent = "An element of the same family, or one whose Comments say \"CAD: <block>\", is already " +
                              "at these block locations (probably from an earlier run).",
                AllowCancellation = true,
                CommonButtons = TaskDialogCommonButtons.None,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Skip them", "Place only the blocks that are not in the model yet (recommended)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Place anyway", "Place every block, even where an element already exists");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Cancel", "Go back to the mapping window");
            // Only after the command links exist: Revit throws "Corresponding button not found"
            // when DefaultButton names a button the dialog does not have yet.
            td.DefaultButton = TaskDialogResult.CommandLink1;
            var r = td.Show();
            return r == TaskDialogResult.CommandLink1 ? ExistingChoice.Skip
                 : r == TaskDialogResult.CommandLink2 ? ExistingChoice.PlaceAnyway
                 : ExistingChoice.Cancel;
        }

        /// <summary>One grid row per unique block name. The project's last mapping restores
        /// Elevation, Host Type, Rotation, Facing, Level and the Slab / Ceiling options, but every
        /// Revit Family starts at (Skip): families are picked again, or come from Load... / Auto-match.</summary>
        static MappingSession BuildSession(Document doc, PlaceOptions opts, List<BlockRef> blocks, Core.Settings settings)
        {
            var session = new MappingSession
            {
                DwgName = doc.GetElement(opts.Import.GetTypeId())?.Name ?? "DWG",
                LevelName = opts.Level.Name,
                InstanceCount = blocks.Count,
                Settings = settings,
                ProjectMappingPath = ProjectStore.MappingPathFor(ProjectStore.KeyFor(ModelPath(doc), doc.Title)),
            };
            session.Slab.WallSearchMm = settings.WallSearchDistanceMm > 0 ? settings.WallSearchDistanceMm : SlabOptions.DefaultWallSearchMm;
            FamilyCatalog.Load(doc, session);
            session.LevelNames = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.ProjectElevation).Select(l => l.Name).ToList();
            var symbols = DwgReader.ExtractSymbols(blocks);
            foreach (var kv in DwgReader.CountByName(blocks).OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                session.Rows.Add(new BlockRow(kv.Key, kv.Value, session.Options, opts.Level.Name)
                {
                    Symbol = symbols.TryGetValue(kv.Key, out var sym) ? sym : null,
                });

            if (File.Exists(session.ProjectMappingPath))
                session.Apply(Mapping.Load(session.ProjectMappingPath), families: false);
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
        static string WriteLog(Document doc, List<PlacementResult> results, bool preview, PhaseTimer timer = null)
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
                    var rows = Report.LogRows(results);
                    if (timer != null) rows.AddRange(timer.LogRows());   // Status = "timing"
                    Tables.WriteCsv(p, Report.LogHeader, rows);
                    return p;
                }
                catch (Exception) { }
            }
            return null;
        }
    }
}

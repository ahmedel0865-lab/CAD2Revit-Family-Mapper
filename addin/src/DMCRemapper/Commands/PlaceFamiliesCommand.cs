using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DMCRemapper.Core;
using DMCRemapper.Revit;
using DMCRemapper.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace DMCRemapper.Commands
{
    /// <summary>
    /// Opens the mapping window straight away (DWG, Level and Host default are on its setup row).
    /// Preview and Run happen inside the window; after Run the result and Needs Review windows
    /// follow. Changing the DWG (or Include nested blocks) reads the blocks again and keeps the
    /// families picked for blocks of the same name. The grid is remembered per project.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class PlaceFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var uiapp = data.Application;
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc.Document;
            RevitOwner.MainHandle = uiapp.MainWindowHandle;
#if REVIT2024_OR_GREATER
            try { Theme.Dark = UIThemeManager.CurrentTheme == UITheme.Dark; } catch (Exception) { Theme.Dark = false; }
#endif
            try
            {
                var settings = Core.Settings.Load();
                var imports = Items.Imports(doc);
                if (imports.Count == 0)
                {
                    TaskDialog.Show("DMC Remapper", "No linked or imported DWG found in this project.");
                    return Result.Cancelled;
                }
                if (Items.Levels(doc).Count == 0)
                {
                    TaskDialog.Show("DMC Remapper", "This model has no levels.");
                    return Result.Cancelled;
                }

                // The DWG: selected in Revit, else the one used last time, else the first.
                var pre = uidoc.Selection.GetElementIds().FirstOrDefault(id => doc.GetElement(id) is ImportInstance);
                int index = pre != null ? imports.FindIndex(i => i.Element.Id == pre) : -1;
                if (index < 0 && !string.IsNullOrEmpty(settings.LastDwg))
                    index = imports.FindIndex(i => string.Equals(Items.DwgName(doc, i.Element), settings.LastDwg, StringComparison.OrdinalIgnoreCase));
                index = Math.Max(index, 0);
                bool nested = settings.IncludeNestedBlocks;
                string version = typeof(PlaceFamiliesCommand).Assembly.GetName().Version is Version v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

                MappingSession previous = null;
                Level keepLevel = null;   // Include nested toggled: the level picked stays
                while (true)
                {
                    var import = imports[index].Element;
                    settings.LastDwg = Items.DwgName(doc, import);
                    Core.Settings.TrySave(settings);
                    var level = keepLevel ?? Items.DefaultLevel(doc, import);

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var blocks = DwgReader.Read(doc, import, nested);
                    if (settings.SimplifyBlockNames) DwgReader.SimplifyNames(blocks);
                    var readDwgTime = sw.Elapsed;
                    if (blocks.Count == 0)
                    {
                        TaskDialog.Show("DMC Remapper", $"No blocks found in {Items.DwgName(doc, import)}.\n\n" +
                            "If the drawing was exploded before linking, it contains no blocks." +
                            (previous != null ? "\n\nBack to the previous DWG." : ""));
                        if (previous == null) return Result.Cancelled;
                        index = previous.DwgIndex = previous.PreviousDwgIndex;
                        continue;
                    }

                    sw.Restart();
                    var session = BuildSession(doc, import, level, blocks, settings);
                    if (previous != null) session.Apply(previous.ToMapping(), families: true);
                    session.Version = version;
                    session.DwgNames = imports.Select(i => i.Name).ToList();
                    session.DwgIndex = session.PreviousDwgIndex = index;
                    session.IncludeNested = nested;
                    var setupTime = sw.Elapsed;

                    MappingWindow win = null;
                    List<PlacementResult> runResults = null;
                    SimpleReport runReport = null;
                    string runDetails = null, runLog = null;
                    session.Place = (preview, progress) =>
                    {
                        var outcome = RunOnce(doc, session, blocks, import, settings, preview, progress, readDwgTime, setupTime, win);
                        if (outcome == null) return false;
                        if (preview)
                        {
                            new ResultWindow(outcome.Report, outcome.Details, outcome.LogPath,
                                "Close this window to go back to the mapping, then click Run to place the families.", win).ShowDialog();
                            session.Message = outcome.Report.Headline + "  (preview: nothing was changed)";
                            session.MessageIsWarning = !outcome.Report.AllPlaced;
                            return false;
                        }
                        runResults = outcome.Results;
                        runReport = outcome.Report;
                        runDetails = outcome.Details;
                        runLog = outcome.LogPath;
                        return true;
                    };

                    win = new MappingWindow(session);
                    RevitOwner.Attach(win);
                    win.ShowDialog();
                    switch (win.Action)
                    {
                        case MappingAction.Cancel:
                            return Result.Cancelled;
                        case MappingAction.Reload:
                            keepLevel = session.DwgIndex == index ? LevelByName(doc, session.LevelName) : null;
                            index = session.DwgIndex;
                            nested = session.IncludeNested;
                            previous = session;
                            continue;
                        case MappingAction.PickDwg:
                            try
                            {
                                var picked = uidoc.Selection.PickObject(Autodesk.Revit.UI.Selection.ObjectType.Element,
                                    new ImportSelectionFilter(), "DMC Remapper: click a DWG link or import in the view (Esc = back to the mapping)");
                                int at = picked != null ? imports.FindIndex(i => i.Element.Id == picked.ElementId) : -1;
                                if (at >= 0) index = at;
                            }
                            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { /* Esc: same DWG */ }
                            keepLevel = index == session.DwgIndex ? LevelByName(doc, session.LevelName) : null;
                            previous = session;
                            continue;
                    }

                    // Run finished: the summary, then the elements to check.
                    string footnote = runReport.Placed > 0 ? "Undo the whole run with a single Ctrl+Z." : null;
                    new ResultWindow(runReport, runDetails, runLog, footnote).ShowDialog();
                    var review = NeedsReview.From(runResults);
                    if (review.Count > 0)
                        new NeedsReviewWindow(review, ids =>
                        {
                            var elementIds = ids.Select(Compat.ToId).ToList();
                            uidoc.Selection.SetElementIds(elementIds);
                            uidoc.ShowElements(elementIds);
                        }).ShowDialog();
                    return Result.Succeeded;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("DMC Remapper - error", ex.ToString());
                return Result.Failed;
            }
        }

        static Level LevelByName(Document doc, string name) =>
            new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault(l => l.Name == name);

        class Outcome
        {
            public List<PlacementResult> Results;
            public SimpleReport Report;
            public string Details, LogPath;
        }

        /// <summary>One Preview (placed and rolled back) or Run, with the mapping window's progress bar.
        /// Null when it was cancelled (the window shows why).</summary>
        static Outcome RunOnce(Document doc, MappingSession session, List<BlockRef> blocks, ImportInstance import, Core.Settings settings,
                               bool preview, Func<int, int, bool> progress, TimeSpan readDwgTime, TimeSpan setupTime, System.Windows.Window owner)
        {
            session.Message = "";
            session.MessageIsWarning = false;
            // Remember the families picked by hand for the next Auto-match.
            try { session.Learn(); } catch (Exception) { }
            // Remember this grid for the project (next time it is pre-filled).
            try
            {
                Directory.CreateDirectory(ProjectStore.Folder);
                Mapping.Save(session.ProjectMappingPath, session.Rows.Select(r => r.ToMapRow()), session.Slab);
            }
            catch (Exception) { /* remembering is a convenience only */ }

            var level = LevelByName(doc, session.LevelName) ?? Items.DefaultLevel(doc, import);
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
            // Run: ask when elements were already placed at these locations.
            var placer = new Placer(doc, settings);
            if (!preview)
            {
                int existing;
                using (timer.Time(Phases.DupIndex)) existing = placer.CountExisting(blocks, mapping, symbols, level);
                if (existing > 0)
                {
                    var choice = AskExisting(existing, owner);
                    if (choice == ExistingChoice.Cancel) return null;
                    placer.CheckDuplicates = choice == ExistingChoice.Skip;
                }
            }
            List<PlacementResult> results;
            try
            {
                results = placer.PlaceAll(blocks, mapping, symbols, level, preview, progress, DwgExtents(import), timer, import);
            }
            catch (OperationCanceledException)
            {
                session.Message = "Cancelled. Everything was rolled back: nothing was changed in the model.";
                session.MessageIsWarning = true;
                return null;
            }
            // Show detection: the line chosen for each vertical-plane block, as detail lines
            // (own transaction, so they stay after a Preview). The previous ones are always removed.
            try
            {
                var view = doc.ActiveView is ViewPlan vp && Detection.CanDraw(vp) ? (View)vp
                    : new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                        .FirstOrDefault(v => !v.IsTemplate && v.GenLevel != null && v.GenLevel.Id == level.Id);
                Detection.Show(doc, view, settings.ShowDetection ? placer.Detections : null);
            }
            catch (Exception) { /* review aid only */ }
            total.Stop();
            timer.Add(Phases.Total, total.Elapsed + readDwgTime + setupTime);
            string logPath;
            using (timer.Time(Phases.WriteLog)) logPath = WriteLog(doc, results, preview, timer);
            if (preview) session.SetDetectedHosts(results);

            // Details (behind "Show details"): the full technical summary, timings and log path.
            var summary = Report.Summarize(results);
            var text = Report.SummaryText(summary, preview);
            if (errors.Count > 0)
                text = "Warnings:\r\n  - " + string.Join("\r\n  - ", errors) + "\r\n\r\n" + text;
            text += "\r\n\r\nTimings (where the time goes):\r\n" + timer.Format();
            text += "\r\n\r\n" + (logPath != null ? "Log saved: " + logPath : "Could not write the log file.");
            return new Outcome { Results = results, Report = SimpleReport.From(results, preview), Details = text, LogPath = logPath };
        }

        /// <summary>Only DWG links / imports can be clicked when picking the DWG in the view.</summary>
        class ImportSelectionFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is ImportInstance;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        enum ExistingChoice { Skip, PlaceAnyway, Cancel }

        /// <summary>"X elements already exist at these locations": Skip them (default) / Place anyway / Cancel.</summary>
        static ExistingChoice AskExisting(int count, System.Windows.Window owner)
        {
            var dlg = new ChoiceDialog("DMC Remapper - already placed", ExistingIndex.Warning(count),
                "An element of the same family, or one whose Comments say \"DMC: <block>\" (or the older \"CAD: <block>\"), is already " +
                "at these block locations (probably from an earlier run).",
                new[]
                {
                    ("Skip them", "Place only the blocks that are not in the model yet (recommended)"),
                    ("Place anyway", "Place every block, even where an element already exists"),
                    ("Cancel", "Go back to the mapping window"),
                }, owner);
            dlg.ShowDialog();
            return dlg.Choice == 0 ? ExistingChoice.Skip : dlg.Choice == 1 ? ExistingChoice.PlaceAnyway : ExistingChoice.Cancel;
        }

        /// <summary>One grid row per unique block name. The project's last mapping restores
        /// Elevation, Host Type, Rotation, Facing, Level and the Slab / Ceiling options, but every
        /// Revit Family starts at (Skip): families are picked again, or come from Load... / Auto-match.</summary>
        static MappingSession BuildSession(Document doc, ImportInstance import, Level level, List<BlockRef> blocks, Core.Settings settings)
        {
            var session = new MappingSession
            {
                DwgName = Items.DwgName(doc, import),
                LevelName = level.Name,
                InstanceCount = blocks.Count,
                Settings = settings,
                ProjectMappingPath = ProjectStore.MappingPathFor(ProjectStore.KeyFor(ModelPath(doc), doc.Title)),
            };
            session.Slab.WallSearchMm = settings.WallSearchDistanceMm > 0 ? settings.WallSearchDistanceMm : SlabOptions.DefaultWallSearchMm;
            FamilyCatalog.Load(doc, session);
            session.LevelNames = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.ProjectElevation).Select(l => l.Name).ToList();
            var symbols = DwgReader.ExtractSymbols(blocks, doc);
            // Auto-match by symbol geometry: the families' symbols are read when Auto-match is clicked.
            session.LoadFamilySymbols = progress =>
            {
                var cache = new SignatureCache();
                var list = FamilySignatures.Load(doc, session.Options, cache, progress, out var st);
                session.SymbolInfo = $"Family symbols: {st.WithSymbol} of {st.Families} families have one" +
                    $" ({st.FromCache} from the cache, {st.Opened} opened" +
                    (st.Skipped > 0 ? $", {st.Skipped} system/in-place skipped" : "") +
                    (st.Failed > 0 ? $", {st.Failed} could not be opened" : "") + ")" +
                    (st.Cancelled ? ". Stopped: the rest were not read." : ".");
                session.SymbolReport = st.Lines;
                return list;
            };
            foreach (var kv in DwgReader.CountByName(blocks).OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                session.Rows.Add(new BlockRow(kv.Key, kv.Value, session.Options, level.Name)
                {
                    Host = BlockRow.HostChoices.Contains(settings.DefaultHost) ? settings.DefaultHost : BlockRow.HostChoices[0],
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

        /// <summary>CSV log in Documents\DMCRemapper\Logs (temp folder if that fails).</summary>
        static string WriteLog(Document doc, List<PlacementResult> results, bool preview, PhaseTimer timer = null)
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var project = ProjectStore.KeyFor(ModelPath(doc), doc.Title);
            var name = $"dmc_{(preview ? "preview" : "log")}_{stamp}.csv";
            foreach (var folder in new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DMCRemapper", "Logs", project),
                Path.Combine(Path.GetTempPath(), "DMCRemapper"),
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

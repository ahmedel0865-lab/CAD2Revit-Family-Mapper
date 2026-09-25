using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// <summary>Lists the block names in a DWG and exports a mapping template.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ListBlocksCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = data.Application.ActiveUIDocument.Document;
            try
            {
                var settings = Core.Settings.Load();
                var imports = Items.Imports(doc);
                if (imports.Count == 0)
                {
                    TaskDialog.Show("CAD2Revit", "No linked or imported DWG found in this project.");
                    return Result.Cancelled;
                }
                var imp = Pickers.PickOne("Select the DWG", imports);
                if (imp == null) return Result.Cancelled;

                var blocks = DwgReader.Read(doc, imp.Element, settings.IncludeNestedBlocks);
                if (settings.SimplifyBlockNames) DwgReader.SimplifyNames(blocks);
                var counts = DwgReader.CountByName(blocks);
                if (counts.Count == 0)
                {
                    TaskDialog.Show("CAD2Revit", "No blocks found in the selected DWG.\n\n" +
                        "If the drawing was exploded before linking, it contains no blocks.");
                    return Result.Cancelled;
                }
                var mirrored = blocks.Where(b => b.Mirrored).GroupBy(b => b.Name).ToDictionary(g => g.Key, g => g.Count());
                var names = counts.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                var anonymous = names.Where(n => n.Length == 0 || n.StartsWith("*")).ToList();

                var text = $"Blocks found in {imp.Name}: {names.Count} names, {blocks.Count} instances\r\n\r\n" +
                           TextTable.Format(new[] { "Block name", "Count", "Mirrored" },
                               names.Select(n => new object[] { n, counts[n], mirrored.TryGetValue(n, out var m) ? (object)m : "" }));
                if (anonymous.Count > 0)
                    text += $"\r\n\r\nNote: {anonymous.Count} anonymous block name(s) (e.g. *U12) found. These are usually " +
                            "dynamic blocks and cannot be mapped reliably - see LIMITATIONS.";

                void Export()
                {
                    using (var dlg = new SaveFileDialog
                    {
                        Title = "Save mapping template", FileName = "cad2revit_mapping.xlsx",
                        Filter = "Excel workbook (*.xlsx)|*.xlsx|CSV UTF-8 (*.csv)|*.csv",
                    })
                    {
                        if (dlg.ShowDialog() != DialogResult.OK) return;
                        var rows = names.Except(anonymous)
                            .Select(n => (IList<object>)new object[] { n, "", "", 0, 0, "non-hosted" }).ToList();
                        Tables.WriteTable(dlg.FileName, Mapping.TemplateHeader, rows);
                        MessageBox.Show($"Template saved:\n{dlg.FileName}\n\nFill in family, type, offset and host type. " +
                                        "Leave the family empty for blocks you do not want to place.", "CAD2Revit");
                    }
                }

                using (var form = new ResultForm("CAD2Revit - Blocks in DWG", text, null, "Export template...", Export))
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

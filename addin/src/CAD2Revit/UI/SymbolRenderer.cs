using System.Windows;
using System.Windows.Media;
using CAD2Revit.Core;

namespace CAD2Revit.UI
{
    /// <summary>Draws a BlockSymbol (CAD line work) as a WPF image for the preview panel.</summary>
    public static class SymbolRenderer
    {
        const double Size = 200, Margin = 12;

        /// <summary>A frozen image of the symbol, or null if it has no line work.</summary>
        public static ImageSource Render(BlockSymbol symbol, Color stroke)
        {
            if (symbol == null || symbol.IsEmpty) return null;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                foreach (var p in symbol.FitTo(Size, Size, Margin))
                {
                    ctx.BeginFigure(new Point(p[0], p[1]), false, false);
                    for (int i = 2; i + 1 < p.Length; i += 2)
                        ctx.LineTo(new Point(p[i], p[i + 1]), true, false);
                }
            }
            geometry.Freeze();
            var pen = new Pen(new SolidColorBrush(stroke), 1.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            pen.Freeze();
            var group = new DrawingGroup();
            // Transparent frame keeps the drawing at a fixed size, so thin symbols are not stretched.
            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, Size, Size))));
            group.Children.Add(new GeometryDrawing(null, pen, geometry));
            group.Freeze();
            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }
    }
}

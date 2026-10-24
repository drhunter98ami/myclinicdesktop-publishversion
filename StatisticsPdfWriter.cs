using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyClinic;

// Exports the report's WPF drawings, preserving shaped Arabic glyphs and vector charts.
// This deliberately supports the drawing primitives used by the statistics report only.
internal sealed class StatisticsPdfWriter
{
    private readonly List<byte[]> objects = new();
    private readonly Dictionary<Uri, PdfFont> fonts = new();
    private readonly Dictionary<double, int> alphas = new();
    private readonly Dictionary<ImageSource, int> images = new();
    private readonly StringBuilder commands = new();
    private static string N(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
    private static string Unicode(string text) => Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text));
    private int Reserve() { objects.Add(Array.Empty<byte>()); return objects.Count; }
    private int Add(string value) { int id = Reserve(); objects[id - 1] = Ascii(value); return id; }
    private void Set(int id, string value) => objects[id - 1] = Ascii(value);
    private int Stream(byte[] bytes, string entries = "")
    {
        using var compressed = new MemoryStream();
        using (var zip = new ZLibStream(compressed, CompressionLevel.Optimal, true)) zip.Write(bytes);
        byte[] data = compressed.ToArray();
        using var result = new MemoryStream();
        result.Write(Ascii($"<< /Length {data.Length} /Filter /FlateDecode {entries} >>\nstream\n"));
        result.Write(data); result.Write(Ascii("\nendstream"));
        int id = Reserve(); objects[id - 1] = result.ToArray(); return id;
    }
    private void Emit(string value) => commands.AppendLine(value);
    private void Matrix(Matrix m) => Emit($"{N(m.M11)} {N(m.M12)} {N(m.M21)} {N(m.M22)} {N(m.OffsetX)} {N(m.OffsetY)} cm");
    private void Alpha(double value)
    {
        value = Math.Round(Math.Clamp(value, 0, 1), 4);
        if (!alphas.TryGetValue(value, out int id))
            alphas[value] = id = Add($"<< /Type /ExtGState /ca {N(value)} /CA {N(value)} >>");
        Emit($"/A{id} gs");
    }
    private void Color(Color color, double opacity)
    {
        Emit($"{N(color.R / 255d)} {N(color.G / 255d)} {N(color.B / 255d)} rg");
        Alpha(opacity * color.A / 255d);
    }
    private void Path(Geometry geometry)
    {
        PathGeometry path = geometry.GetFlattenedPathGeometry(0.08, ToleranceType.Absolute);
        Matrix m = path.Transform?.Value ?? System.Windows.Media.Matrix.Identity;
        foreach (var figure in path.Figures)
        {
            Point start = m.Transform(figure.StartPoint);
            Emit($"{N(start.X)} {N(start.Y)} m");
            foreach (var segment in figure.Segments)
            {
                IEnumerable<Point> points = segment switch
                {
                    LineSegment line => new[] { line.Point },
                    PolyLineSegment poly => poly.Points,
                    _ => throw new InvalidOperationException("Unexpected flattened PDF geometry.")
                };
                foreach (Point point in points)
                {
                    Point p = m.Transform(point); Emit($"{N(p.X)} {N(p.Y)} l");
                }
            }
            if (figure.IsClosed) Emit("h");
        }
    }
    private void Paint(Geometry geometry, Brush? brush, double opacity)
    {
        if (brush == null || geometry.IsEmpty()) return;
        Emit("q");
        if (brush is SolidColorBrush solid)
        {
            Color(solid.Color, opacity * solid.Opacity); Path(geometry);
            Emit(geometry.GetFlattenedPathGeometry().FillRule == FillRule.EvenOdd ? "f*" : "f");
        }
        else if (brush is LinearGradientBrush gradient)
        {
            // The report uses a two-stop diagonal header; a PDF axial shading stays vector.
            Rect b = geometry.Bounds;
            Point start = gradient.StartPoint, end = gradient.EndPoint;
            if (gradient.MappingMode == BrushMappingMode.RelativeToBoundingBox)
            {
                start = new(b.X + start.X * b.Width, b.Y + start.Y * b.Height);
                end = new(b.X + end.X * b.Width, b.Y + end.Y * b.Height);
            }
            Color first = gradient.GradientStops.First().Color, last = gradient.GradientStops.Last().Color;
            string Rgb(Color c) => $"{N(c.R / 255d)} {N(c.G / 255d)} {N(c.B / 255d)}";
            int shading = Add($"<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [{N(start.X)} {N(start.Y)} {N(end.X)} {N(end.Y)}] /Function << /FunctionType 2 /Domain [0 1] /C0 [{Rgb(first)}] /C1 [{Rgb(last)}] /N 1 >> /Extend [true true] >>");
            shadings.Add(shading);
            Alpha(opacity * brush.Opacity); Path(geometry); Emit("W n"); Emit($"/S{shading} sh");
        }
        else throw new InvalidOperationException("Unsupported statistics PDF brush.");
        Emit("Q");
    }
    private readonly List<int> shadings = new();
    private void Drawing(Drawing drawing, double opacity)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                Emit("q");
                if (group.Transform != null) Matrix(group.Transform.Value);
                if (group.ClipGeometry != null) { Path(group.ClipGeometry); Emit("W n"); }
                foreach (Drawing child in group.Children) Drawing(child, opacity * group.Opacity);
                Emit("Q"); break;
            case GeometryDrawing shape:
                Paint(shape.Geometry, shape.Brush, opacity);
                if (shape.Pen != null) Paint(shape.Geometry.GetWidenedPathGeometry(shape.Pen), shape.Pen.Brush, opacity);
                break;
            case GlyphRunDrawing text:
                Glyphs(text.GlyphRun, text.ForegroundBrush, opacity); break;
            case ImageDrawing image:
                Image(image, opacity); break;
            default: throw new InvalidOperationException("Unsupported statistics PDF drawing.");
        }
    }
    private void Visual(Visual visual, double opacity)
    {
        Emit("q");
        Vector offset = VisualTreeHelper.GetOffset(visual);
        Emit($"1 0 0 1 {N(offset.X)} {N(offset.Y)} cm");
        Transform? transform = VisualTreeHelper.GetTransform(visual);
        if (transform != null) Matrix(transform.Value);
        Geometry? clip = VisualTreeHelper.GetClip(visual);
        if (clip != null) { Path(clip); Emit("W n"); }
        opacity *= VisualTreeHelper.GetOpacity(visual);
        // ActualText keeps logical Arabic reading order, ligatures and wrapped lines copyable.
        bool actualText = visual is TextBlock;
        if (visual is TextBlock block) Emit($"/Span << /ActualText <FEFF{Unicode(block.Text)}> >> BDC");
        DrawingGroup? drawing = VisualTreeHelper.GetDrawing(visual);
        if (drawing != null) Drawing(drawing, opacity);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            if (VisualTreeHelper.GetChild(visual, i) is Visual child) Visual(child, opacity);
        if (actualText) Emit("EMC");
        Emit("Q");
    }
    private sealed class PdfFont
    {
        public required GlyphTypeface Typeface;
        public int Id;
        public readonly Dictionary<(ushort Glyph, string Text), ushort> Codes = new();
        public ushort Code(ushort glyph, string text)
        {
            var key = (glyph, text);
            if (!Codes.TryGetValue(key, out ushort code))
            {
                if (Codes.Count >= 65534) throw new InvalidOperationException("Too many PDF font glyphs.");
                Codes[key] = code = (ushort)(Codes.Count + 1);
            }
            return code;
        }
    }
    private void Glyphs(GlyphRun run, Brush brush, double opacity)
    {
        if (brush is not SolidColorBrush solid || run.IsSideways)
            throw new InvalidOperationException("Unsupported statistics PDF text style.");
        if (!fonts.TryGetValue(run.GlyphTypeface.FontUri, out var font))
        {
            font = new PdfFont { Typeface = run.GlyphTypeface, Id = Reserve() };
            fonts.Add(run.GlyphTypeface.FontUri, font);
        }
        Color(solid.Color, opacity * solid.Opacity);
        var unicode = new string[run.GlyphIndices.Count];
        if (run.Characters != null)
            for (int i = 0; i < run.Characters.Count; i++)
            {
                int cluster = run.ClusterMap != null && run.ClusterMap.Count > i ? run.ClusterMap[i] : i;
                if (cluster < unicode.Length) unicode[cluster] += run.Characters[i];
            }
        double advance = 0, size = run.FontRenderingEmSize;
        bool rtl = (run.BidiLevel & 1) != 0;
        Emit($"BT /F{font.Id} {N(size)} Tf");
        for (int i = 0; i < run.GlyphIndices.Count; i++)
        {
            ushort glyph = run.GlyphIndices[i];
            Point offset = run.GlyphOffsets != null && run.GlyphOffsets.Count > i ? run.GlyphOffsets[i] : new Point();
            double x = run.BaselineOrigin.X + (rtl ? -advance - font.Typeface.AdvanceWidths[glyph] * size - offset.X : advance + offset.X);
            double y = run.BaselineOrigin.Y - offset.Y;
            ushort code = font.Code(glyph, unicode[i] ?? "\u200B");
            Emit($"1 0 0 -1 {N(x)} {N(y)} Tm <{code:X4}> Tj");
            advance += run.AdvanceWidths[i];
        }
        Emit("ET");
    }
    private void Image(ImageDrawing drawing, double opacity)
    {
        if (!images.TryGetValue(drawing.ImageSource, out int id))
        {
            if (drawing.ImageSource is not BitmapSource bitmap) throw new InvalidOperationException("Unsupported PDF image.");
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            int w = converted.PixelWidth, h = converted.PixelHeight;
            byte[] pixels = new byte[w * h * 4], rgb = new byte[w * h * 3];
            converted.CopyPixels(pixels, w * 4, 0);
            for (int p = 0; p < w * h; p++)
            {
                double a = pixels[p * 4 + 3] / 255d;
                for (int c = 0; c < 3; c++) rgb[p * 3 + c] = (byte)Math.Round(pixels[p * 4 + 2 - c] * a + 255 * (1 - a));
            }
            images[drawing.ImageSource] = id = Stream(rgb, $"/Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceRGB /BitsPerComponent 8");
        }
        Rect r = drawing.Rect;
        Emit("q"); Alpha(opacity);
        Emit($"{N(r.Width)} 0 0 {N(-r.Height)} {N(r.X)} {N(r.Bottom)} cm /I{id} Do"); Emit("Q");
    }
    private void Embed(PdfFont font)
    {
        using var bytes = new MemoryStream();
        using (Stream stream = font.Typeface.GetFontStream()) stream.CopyTo(bytes);
        int file = Stream(bytes.ToArray(), $"/Length1 {bytes.Length}");
        string name = "ReportFont" + font.Id;
        int descriptor = Add($"<< /Type /FontDescriptor /FontName /{name} /Flags 32 /FontBBox [-2000 -2000 4000 4000] /ItalicAngle 0 /Ascent 1100 /Descent -400 /CapHeight 750 /StemV 80 /FontFile2 {file} 0 R >>");
        byte[] map = new byte[(font.Codes.Count + 1) * 2];
        var widths = new StringBuilder();
        var cmap = new StringBuilder("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /ReportUnicode def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        foreach (var batch in font.Codes.Chunk(100))
        {
            cmap.AppendLine($"{batch.Length} beginbfchar");
            foreach (var entry in batch)
            {
                ushort glyph = entry.Key.Glyph, code = entry.Value;
                map[code * 2] = (byte)(glyph >> 8); map[code * 2 + 1] = (byte)glyph;
                widths.Append($"{code} [{N(font.Typeface.AdvanceWidths[glyph] * 1000)}] ");
                cmap.AppendLine($"<{code:X4}> <{Unicode(entry.Key.Text)}>");
            }
            cmap.AppendLine("endbfchar");
        }
        cmap.AppendLine("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend");
        int mapping = Stream(map), toUnicode = Stream(Ascii(cmap.ToString()));
        int cid = Add($"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{name} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor {descriptor} 0 R /CIDToGIDMap {mapping} 0 R /W [{widths}] >>");
        Set(font.Id, $"<< /Type /Font /Subtype /Type0 /BaseFont /{name} /Encoding /Identity-H /DescendantFonts [{cid} 0 R] /ToUnicode {toUnicode} 0 R >>");
    }
    public static byte[] Build(IReadOnlyList<Canvas> pages, string title)
    {
        var writer = new StatisticsPdfWriter();
        int catalog = writer.Reserve(), tree = writer.Reserve();
        var pageIds = new List<int>(); var contentIds = new List<int>();
        foreach (Canvas page in pages)
        {
            page.Measure(new Size(page.Width, page.Height));
            page.Arrange(new Rect(0, 0, page.Width, page.Height)); page.UpdateLayout();
            writer.commands.Clear(); writer.Emit("q");
            writer.Emit($"{N(595.276 / page.Width)} 0 0 {N(-841.89 / page.Height)} 0 841.89 cm");
            writer.Visual(page, 1); writer.Emit("Q");
            contentIds.Add(writer.Stream(Ascii(writer.commands.ToString()))); pageIds.Add(writer.Reserve());
        }
        foreach (PdfFont font in writer.fonts.Values) writer.Embed(font);
        string resources = $"/Font << {string.Join(" ", writer.fonts.Values.Select(f => $"/F{f.Id} {f.Id} 0 R"))} >> /ExtGState << {string.Join(" ", writer.alphas.Values.Select(id => $"/A{id} {id} 0 R"))} >> /XObject << {string.Join(" ", writer.images.Values.Select(id => $"/I{id} {id} 0 R"))} >> /Shading << {string.Join(" ", writer.shadings.Select(id => $"/S{id} {id} 0 R"))} >>";
        for (int i = 0; i < pageIds.Count; i++)
            writer.Set(pageIds[i], $"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 595.276 841.89] /Resources << {resources} >> /Contents {contentIds[i]} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Count {pageIds.Count} /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] >>");
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R /Lang (ar) >>");
        int info = writer.Add($"<< /Title <FEFF{Unicode(title)}> /Creator (My Clinic) >>");
        using var pdf = new MemoryStream();
        pdf.Write(Ascii("%PDF-1.7\n%")); pdf.Write(new byte[] { 0xE2, 0xE3, 0xCF, 0xD3, 10 });
        var offsets = new List<long> { 0 };
        for (int i = 0; i < writer.objects.Count; i++)
        {
            offsets.Add(pdf.Position); pdf.Write(Ascii($"{i + 1} 0 obj\n")); pdf.Write(writer.objects[i]); pdf.Write(Ascii("\nendobj\n"));
        }
        long xref = pdf.Position; pdf.Write(Ascii($"xref\n0 {offsets.Count}\n0000000000 65535 f \n"));
        foreach (long offset in offsets.Skip(1)) pdf.Write(Ascii(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n"));
        pdf.Write(Ascii($"trailer\n<< /Size {offsets.Count} /Root {catalog} 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return pdf.ToArray();
    }
}

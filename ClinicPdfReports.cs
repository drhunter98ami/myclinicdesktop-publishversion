using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MyClinic;

internal sealed class ClinicPdfDocument
{
    private const double Width = 1240, Height = 1754, Margin = 70, ContentWidth = Width - Margin * 2;
    private static readonly Brush Navy = Brush("#16324F");
    private static readonly Brush Teal = Brush("#0F9B8E");
    private static readonly Brush Muted = Brush("#64748B");
    private static readonly Brush Border = Brush("#D8E1EA");
    private static readonly Brush HeaderBg = Brush("#EAF1F7");
    private readonly string _title;
    private readonly string _subtitle;
    private readonly List<Canvas> _pages = new();
    private Canvas _page = null!;
    private double _y;

    public ClinicPdfDocument(string title, string subtitle)
    {
        _title = title;
        _subtitle = subtitle;
        NewPage();
    }

    public void AddSection(string title)
    {
        EnsureSpace(56);
        _page.Children.Add(new Rectangle { Width = ContentWidth, Height = 46, Fill = HeaderBg, RadiusX = 8, RadiusY = 8 });
        Canvas.SetLeft(_page.Children[^1], Margin);
        Canvas.SetTop(_page.Children[^1], _y);
        AddText(title, Margin + 14, _y + 8, ContentWidth - 28, 19, Navy, true);
        _page.Children.Add(new Rectangle { Width = 5, Height = 46, Fill = Teal, RadiusX = 2, RadiusY = 2 });
        Canvas.SetLeft(_page.Children[^1], Width - Margin - 5);
        Canvas.SetTop(_page.Children[^1], _y);
        _y += 58;
    }

    public void AddParagraph(string text)
    {
        string remaining = string.IsNullOrWhiteSpace(text) ? "—" : text.Trim();
        while (remaining.Length > 0)
        {
            double available = Height - 105 - _y;
            if (available < 44)
            {
                NewPage();
                available = Height - 105 - _y;
            }

            var block = CreateText(remaining, ContentWidth - 22, 12, Muted, false);
            block.Measure(new Size(ContentWidth - 22, double.PositiveInfinity));
            double required = Math.Max(28, block.DesiredSize.Height + 14);
            if (required <= available)
            {
                AddTextBlock(block, Margin + 11, _y + 5, ContentWidth - 22, block.DesiredSize.Height);
                _y += required;
                return;
            }

            string chunk = TakeFittingText(remaining, ContentWidth - 22, available - 14, 12);
            if (chunk.Length == 0)
            {
                NewPage();
                continue;
            }
            block = CreateText(chunk, ContentWidth - 22, 12, Muted, false);
            block.Measure(new Size(ContentWidth - 22, double.PositiveInfinity));
            AddTextBlock(block, Margin + 11, _y + 5, ContentWidth - 22, block.DesiredSize.Height);
            _y += block.DesiredSize.Height + 14;
            remaining = remaining[chunk.Length..].TrimStart();
        }
    }

    public void AddSummaryCards(IReadOnlyList<(string Label, string Value, string Color)> items)
    {
        if (items.Count == 0) return;
        EnsureSpace(120);
        const double gap = 12;
        double cardWidth = (ContentWidth - gap * (items.Count - 1)) / items.Count;
        double x = Width - Margin;
        foreach (var item in items)
        {
            x -= cardWidth;
            var card = new Rectangle { Width = cardWidth, Height = 96, Fill = Brush("#F3F7FA"), RadiusX = 9, RadiusY = 9, Stroke = Border, StrokeThickness = 1 };
            _page.Children.Add(card);
            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, _y);
            var stripe = new Rectangle { Width = 5, Height = 96, Fill = Brush(item.Color), RadiusX = 2, RadiusY = 2 };
            _page.Children.Add(stripe);
            Canvas.SetLeft(stripe, x + cardWidth - 5);
            Canvas.SetTop(stripe, _y);
            AddText(item.Label, x + 12, _y + 13, cardWidth - 28, 12, Muted, true);
            AddText(item.Value, x + 12, _y + 43, cardWidth - 28, 17, Brush(item.Color), true);
            x -= gap;
        }
        _y += 112;
    }

    public void AddTable(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, params double[] weights)
    {
        if (headers.Count == 0) return;
        double[] ratios = weights.Length == headers.Count && weights.All(w => w > 0)
            ? weights.Select(w => w / weights.Sum()).ToArray()
            : Enumerable.Repeat(1d / headers.Count, headers.Count).ToArray();
        double[] widths = ratios.Select(r => ContentWidth * r).ToArray();
        DrawTableRow(headers, widths, true);

        foreach (string[] row in rows)
        {
            var values = Enumerable.Range(0, headers.Count).Select(i => i < row.Length ? row[i] : "").ToArray();
            double maxCellHeight = Height - 105 - 176 - 48 - 20;
            List<string>[] wrapped = values.Select((value, i) => SplitForCell(value, widths[i] - 18, maxCellHeight)).ToArray();
            int fragmentCount = wrapped.Max(parts => parts.Count);
            for (int fragment = 0; fragment < fragmentCount; fragment++)
            {
                string[] fragmentValues = wrapped.Select(parts => parts.ElementAtOrDefault(fragment) ?? "").ToArray();
                if (fragment > 0) fragmentValues[0] = values[0] + " (تابع)";
                var cells = fragmentValues.Select((value, i) =>
                {
                    var block = CreateText(value, widths[i] - 18, 12, Muted, false);
                    block.Measure(new Size(widths[i] - 18, double.PositiveInfinity));
                    return block;
                }).ToArray();
                double rowHeight = Math.Max(44, cells.Max(c => c.DesiredSize.Height + 20));
                if (_y + rowHeight > Height - 105)
                {
                    NewPage();
                    DrawTableRow(headers, widths, true);
                }

                var background = new Rectangle { Width = ContentWidth, Height = rowHeight, Fill = (_pages.Count + (int)(_y / 44)) % 2 == 0 ? Brushes.White : Brush("#F8FAFC") };
                _page.Children.Add(background);
                Canvas.SetLeft(background, Margin);
                Canvas.SetTop(background, _y);
                AddCellText(cells, widths, _y, rowHeight, false);
                _page.Children.Add(new Rectangle { Width = ContentWidth, Height = 1, Fill = Border });
                Canvas.SetLeft(_page.Children[^1], Margin);
                Canvas.SetTop(_page.Children[^1], _y + rowHeight - 1);
                _y += rowHeight;
            }
        }
        _y += 14;
    }

    public byte[] Build(string documentTitle)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            var footer = CreateText($"عيادتي · تقرير طبي/مالي · صفحة {i + 1} من {_pages.Count}", ContentWidth, 10, Muted, false);
            footer.Measure(new Size(ContentWidth, 22));
            footer.Width = ContentWidth;
            footer.Height = 22;
            _pages[i].Children.Add(footer);
            Canvas.SetLeft(footer, Margin);
            Canvas.SetTop(footer, Height - 62);
            var line = new Rectangle { Width = ContentWidth, Height = 1, Fill = Border };
            _pages[i].Children.Add(line);
            Canvas.SetLeft(line, Margin);
            Canvas.SetTop(line, Height - 76);
        }
        return StatisticsPdfWriter.Build(_pages, documentTitle);
    }

    private void NewPage()
    {
        _page = new Canvas { Width = Width, Height = Height, Background = Brushes.White, FlowDirection = FlowDirection.LeftToRight };
        _pages.Add(_page);
        _page.Children.Add(new Rectangle { Width = Width, Height = 14, Fill = Navy });
        Canvas.SetLeft(_page.Children[^1], 0);
        Canvas.SetTop(_page.Children[^1], 0);
        AddText("عيادتي", Margin, 38, ContentWidth, 15, Teal, true);
        AddText(_title, Margin, 72, ContentWidth, 27, Navy, true);
        AddText(_subtitle, Margin, 116, ContentWidth, 12, Muted, false);
        var line = new Rectangle { Width = ContentWidth, Height = 2, Fill = Border };
        _page.Children.Add(line);
        Canvas.SetLeft(line, Margin);
        Canvas.SetTop(line, 154);
        _y = 176;
    }

    private void DrawTableRow(IReadOnlyList<string> values, double[] widths, bool header)
    {
        double rowHeight = 48;
        if (_y + rowHeight > Height - 105) NewPage();
        var background = new Rectangle { Width = ContentWidth, Height = rowHeight, Fill = header ? Navy : Brushes.White };
        _page.Children.Add(background);
        Canvas.SetLeft(background, Margin);
        Canvas.SetTop(background, _y);
        var cells = values.Select((value, i) =>
        {
            var block = CreateText(value, widths[i] - 18, 12, header ? Brushes.White : Muted, header);
            block.Measure(new Size(widths[i] - 18, double.PositiveInfinity));
            return block;
        }).ToArray();
        AddCellText(cells, widths, _y, rowHeight, header);
        _y += rowHeight;
    }

    private void AddCellText(TextBlock[] cells, double[] widths, double y, double height, bool header)
    {
        double x = Width - Margin;
        for (int i = 0; i < cells.Length; i++)
        {
            x -= widths[i];
            AddTextBlock(cells[i], x + 9, y + 7, widths[i] - 18, Math.Max(28, height - 14));
            if (i > 0)
            {
                var separator = new Rectangle { Width = 1, Height = height, Fill = header ? Brush("#3A526B") : Border };
                _page.Children.Add(separator);
                Canvas.SetLeft(separator, x);
                Canvas.SetTop(separator, y);
            }
        }
    }

    private void EnsureSpace(double height)
    {
        if (_y + height > Height - 105) NewPage();
    }

    private void AddText(string text, double x, double y, double width, double size, Brush color, bool bold)
    {
        var block = CreateText(text, width, size, color, bold);
        block.Measure(new Size(width, double.PositiveInfinity));
        AddTextBlock(block, x, y, width, Math.Max(size * 1.6, block.DesiredSize.Height));
    }

    private void AddTextBlock(TextBlock block, double x, double y, double width, double height)
    {
        block.Width = width;
        block.Height = height;
        _page.Children.Add(block);
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
    }

    private static TextBlock CreateText(string text, double width, double size, Brush color, bool bold) => new()
    {
        Text = text,
        Width = width,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = size,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        Foreground = color,
        TextAlignment = TextAlignment.Right,
        FlowDirection = FlowDirection.RightToLeft,
        TextWrapping = TextWrapping.Wrap
    };

    private static string TakeFittingText(string text, double width, double height, double size)
    {
        string[] words = text.Split(' ');
        var accepted = new List<string>();
        foreach (string word in words)
        {
            string candidate = string.Join(' ', accepted.Append(word));
            var block = CreateText(candidate, width, size, Muted, false);
            block.Measure(new Size(width, double.PositiveInfinity));
            if (block.DesiredSize.Height > height)
                break;
            accepted.Add(word);
        }
        return accepted.Count > 0 ? string.Join(' ', accepted) : text[..Math.Min(text.Length, 80)];
    }

    private static List<string> SplitForCell(string text, double width, double maxHeight)
    {
        var parts = new List<string>();
        string remaining = string.IsNullOrWhiteSpace(text) ? "" : text.Trim();
        while (remaining.Length > 0)
        {
            var block = CreateText(remaining, width, 12, Muted, false);
            block.Measure(new Size(width, double.PositiveInfinity));
            if (block.DesiredSize.Height <= maxHeight)
            {
                parts.Add(remaining);
                break;
            }
            string chunk = TakeFittingText(remaining, width, maxHeight, 12);
            parts.Add(chunk);
            remaining = remaining[chunk.Length..].TrimStart();
        }
        return parts.Count == 0 ? new List<string> { "" } : parts;
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}

internal static class ClinicPdfReports
{
    internal static byte[] FinancialStatement(FinanceReport report, FinanceAccount account)
    {
        var document = new ClinicPdfDocument(report.Title, report.Scope);
        document.AddSection("تفاصيل كشف الحساب");
        document.AddTable(report.Headers, report.Rows, .11, .10, .10, .10, .10, .13, .25, .11);
        document.AddSection("الملخص المالي");
        document.AddSummaryCards(new[]
        {
            ("التكلفة", FinanceMoney.Format(account.Cost), "#2563EB"),
            ("الدفعات", FinanceMoney.Format(account.Paid), "#0F9B8E"),
            ("المسامحة", FinanceMoney.Format(account.Forgiven), "#8B5CF6"),
            (account.Balance < 0 ? "رصيد دائن" : "المستحق", FinanceMoney.Format(Math.Abs(account.Balance)), account.Balance < 0 ? "#0F9B8E" : "#E05252")
        });
        return document.Build(report.Title);
    }

    internal static byte[] PatientHistory(PatientCardModel patient, IReadOnlyList<VisitHistoryItem> visits)
    {
        var document = new ClinicPdfDocument("الملف الطبي للمريض", $"تاريخ التصدير: {AppClock.Now:dd/MM/yyyy HH:mm}");
        document.AddSection("معلومات المريض");
        document.AddTable(new[] { "البيان", "المعلومات" }, new[]
        {
            new[] { "الاسم", patient.DisplayName },
            new[] { "رقم الملف", patient.Id.ToString(CultureInfo.InvariantCulture) },
            new[] { "الهاتف", string.IsNullOrWhiteSpace(patient.PhoneNumber) ? "غير مسجل" : patient.PhoneNumber },
            new[] { "العمر والجنس", $"{patient.AgeWithUnit} · {patient.GenderLabel}" },
            new[] { "عدد الزيارات", patient.VisitCount.ToString(CultureInfo.InvariantCulture) },
            new[] { "آخر زيارة", patient.LatestVisitLabel },
            new[] { "زمرة الدم", patient.BloodType },
            new[] { "الحساسية", patient.Allergies },
            new[] { "الأمراض المزمنة", patient.ChronicDiseases },
            new[] { "التدخين", patient.SmokingSummary }
        }, .27, .73);

        document.AddSection($"التاريخ الطبي · {visits.Count:N0} زيارة");
        if (visits.Count == 0)
        {
            document.AddParagraph("لا توجد زيارات طبية مسجلة لهذا المريض.");
        }
        else
        {
            foreach (VisitHistoryItem visit in visits.OrderByDescending(v => v.VisitDate))
            {
                document.AddSection($"{visit.VisitDate:dd/MM/yyyy HH:mm} · {visit.Title}");
                document.AddParagraph($"الشكوى: {visit.SymptomsText}");
                document.AddParagraph($"التشخيص: {visit.DiagnosisText}");
                document.AddParagraph($"العلاج النهائي: {visit.FinalTreatmentText}");
                document.AddParagraph($"خطة العلاج: {visit.TreatmentPlanText}");
                document.AddParagraph($"نمط مخطط الأسنان: {visit.ChartModeText}");
                document.AddParagraph($"الأسنان المسجلة: {(visit.ToothIds.Count == 0 ? "غير مسجلة" : string.Join("، ", visit.ToothIds))}");
                document.AddParagraph($"العلامات الحيوية: ضغط الدم {visit.BloodPressureText} · النبض {visit.HeartRateText} · الحرارة {visit.TemperatureText} · التنفس {visit.RespiratoryRateText} · الوزن {visit.WeightText} · الطول {visit.HeightText} · سكر الدم {visit.BloodSugarText} · BMI {visit.BmiText}");
                document.AddParagraph($"الحمل والرضاعة: {visit.PregnancySummary}");
                if (visit.SelectedTreatments.Count > 0)
                    document.AddParagraph($"العلاجات: {string.Join("؛ ", visit.SelectedTreatments.Select(t => $"{t.TreatmentName} × {Math.Max(1, t.Quantity)} · {t.ToothDisplay}"))}");
                if (visit.PrescriptionItems.Count > 0)
                    document.AddParagraph($"الوصفات: {string.Join("؛ ", visit.PrescriptionItems.Select(p => $"{p.MedicineName}: {p.InstructionText}"))}");
                if (visit.HasAttachments)
                    document.AddParagraph($"المرفقات: {visit.AttachmentLabel}");
            }
        }

        return document.Build($"الملف الطبي - {patient.DisplayName}");
    }
}

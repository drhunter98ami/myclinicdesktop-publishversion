using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Text.Json;
using System.Text;
using System.Xml.Linq;
using System.Security;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Markup;
using Microsoft.Win32;

namespace MyClinic
{
    public partial class StatisticsView : UserControl
    {
        // ── State ─────────────────────────────────────────────────────────────
        private enum MainTab { Income, Expenses }
        private enum SubTab  { Daily, Monthly, Yearly }

        private MainTab _mainTab   = MainTab.Income;
        private SubTab  _subTab    = SubTab.Monthly;

        private DateTime _selectedDay   = DateTime.Today;
        private int      _selectedMonth = DateTime.Today.Month;
        private int      _selectedYear  = DateTime.Today.Year;
        private List<ExpenseEntry> _currentExpenses = new();

        // ── Pie colours ───────────────────────────────────────────────────────
        private static readonly string[] SliceColors =
        {
            "#3B82F6","#10B981","#F59E0B","#EF4444","#8B5CF6",
            "#06B6D4","#F97316","#EC4899","#84CC16","#6366F1",
            "#14B8A6","#FB923C","#A855F7","#22C55E","#E11D48"
        };

        // Expense keyword → display label (order matters: first match wins)
        private static readonly (string Keyword, string Label)[] ExpenseKeywords =
        {
            ("مخبر",   "مخبر"),
            ("نواقص",  "نواقص"),
            ("خزان",   "خزان"),
            ("صيانة",  "صيانة"),
            ("أجار",   "أجار"),
            ("أمبير",  "أمبير"),
            ("كهربا",  "كهربا"),
            ("ممرضة",  "ممرضة"),
        };
        private const string OtherLabel = "أخرى";

        // ── Constructor ───────────────────────────────────────────────────────
        public StatisticsView()
        {
            InitializeComponent();
            Loaded += async (_, _) => await RefreshAsync();
        }

        // ═════════════════════════════════════════════════════════════════════
        // Tab / subtab click handlers
        // ═════════════════════════════════════════════════════════════════════

        private async void BtnTabIncome_Click(object sender, RoutedEventArgs e)
        {
            _mainTab = MainTab.Income;
            UpdateTabStyles();
            await RefreshAsync();
        }

        private async void BtnTabExpenses_Click(object sender, RoutedEventArgs e)
        {
            _mainTab = MainTab.Expenses;
            UpdateTabStyles();
            await RefreshAsync();
        }

        private async void BtnDaily_Click(object sender, RoutedEventArgs e)
        {
            _subTab = SubTab.Daily;
            UpdateSubTabStyles();
            await RefreshAsync();
        }

        private async void BtnMonthly_Click(object sender, RoutedEventArgs e)
        {
            _subTab = SubTab.Monthly;
            UpdateSubTabStyles();
            await RefreshAsync();
        }

        private async void BtnYearly_Click(object sender, RoutedEventArgs e)
        {
            _subTab = SubTab.Yearly;
            UpdateSubTabStyles();
            await RefreshAsync();
        }

        // ── Date navigation ───────────────────────────────────────────────────

        private async void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            StepDate(-1);
            await RefreshAsync();
        }

        private async void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            StepDate(+1);
            await RefreshAsync();
        }

        private async void BtnNow_Click(object sender, RoutedEventArgs e)
        {
            _selectedDay   = DateTime.Today;
            _selectedMonth = DateTime.Today.Month;
            _selectedYear  = DateTime.Today.Year;
            await RefreshAsync();
        }

        private async void BtnExportPdf_Click(object sender, RoutedEventArgs e)
        {
            await ExportExpensesPdfAsync();
        }

        private async void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            await ExportFinancialExcelAsync();
        }

        private void StepDate(int delta)
        {
            switch (_subTab)
            {
                case SubTab.Daily:
                    _selectedDay = _selectedDay.AddDays(delta);
                    break;
                case SubTab.Monthly:
                    var d = new DateTime(_selectedYear, _selectedMonth, 1).AddMonths(delta);
                    _selectedMonth = d.Month;
                    _selectedYear  = d.Year;
                    break;
                case SubTab.Yearly:
                    _selectedYear += delta;
                    break;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // Main refresh
        // ═════════════════════════════════════════════════════════════════════

        private async Task RefreshAsync()
        {
            UpdateDateLabel();

            if (_mainTab == MainTab.Income)
                await RefreshIncomeAsync();
            else
                await RefreshExpensesAsync();
        }

        // ── Income ────────────────────────────────────────────────────────────

        private async Task RefreshIncomeAsync()
        {
            IncomeChartPanel.Visibility = Visibility.Visible;
            // 1. Load treatment names from settings (to get the full list)
            List<string> knownTreatments;
            List<Visit>  visits;

            using (var ctx = new AppDbContext())
            {
                knownTreatments = await ctx.TreatmentCosts
                    .AsNoTracking()
                    .Select(t => t.TreatmentName)
                    .ToListAsync();

                var query = ctx.Visits.AsNoTracking().AsQueryable();
                query = ApplyDateFilter(query);
                visits = await query.ToListAsync();
            }

            // 2. Aggregate paid amounts per treatment
            //    We look at TodayPaid per visit and attribute it via FIFO
            //    (same logic as FinancialRecordsView).  For statistics we only
            //    need the gross billed amount per treatment name, so we sum
            //    (Cost × Quantity) converted to SYP using the visit's snapshot.

            var totals = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach (var visit in visits)
            {
                if (visit.CurrentCost == 0 && visit.TodayPaid == 0) continue;

                double rate = visit.UsdToSypRateSnapshot > 0
                    ? visit.UsdToSypRateSnapshot
                    : 15000;

                var treatments = ParseTreatments(visit.SelectedTreatmentsJson);
                if (treatments.Count == 0)
                {
                    // No treatment detail — add to أخرى
                    double paid = visit.TodayPaid > 0 ? visit.TodayPaid : visit.CurrentCost;
                    AddTo(totals, OtherLabel, paid);
                }
                else
                {
                    foreach (var t in treatments)
                    {
                        double amountSyp = t.Currency == "USD"
                            ? (double)t.Cost * t.Quantity * rate
                            : (double)t.Cost * t.Quantity;

                        string name = string.IsNullOrWhiteSpace(t.TreatmentName)
                            ? OtherLabel
                            : t.TreatmentName;

                        AddTo(totals, name, amountSyp);
                    }
                }
            }

            // 3. Build slice list: known treatments first, then unknowns, أخرى last
            var slices = BuildIncomeSlices(knownTreatments, totals);
            DrawPie(slices);
            await RefreshIncomeChartAsync();
        }

        // ── Expenses ──────────────────────────────────────────────────────────

        private async Task RefreshExpensesAsync()
        {
            IncomeChartPanel.Visibility = Visibility.Collapsed;
            List<ExpenseEntry> expenses;
            using (var ctx = new AppDbContext())
            {
                var query = ctx.Expenses.AsNoTracking().AsQueryable();
                query = ApplyDateFilterExpenses(query);
                expenses = await query.ToListAsync();
            }
            _currentExpenses = expenses;
            ExpenseDetailsPanel.Visibility = Visibility.Collapsed;

            // Category totals
            var totals = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach (var expense in expenses)
            {
                string category = ClassifyExpense(expense.Description);
                AddTo(totals, category, expense.Amount);
            }

            // Build slices: fixed category order, then أخرى last
            var slices = new List<PieSlice>();
            var fixedOrder = ExpenseKeywords.Select(k => k.Label).ToList();
            fixedOrder.Add(OtherLabel);

            int colorIdx = 0;
            foreach (var label in fixedOrder)
            {
                if (totals.TryGetValue(label, out double amount) && amount > 0)
                {
                    slices.Add(new PieSlice
                    {
                        Label  = label,
                        Amount = amount,
                        Color  = SliceColors[colorIdx % SliceColors.Length]
                    });
                }
                colorIdx++;
            }

            DrawPie(slices);
        }

        private async Task RefreshIncomeChartAsync()
        {
            List<Visit> visits;
            using (var ctx = new AppDbContext())
            {
                var query = ctx.Visits.AsNoTracking().AsQueryable();
                if (_subTab == SubTab.Monthly)
                    query = query.Where(v => v.VisitDate.Year == _selectedYear && v.VisitDate.Month == _selectedMonth);
                else if (_subTab == SubTab.Yearly)
                    query = query.Where(v => v.VisitDate.Year == _selectedYear);
                else
                    query = query.Where(v => v.VisitDate.Date == _selectedDay.Date);
                visits = await query.ToListAsync();
            }

            var buckets = new Dictionary<string, double>();
            if (_subTab == SubTab.Monthly)
            {
                for (int day = 1; day <= DateTime.DaysInMonth(_selectedYear, _selectedMonth); day++)
                    buckets[day.ToString()] = 0;
                foreach (var visit in visits) AddTo(buckets, visit.VisitDate.Day.ToString(), IncomeAmount(visit));
            }
            else if (_subTab == SubTab.Yearly)
            {
                for (int month = 1; month <= 12; month++) buckets[ArabicMonth(month)] = 0;
                foreach (var visit in visits) AddTo(buckets, ArabicMonth(visit.VisitDate.Month), IncomeAmount(visit));
            }
            else
            {
                buckets[_selectedDay.ToString("dd/MM")] = visits.Sum(IncomeAmount);
            }

            DrawIncomeChart(buckets.Select(kv => (kv.Key, kv.Value)).ToList());
        }

        private static double IncomeAmount(Visit visit) => visit.TodayPaid > 0 ? visit.TodayPaid : visit.CurrentCost;

        private void DrawIncomeChart(List<(string Label, double Amount)> points)
        {
            IncomeChartCanvas.Children.Clear();
            if (points.Count == 0) return;

            double width = Math.Max(600, ActualWidth - 80);
            double height = 220;
            double max = Math.Max(1, points.Max(p => p.Amount));
            double step = width / Math.Max(1, points.Count);
            int skip = points.Count > 20 ? 2 : 1;

            IncomeChartCanvas.Width = width;
            IncomeChartCanvas.Height = height + 34;
            IncomeChartCanvas.Children.Add(new Line { X1 = 0, Y1 = height, X2 = width, Y2 = height, Stroke = HexBrush("#64748B"), StrokeThickness = 1 });

            for (int i = 0; i < points.Count; i++)
            {
                double barHeight = points[i].Amount <= 0 ? 0 : Math.Max(3, points[i].Amount / max * (height - 24));
                var bar = new Border { Width = Math.Max(4, step - 6), Height = barHeight, Background = HexBrush("#10B981"), CornerRadius = new CornerRadius(4, 4, 0, 0), ToolTip = $"{points[i].Label}: {points[i].Amount:N0} ل.س" };
                Canvas.SetLeft(bar, i * step + 3);
                Canvas.SetTop(bar, height - barHeight);
                IncomeChartCanvas.Children.Add(bar);

                if (i % skip == 0)
                {
                    var label = new TextBlock { Text = points[i].Label, FontSize = 10, Foreground = (Brush)Application.Current.Resources["AppTextSecondary"], Width = step, TextAlignment = TextAlignment.Center };
                    Canvas.SetLeft(label, i * step);
                    Canvas.SetTop(label, height + 6);
                    IncomeChartCanvas.Children.Add(label);
                }
            }
        }

        private void ShowExpenseDetails(string category)
        {
            var rows = _currentExpenses.Where(e => ClassifyExpense(e.Description) == category).OrderBy(e => e.ExpenseDate).ToList();
            ExpenseDetailsTitle.Text = $"تفاصيل {category} — {rows.Sum(e => e.Amount):N0} ل.س";
            ExpenseDetailsItems.Children.Clear();
            foreach (var expense in rows)
            {
                ExpenseDetailsItems.Children.Add(new TextBlock
                {
                    Text = $"{expense.ExpenseDate:dd/MM/yyyy}   {expense.Description}   —   {expense.Amount:N0} ل.س",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)Application.Current.Resources["AppTextPrimary"],
                    Margin = new Thickness(0, 4, 0, 4)
                });
            }
            ExpenseDetailsPanel.Visibility = Visibility.Visible;
        }

        private async Task ExportExpensesPdfAsync()
        {
            List<ExpenseEntry> expenses;
            using (var ctx = new AppDbContext())
            {
                var query = ApplyDateFilterExpenses(ctx.Expenses.AsNoTracking());
                expenses = await query.OrderBy(e => e.ExpenseDate).ToListAsync();
            }

            var dialog = new SaveFileDialog { Filter = "PDF files (*.pdf)|*.pdf", FileName = $"تقرير-المصاريف-{DateTime.Now:yyyyMMdd-HHmm}.pdf" };
            if (dialog.ShowDialog() != true) return;

            var grouped = expenses.GroupBy(e => ClassifyExpense(e.Description))
                .OrderBy(g => Array.IndexOf(ExpenseKeywords.Select(k => k.Label).Append(OtherLabel).ToArray(), g.Key))
                .ToList();
            var pages = new List<byte[]>();
            if (grouped.Count == 0) pages.Add(RenderPdfPage(new List<(string, List<ExpenseEntry>)> { ("لا توجد مصاريف", new List<ExpenseEntry>()) }));
            else
            {
                var pageGroups = new List<(string, List<ExpenseEntry>)>();
                foreach (var group in grouped)
                {
                    if (pageGroups.Count > 0 && pageGroups.Sum(x => x.Item2.Count) >= 18) { pages.Add(RenderPdfPage(pageGroups)); pageGroups.Clear(); }
                    pageGroups.Add((group.Key, group.OrderBy(e => e.ExpenseDate).ToList()));
                }
                if (pageGroups.Count > 0) pages.Add(RenderPdfPage(pageGroups));
            }
            File.WriteAllBytes(dialog.FileName, BuildImagePdf(pages));
            MessageBox.Show("تم تصدير التقرير بنجاح.", "تصدير PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private byte[] RenderPdfPage(List<(string Category, List<ExpenseEntry> Items)> groups)
        {
            var canvas = new Canvas { Width = 794, Height = 1123, Background = Brushes.White, FlowDirection = FlowDirection.RightToLeft };
            var title = PdfText($"تقرير المصاريف - {PeriodText()}", 28, FontWeights.Bold, 720);
            Canvas.SetLeft(title, 36); Canvas.SetTop(title, 36); canvas.Children.Add(title);
            var subtitle = PdfText("التقرير للفترة المحددة في الإحصائيات", 14, FontWeights.Normal, 720);
            Canvas.SetLeft(subtitle, 36); Canvas.SetTop(subtitle, 78); canvas.Children.Add(subtitle);
            double top = 120;
            foreach (var group in groups)
            {
                var header = PdfText($"{group.Category} - المجموع: {group.Items.Sum(e => e.Amount):N0} ل.س", 20, FontWeights.Bold, 720);
                header.Foreground = HexBrush("#1D4ED8");
                Canvas.SetLeft(header, 36); Canvas.SetTop(header, top); canvas.Children.Add(header); top += 34;
                AddPdfColumnHeader(canvas, "الوصف", 410, 310, top);
                AddPdfColumnHeader(canvas, "التاريخ", 210, 170, top);
                AddPdfColumnHeader(canvas, "المبلغ", 36, 140, top);
                top += 25;
                foreach (var item in group.Items)
                {
                    AddPdfColumn(canvas, item.Description, 410, 310, top);
                    AddPdfColumn(canvas, item.ExpenseDate.ToString("dd/MM/yyyy"), 210, 170, top);
                    AddPdfColumn(canvas, $"{item.Amount:N0} ل.س", 36, 140, top);
                    top += 25;
                }
                top += 18;
            }
            canvas.Measure(new Size(794, 1123)); canvas.Arrange(new Rect(0, 0, 794, 1123)); canvas.UpdateLayout();
            var bitmap = new RenderTargetBitmap(794, 1123, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas);
            var encoder = new JpegBitmapEncoder { QualityLevel = 92 }; encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
        }

        private static TextBlock PdfText(string text, double size, FontWeight weight, double width)
        {
            return new TextBlock
            {
                Text = text,
                Width = width,
                FontSize = size,
                FontWeight = weight,
                Foreground = Brushes.Black,
                TextAlignment = TextAlignment.Right,
                FlowDirection = FlowDirection.RightToLeft,
                Language = XmlLanguage.GetLanguage("ar-SA")
            };
        }

        private static void AddPdfColumnHeader(Canvas canvas, string text, double left, double width, double top)
        {
            var header = PdfText(text, 13, FontWeights.Bold, width);
            header.Foreground = HexBrush("#475569");
            Canvas.SetLeft(header, left); Canvas.SetTop(header, top); canvas.Children.Add(header);
        }

        private static void AddPdfColumn(Canvas canvas, string text, double left, double width, double top)
        {
            var value = PdfText(text, 14, FontWeights.Normal, width);
            Canvas.SetLeft(value, left); Canvas.SetTop(value, top); canvas.Children.Add(value);
        }

        private async Task ExportFinancialExcelAsync()
        {
            List<Visit> visits;
            List<ExpenseEntry> expenses;
            using (var ctx = new AppDbContext())
            {
                visits = await ApplyDateFilter(ctx.Visits.AsNoTracking()).ToListAsync();
                expenses = await ApplyDateFilterExpenses(ctx.Expenses.AsNoTracking()).ToListAsync();
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"تقرير-الدخل-والمصاريف-{DateTime.Now:yyyyMMdd-HHmm}.xlsx"
            };
            if (dialog.ShowDialog() != true) return;

            var periods = BuildExportPeriods();
            var summaryRows = periods.Select(period =>
            {
                double income = visits.Where(v => MatchesPeriod(v.VisitDate, period)).Sum(IncomeAmount);
                double expense = expenses.Where(e => MatchesPeriod(e.ExpenseDate, period)).Sum(e => e.Amount);
                return (Label: period.Label, Income: income, Expense: expense, Net: income - expense);
            }).ToList();

            var details = new List<(DateTime Date, string Type, string Description, double Amount, string Category)>();
            details.AddRange(visits.Where(v => MatchesSelectedPeriod(v.VisitDate)).Select(v =>
                (v.VisitDate, "دخل", $"زيارة رقم {v.Id}", IncomeAmount(v), "الدخل")));
            details.AddRange(expenses.Where(e => MatchesSelectedPeriod(e.ExpenseDate)).Select(e =>
                (e.ExpenseDate, "مصروف", e.Description, e.Amount, ClassifyExpense(e.Description))));
            details = details.OrderBy(d => d.Date).ThenBy(d => d.Type).ToList();

            File.WriteAllBytes(dialog.FileName, BuildExcelWorkbook(summaryRows, details));
            MessageBox.Show("تم تصدير ملف Excel بنجاح.", "تصدير Excel", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private List<(DateTime Date, string Label)> BuildExportPeriods()
        {
            if (_subTab == SubTab.Daily)
                return new List<(DateTime, string)> { (_selectedDay.Date, _selectedDay.ToString("dd/MM/yyyy")) };
            if (_subTab == SubTab.Monthly)
                return Enumerable.Range(1, DateTime.DaysInMonth(_selectedYear, _selectedMonth))
                    .Select(day => (new DateTime(_selectedYear, _selectedMonth, day), day.ToString("00/MM/yyyy"))).ToList();
            return Enumerable.Range(1, 12)
                .Select(month => (new DateTime(_selectedYear, month, 1), $"{ArabicMonth(month)} {_selectedYear}")).ToList();
        }

        private bool MatchesPeriod(DateTime value, (DateTime Date, string Label) period)
        {
            if (_subTab == SubTab.Daily) return value.Date == period.Date.Date;
            if (_subTab == SubTab.Monthly) return value.Year == period.Date.Year && value.Month == period.Date.Month && value.Day == period.Date.Day;
            return value.Year == period.Date.Year && value.Month == period.Date.Month;
        }

        private bool MatchesSelectedPeriod(DateTime value)
        {
            return _subTab switch
            {
                SubTab.Daily => value.Date == _selectedDay.Date,
                SubTab.Monthly => value.Year == _selectedYear && value.Month == _selectedMonth,
                _ => value.Year == _selectedYear
            };
        }

        private static byte[] BuildExcelWorkbook(
            List<(string Label, double Income, double Expense, double Net)> summary,
            List<(DateTime Date, string Type, string Description, double Amount, string Category)> details)
        {
            var files = new Dictionary<string, string>
            {
                ["[Content_Types].xml"] = ExcelContentTypes(),
                ["_rels/.rels"] = ExcelRootRels(),
                ["xl/workbook.xml"] = ExcelWorkbook(),
                ["xl/_rels/workbook.xml.rels"] = ExcelWorkbookRels(),
                ["xl/styles.xml"] = ExcelStyles(),
                ["xl/worksheets/sheet1.xml"] = ExcelSummarySheet(summary),
                ["xl/worksheets/sheet2.xml"] = ExcelDetailsSheet(details)
            };
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in files)
                {
                    var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
                    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    writer.Write(file.Value);
                }
            }
            return stream.ToArray();
        }

        private static string ExcelContentTypes() => "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>";

        private static string ExcelRootRels() => "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>";

        private static string ExcelWorkbook() => "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"الملخص\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"التفاصيل\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>";

        private static string ExcelWorkbookRels() => "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>";

        private static string ExcelStyles() => "<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Arial\"/></font><font><b/><sz val=\"14\"/><name val=\"Arial\"/></font></fonts><fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD9EAF7\"/></patternFill></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellXfs count=\"6\"><xf/><xf fontId=\"1\"/><xf fillId=\"2\" fontId=\"1\"/><xf fillId=\"2\" fontId=\"0\"/><xf numFmtId=\"14\"/><xf numFmtId=\"4\"/></cellXfs></styleSheet>";

        private static string ExcelSummarySheet(List<(string Label, double Income, double Expense, double Net)> rows)
        {
            var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView rightToLeft=\"1\" workbookViewId=\"0\"/></sheetViews><sheetFormatPr defaultRowHeight=\"20\"/><cols><col min=\"1\" max=\"1\" width=\"24\"/><col min=\"2\" max=\"4\" width=\"18\"/></cols><sheetData>");
            xml.Append("<row r=\"1\"><c r=\"A1\" s=\"1\" t=\"inlineStr\"><is><t>ملخص الدخل والمصاريف</t></is></c></row>");
            xml.Append("<row r=\"3\"><c r=\"A3\" s=\"2\" t=\"inlineStr\"><is><t>الفترة</t></is></c><c r=\"B3\" s=\"2\" t=\"inlineStr\"><is><t>الدخل</t></is></c><c r=\"C3\" s=\"2\" t=\"inlineStr\"><is><t>المصاريف</t></is></c><c r=\"D3\" s=\"2\" t=\"inlineStr\"><is><t>الصافي</t></is></c></row>");
            for (int i = 0; i < rows.Count; i++)
            {
                int r = i + 4;
                xml.Append($"<row r=\"{r}\">{InlineCell($"A{r}", rows[i].Label, 0)}{NumberCell($"B{r}", rows[i].Income)}{NumberCell($"C{r}", rows[i].Expense)}{NumberCell($"D{r}", rows[i].Net)}</row>");
            }
            int total = rows.Count + 4;
            int last = total - 1;
            xml.Append($"<row r=\"{total}\"><c r=\"A{total}\" s=\"3\" t=\"inlineStr\"><is><t>الإجمالي</t></is></c>{FormulaCell($"B{total}", $"SUM(B4:B{last})")}{FormulaCell($"C{total}", $"SUM(C4:C{last})")}{FormulaCell($"D{total}", $"SUM(D4:D{last})")}</row>");
            xml.Append("</sheetData><mergeCells count=\"1\"><mergeCell ref=\"A1:D1\"/></mergeCells></worksheet>");
            return xml.ToString();
        }

        private static string ExcelDetailsSheet(List<(DateTime Date, string Type, string Description, double Amount, string Category)> rows)
        {
            var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView rightToLeft=\"1\" workbookViewId=\"0\"/></sheetViews><cols><col min=\"1\" max=\"1\" width=\"16\"/><col min=\"2\" max=\"2\" width=\"14\"/><col min=\"3\" max=\"3\" width=\"38\"/><col min=\"4\" max=\"4\" width=\"18\"/><col min=\"5\" max=\"5\" width=\"18\"/></cols><sheetData>");
            xml.Append("<row r=\"1\"><c r=\"A1\" s=\"1\" t=\"inlineStr\"><is><t>تفاصيل الدخل والمصاريف</t></is></c></row>");
            xml.Append("<row r=\"3\"><c r=\"A3\" s=\"2\" t=\"inlineStr\"><is><t>التاريخ</t></is></c><c r=\"B3\" s=\"2\" t=\"inlineStr\"><is><t>النوع</t></is></c><c r=\"C3\" s=\"2\" t=\"inlineStr\"><is><t>الوصف</t></is></c><c r=\"D3\" s=\"2\" t=\"inlineStr\"><is><t>المبلغ</t></is></c><c r=\"E3\" s=\"2\" t=\"inlineStr\"><is><t>التصنيف</t></is></c></row>");
            for (int i = 0; i < rows.Count; i++)
            {
                int r = i + 4;
                xml.Append($"<row r=\"{r}\">{DateCell($"A{r}", rows[i].Date)}{InlineCell($"B{r}", rows[i].Type, 0)}{InlineCell($"C{r}", rows[i].Description, 0)}{NumberCell($"D{r}", rows[i].Amount)}{InlineCell($"E{r}", rows[i].Category, 0)}</row>");
            }
            int total = rows.Count + 4;
            int last = total - 1;
            xml.Append($"<row r=\"{total}\"><c r=\"C{total}\" s=\"3\" t=\"inlineStr\"><is><t>الإجمالي</t></is></c>{FormulaCell($"D{total}", $"SUM(D4:D{last})")}</row>");
            xml.Append("</sheetData><mergeCells count=\"1\"><mergeCell ref=\"A1:E1\"/></mergeCells></worksheet>");
            return xml.ToString();
        }

        private static string InlineCell(string reference, string value, int style) => $"<c r=\"{reference}\" s=\"{style}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(value) ?? string.Empty}</t></is></c>";
        private static string NumberCell(string reference, double value) => $"<c r=\"{reference}\" s=\"5\"><v>{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>";
        private static string DateCell(string reference, DateTime value) => $"<c r=\"{reference}\" s=\"4\"><v>{value.ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>";
        private static string FormulaCell(string reference, string formula) => $"<c r=\"{reference}\" s=\"5\"><f>{formula}</f><v>0</v></c>";

        private string PeriodText() => _subTab switch
        {
            SubTab.Daily => _selectedDay.ToString("dd/MM/yyyy"),
            SubTab.Monthly => $"{ArabicMonth(_selectedMonth)} {_selectedYear}",
            _ => _selectedYear.ToString()
        };

        private static byte[] BuildImagePdf(List<byte[]> images)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            var offsets = new List<long>();
            void Obj(int number, string body) { offsets.Add(stream.Position); writer.Write(System.Text.Encoding.ASCII.GetBytes($"{number} 0 obj\n{body}\nendobj\n")); }
            writer.Write(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n"));
            int pageCount = images.Count, next = 3 + pageCount * 2;
            Obj(1, $"<< /Type /Catalog /Pages 2 0 R >>");
            Obj(2, $"<< /Type /Pages /Kids [{string.Join(" ", Enumerable.Range(0, pageCount).Select(i => $"{3 + i * 2} 0 R"))}] /Count {pageCount} >>");
            for (int i = 0; i < pageCount; i++)
            {
                int page = 3 + i * 2, image = page + 1;
                Obj(page, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 794 1123] /Resources << /XObject << /Im0 {image} 0 R >> >> /Contents {next} 0 R >>");
                offsets.Add(stream.Position); writer.Write(System.Text.Encoding.ASCII.GetBytes($"{image} 0 obj\n<< /Type /XObject /Subtype /Image /Width 794 /Height 1123 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {images[i].Length} >>\nstream\n")); writer.Write(images[i]); writer.Write(System.Text.Encoding.ASCII.GetBytes("\nendstream\nendobj\n"));
            }
            const string content = "q 794 0 0 1123 0 0 cm /Im0 Do Q\n";
            offsets.Add(stream.Position); writer.Write(System.Text.Encoding.ASCII.GetBytes($"{next} 0 obj\n<< /Length {content.Length} >>\nstream\n{content}endstream\nendobj\n"));
            long xref = stream.Position; writer.Write(System.Text.Encoding.ASCII.GetBytes($"xref\n0 {next + 1}\n0000000000 65535 f \n")); foreach (var offset in offsets) writer.Write(System.Text.Encoding.ASCII.GetBytes($"{offset:0000000000} 00000 n \n")); writer.Write(System.Text.Encoding.ASCII.GetBytes($"trailer\n<< /Size {next + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF")); return stream.ToArray();
        }

        // ═════════════════════════════════════════════════════════════════════
        // Pie drawing
        // ═════════════════════════════════════════════════════════════════════

        private void DrawPie(List<PieSlice> slices)
        {
            PieCanvas.Children.Clear();
            LegendPanel.Children.Clear();

            double total = slices.Sum(s => s.Amount);

            // No data
            if (total <= 0 || slices.Count == 0)
            {
                TblNoData.Visibility = Visibility.Visible;
                TblTotal.Text        = "";
                return;
            }
            TblNoData.Visibility = Visibility.Collapsed;

            double cx = 140, cy = 140, r = 128, innerR = 56;
            double startAngle = -90.0; // start at 12 o'clock

            foreach (var slice in slices)
            {
                double sweep = (slice.Amount / total) * 360.0;
                // Clamp to avoid degenerate arcs at exactly 360
                if (sweep >= 360) sweep = 359.9999;

                var brush = HexBrush(slice.Color);

                if (slices.Count == 1)
                {
                    // Full ring
                    var outer = new Ellipse
                    {
                        Width = r * 2, Height = r * 2,
                        Fill = brush
                    };
                    Canvas.SetLeft(outer, cx - r);
                    Canvas.SetTop(outer,  cy - r);
                    PieCanvas.Children.Add(outer);
                }
                else
                {
                    var path = CreateDonutSlice(cx, cy, r, innerR, startAngle, sweep, brush);
                    PieCanvas.Children.Add(path);
                }

                startAngle += sweep;
            }

            // Centre hole (white/card bg)
            if (slices.Count > 1)
            {
                var hole = new Ellipse
                {
                    Width  = innerR * 2,
                    Height = innerR * 2,
                    Fill   = (Brush)Application.Current.Resources["AppWindowBg"]
                             ?? new SolidColorBrush(Color.FromRgb(11, 17, 32))
                };
                Canvas.SetLeft(hole, cx - innerR);
                Canvas.SetTop(hole,  cy - innerR);
                PieCanvas.Children.Add(hole);
            }

            // Total label
            TblTotal.Text = $"الإجمالي: {total:N0} ل.س";

            // Legend
            BuildLegend(slices, total);
        }

        private static System.Windows.Shapes.Path CreateDonutSlice(
            double cx, double cy, double outerR, double innerR,
            double startDeg, double sweepDeg, Brush fill)
        {
            double startRad = DegToRad(startDeg);
            double endRad   = DegToRad(startDeg + sweepDeg);

            // Outer arc points
            var outerStart = new Point(cx + outerR * Math.Cos(startRad), cy + outerR * Math.Sin(startRad));
            var outerEnd   = new Point(cx + outerR * Math.Cos(endRad),   cy + outerR * Math.Sin(endRad));

            // Inner arc points (reversed)
            var innerEnd   = new Point(cx + innerR * Math.Cos(endRad),   cy + innerR * Math.Sin(endRad));
            var innerStart = new Point(cx + innerR * Math.Cos(startRad), cy + innerR * Math.Sin(startRad));

            bool largeArc = sweepDeg > 180;

            var figure = new PathFigure { StartPoint = outerStart, IsClosed = true };
            // Outer arc (clockwise)
            figure.Segments.Add(new ArcSegment(outerEnd, new Size(outerR, outerR), 0,
                largeArc, SweepDirection.Clockwise, true));
            // Line to inner arc end
            figure.Segments.Add(new LineSegment(innerEnd, true));
            // Inner arc (counter-clockwise)
            figure.Segments.Add(new ArcSegment(innerStart, new Size(innerR, innerR), 0,
                largeArc, SweepDirection.Counterclockwise, true));

            return new System.Windows.Shapes.Path
            {
                Data = new PathGeometry { Figures = { figure } },
                Fill = fill,
                Stroke = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
                StrokeThickness = 1
            };
        }

        private void BuildLegend(List<PieSlice> slices, double total)
        {
            // Title
            var title = new TextBlock
            {
                Text       = "التفاصيل",
                FontSize   = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["AppTextSecondary"],
                Margin     = new Thickness(0, 0, 0, 10)
            };
            LegendPanel.Children.Add(title);

            foreach (var slice in slices)
            {
                double pct = total > 0 ? (slice.Amount / total) * 100 : 0;

                var row = new Grid { Margin = new Thickness(0, 4, 0, 4), Cursor = Cursors.Hand };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Colour swatch
                var swatch = new Border
                {
                    Width        = 12,
                    Height       = 12,
                    CornerRadius = new CornerRadius(3),
                    Background   = HexBrush(slice.Color),
                    Margin       = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(swatch, 0);

                // Label + percentage
                var labelBlock = new TextBlock
                {
                    Text                = $"{slice.Label}  ({pct:N1}%)",
                    FontSize            = 13,
                    Foreground          = (Brush)Application.Current.Resources["AppTextPrimary"],
                    VerticalAlignment   = VerticalAlignment.Center,
                    TextTrimming        = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(labelBlock, 1);

                // Amount
                var amtBlock = new TextBlock
                {
                    Text              = $"{slice.Amount:N0}",
                    FontSize          = 13,
                    FontWeight        = FontWeights.SemiBold,
                    Foreground        = HexBrush(slice.Color),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(12, 0, 0, 0)
                };
                Grid.SetColumn(amtBlock, 2);

                row.Children.Add(swatch);
                row.Children.Add(labelBlock);
                row.Children.Add(amtBlock);

                if (_mainTab == MainTab.Expenses)
                    row.MouseLeftButtonUp += (_, _) => ShowExpenseDetails(slice.Label);

                LegendPanel.Children.Add(row);

                // Thin separator
                LegendPanel.Children.Add(new Border
                {
                    Height     = 1,
                    Background = (Brush)Application.Current.Resources["AppBorder"],
                    Margin     = new Thickness(0, 2, 0, 2),
                    Opacity    = 0.5
                });
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // Helpers
        // ═════════════════════════════════════════════════════════════════════

        // Date filter for Visits
        private IQueryable<Visit> ApplyDateFilter(IQueryable<Visit> query)
        {
            switch (_subTab)
            {
                case SubTab.Daily:
                    var day = _selectedDay.Date;
                    return query.Where(v => v.VisitDate.Date == day);
                case SubTab.Monthly:
                    return query.Where(v => v.VisitDate.Year == _selectedYear
                                        && v.VisitDate.Month == _selectedMonth);
                case SubTab.Yearly:
                    return query.Where(v => v.VisitDate.Year == _selectedYear);
                default: return query;
            }
        }

        // Date filter for Expenses
        private IQueryable<ExpenseEntry> ApplyDateFilterExpenses(IQueryable<ExpenseEntry> query)
        {
            switch (_subTab)
            {
                case SubTab.Daily:
                    var day = _selectedDay.Date;
                    return query.Where(e => e.ExpenseDate.Date == day);
                case SubTab.Monthly:
                    return query.Where(e => e.ExpenseDate.Year == _selectedYear
                                         && e.ExpenseDate.Month == _selectedMonth);
                case SubTab.Yearly:
                    return query.Where(e => e.ExpenseDate.Year == _selectedYear);
                default: return query;
            }
        }

        // Classify an expense description into a category label
        private static string ClassifyExpense(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return OtherLabel;
            foreach (var (keyword, label) in ExpenseKeywords)
                if (description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return label;
            return OtherLabel;
        }

        // Build income slices in fixed order (known treatments → unknowns → أخرى)
        private static List<PieSlice> BuildIncomeSlices(
            List<string> knownTreatments,
            Dictionary<string, double> totals)
        {
            var slices    = new List<PieSlice>();
            var used      = new HashSet<string>(StringComparer.Ordinal);
            int colorIdx  = 0;

            // Known treatments first
            foreach (var name in knownTreatments)
            {
                if (totals.TryGetValue(name, out double amt) && amt > 0)
                {
                    slices.Add(new PieSlice
                    {
                        Label  = name,
                        Amount = amt,
                        Color  = SliceColors[colorIdx % SliceColors.Length]
                    });
                    colorIdx++;
                }
                used.Add(name);
            }

            // Unknown treatment names (not in settings — shouldn't normally happen)
            foreach (var kv in totals)
            {
                if (used.Contains(kv.Key) || kv.Key == OtherLabel) continue;
                if (kv.Value <= 0) continue;
                slices.Add(new PieSlice
                {
                    Label  = kv.Key,
                    Amount = kv.Value,
                    Color  = SliceColors[colorIdx % SliceColors.Length]
                });
                colorIdx++;
            }

            // أخرى last
            if (totals.TryGetValue(OtherLabel, out double other) && other > 0)
            {
                slices.Add(new PieSlice
                {
                    Label  = OtherLabel,
                    Amount = other,
                    Color  = SliceColors[colorIdx % SliceColors.Length]
                });
            }

            return slices;
        }

        private static void AddTo(Dictionary<string, double> dict, string key, double amount)
        {
            if (!dict.ContainsKey(key)) dict[key] = 0;
            dict[key] += amount;
        }

        private static List<SelectedTreatment> ParseTreatments(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<SelectedTreatment>();
            try
            {
                return JsonSerializer.Deserialize<List<SelectedTreatment>>(json)
                       ?? new List<SelectedTreatment>();
            }
            catch { return new List<SelectedTreatment>(); }
        }

        // Update the date label text
        private void UpdateDateLabel()
        {
            TblDateLabel.Text = _subTab switch
            {
                SubTab.Daily   => _selectedDay.ToString("dd / MM / yyyy"),
                SubTab.Monthly => $"{ArabicMonth(_selectedMonth)} {_selectedYear}",
                SubTab.Yearly  => _selectedYear.ToString(),
                _              => ""
            };
        }

        private static string ArabicMonth(int m) => m switch
        {
            1  => "يناير",  2  => "فبراير", 3  => "مارس",
            4  => "أبريل",  5  => "مايو",   6  => "يونيو",
            7  => "يوليو",  8  => "أغسطس",  9  => "سبتمبر",
            10 => "أكتوبر", 11 => "نوفمبر", 12 => "ديسمبر",
            _  => m.ToString()
        };

        // Visual tab/subtab active state
        private void UpdateTabStyles()
        {
            var active   = FindResource("MainTabBtnActive") as Style;
            var inactive = FindResource("MainTabBtn")       as Style;

            BtnTabIncome.Style   = _mainTab == MainTab.Income   ? active : inactive;
            BtnTabExpenses.Style = _mainTab == MainTab.Expenses ? active : inactive;
        }

        private void UpdateSubTabStyles()
        {
            var active   = FindResource("SubTabBtnActive") as Style;
            var inactive = FindResource("SubTabBtn")       as Style;

            BtnDaily.Style   = _subTab == SubTab.Daily   ? active : inactive;
            BtnMonthly.Style = _subTab == SubTab.Monthly ? active : inactive;
            BtnYearly.Style  = _subTab == SubTab.Yearly  ? active : inactive;
        }

        // Hex colour string → SolidColorBrush
        private static SolidColorBrush HexBrush(string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch
            {
                return new SolidColorBrush(Colors.Gray);
            }
        }

        private static double DegToRad(double deg) => deg * Math.PI / 180.0;

        // ── Inner types ───────────────────────────────────────────────────────

        private class PieSlice
        {
            public string Label  { get; set; } = "";
            public double Amount { get; set; }
            public string Color  { get; set; } = "#3B82F6";
        }
    }
}

using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace MyClinic
{
    public partial class StatisticsView : UserControl
    {
        // ── State ─────────────────────────────────────────────────────────────
        private enum MainTab { Income, Expenses, Visits, Treatments }
        private enum SubTab  { Daily, Monthly, Yearly }
        private enum PieMetric { Money, Count }
        private enum IncomeChartType { Area, Columns }

        private MainTab _mainTab   = MainTab.Income;
        private SubTab  _subTab    = SubTab.Monthly;
        private PieMetric _pieMetric = PieMetric.Money;
        private IncomeChartType _incomeChartType = IncomeChartType.Area;

        private DateTime _selectedDay   = DateTime.Today;
        private int      _selectedMonth = DateTime.Today.Month;
        private int      _selectedYear  = DateTime.Today.Year;

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
            UpdateSubTabStyles();
            UpdateIncomeChartStyles();
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

        private async void BtnTabVisits_Click(object sender, RoutedEventArgs e)
        {
            _mainTab = MainTab.Visits;
            UpdateTabStyles();
            await RefreshAsync();
        }

        private async void BtnTabTreatments_Click(object sender, RoutedEventArgs e)
        {
            _mainTab = MainTab.Treatments;
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

        private async void BtnIncomeArea_Click(object sender, RoutedEventArgs e)
        {
            _incomeChartType = IncomeChartType.Area;
            UpdateIncomeChartStyles();
            if (_mainTab == MainTab.Income)
                await RefreshAsync();
        }

        private async void BtnIncomeColumns_Click(object sender, RoutedEventArgs e)
        {
            _incomeChartType = IncomeChartType.Columns;
            UpdateIncomeChartStyles();
            if (_mainTab == MainTab.Income)
                await RefreshAsync();
        }

        private async void BtnExportExpensesPdf_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                List<ExpenseEntry> expenses;
                using (var ctx = new AppDbContext())
                {
                    expenses = await ApplyDateFilterExpenses(ctx.Expenses.AsNoTracking().AsQueryable())
                        .OrderBy(e => e.ExpenseDate)
                        .ToListAsync();
                }

                SaveFileDialog dialog = new()
                {
                    Title = "تصدير تفاصيل المصاريف",
                    Filter = "ملف PDF (*.pdf)|*.pdf",
                    FileName = $"المصاريف-{PeriodFileLabel()}.pdf",
                    AddExtension = true,
                    OverwritePrompt = true
                };
                if (dialog.ShowDialog() != true) return;

                File.WriteAllBytes(dialog.FileName, BuildImagePdf(BuildExpensePdfPages(expenses)));
                MessageBox.Show("تم تصدير تفاصيل المصاريف بنجاح.", "التصدير", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر تصدير المصاريف حالياً.\n{ex.Message}", "التصدير", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnExportIncomeExcel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DateTime start = SelectedPeriodStart();
                DateTime end = SelectedPeriodEnd();
                List<IncomeExportDetail> details;
                List<IncomeExportSummary> summary;
                List<TreatmentIncomeExportRow> treatmentSummary;

                using (var ctx = new AppDbContext())
                {
                    List<Visit> allVisits = await ctx.Visits.AsNoTracking().ToListAsync();
                    List<Visit> visits = allVisits
                        .Where(v => v.VisitDate >= start && v.VisitDate < end.AddDays(1))
                        .Where(v => v.CurrentCost != 0 || v.TodayPaid > 0)
                        .ToList();
                    var expenses = await ctx.Expenses.AsNoTracking()
                        .Where(e => e.ExpenseDate >= start && e.ExpenseDate < end.AddDays(1))
                        .Select(e => new { e.ExpenseDate, e.Description, e.Amount })
                        .ToListAsync();

                    details = visits.Select(v => new IncomeExportDetail(v.VisitDate, "دخل", $"زيارة رقم {v.Id}", v.TodayPaid, "الدخل"))
                        .Concat(expenses.Select(e => new IncomeExportDetail(e.ExpenseDate, "مصروف", e.Description, e.Amount, ClassifyExpense(e.Description))))
                        .OrderBy(d => d.Date)
                        .ToList();

                    summary = BuildIncomeSummary(start, end, visits.Select(v => (v.VisitDate, v.TodayPaid)), expenses.Select(e => (e.ExpenseDate, e.Amount)));
                    treatmentSummary = BuildTreatmentIncomeExportRows(allVisits, visits);
                }

                SaveFileDialog dialog = new()
                {
                    Title = "تصدير إجمالي الدخل",
                    Filter = "ملف Excel (*.xlsx)|*.xlsx",
                    FileName = $"الدخل-{PeriodFileLabel()}.xlsx",
                    AddExtension = true,
                    OverwritePrompt = true
                };
                if (dialog.ShowDialog() != true) return;

                File.WriteAllBytes(dialog.FileName, BuildIncomeWorkbook(summary, details, treatmentSummary));
                MessageBox.Show("تم تصدير إجمالي الدخل بنجاح.", "التصدير", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر تصدير الدخل حالياً.\n{ex.Message}", "التصدير", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnExportTreatmentsExcel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DateTime start = SelectedPeriodStart();
                DateTime end = SelectedPeriodEnd();
                List<TreatmentIncomeExportRow> treatmentSummary;

                using (var ctx = new AppDbContext())
                {
                    List<Visit> allVisits = await ctx.Visits.AsNoTracking().ToListAsync();
                    List<Visit> selectedVisits = allVisits
                        .Where(v => v.VisitDate >= start && v.VisitDate < end.AddDays(1))
                        .ToList();
                    List<string> knownTreatments = await ctx.TreatmentCosts
                        .AsNoTracking()
                        .Select(t => t.TreatmentName)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct()
                        .ToListAsync();
                    treatmentSummary = BuildTreatmentIncomeExportRows(allVisits, selectedVisits, knownTreatments);
                }

                SaveFileDialog dialog = new()
                {
                    Title = "تصدير إحصائيات العلاجات",
                    Filter = "ملف Excel (*.xlsx)|*.xlsx",
                    FileName = $"العلاجات-{PeriodFileLabel()}.xlsx",
                    AddExtension = true,
                    OverwritePrompt = true
                };
                if (dialog.ShowDialog() != true) return;

                File.WriteAllBytes(dialog.FileName, BuildTreatmentWorkbook(treatmentSummary));
                MessageBox.Show("تم تصدير إحصائيات العلاجات بنجاح.", "التصدير", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر تصدير إحصائيات العلاجات حالياً.\n{ex.Message}", "التصدير", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

            switch (_mainTab)
            {
                case MainTab.Income:
                    await RefreshIncomeAsync();
                    break;
                case MainTab.Expenses:
                    await RefreshExpensesAsync();
                    break;
                case MainTab.Visits:
                    await RefreshVisitsAsync();
                    break;
                case MainTab.Treatments:
                    await RefreshTreatmentsAsync();
                    break;
            }
        }

        // ── Income ────────────────────────────────────────────────────────────

        private async Task RefreshIncomeAsync()
        {
            _pieMetric = PieMetric.Money;
            // 1. Load treatment names from settings (to get the full list)
            List<string> knownTreatments;
            List<Visit>  allVisits;

            using (var ctx = new AppDbContext())
            {
                knownTreatments = (await ctx.TreatmentCosts
                    .AsNoTracking()
                    .Select(t => t.TreatmentName)
                    .ToListAsync())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name)
                    .ToList();

                // Payment allocation needs the patient's complete treatment/payment
                // history, even when the pie itself is filtered to one day/month/year.
                allVisits = await ctx.Visits
                    .AsNoTracking()
                    .ToListAsync();
            }

            var visits = allVisits
                .Where(IsInSelectedPeriod)
                .Where(v => v.CurrentCost != 0 || v.TodayPaid > 0)
                .ToList();

            // 2. Aggregate actual paid amounts per treatment using the same FIFO
            //    allocation as الشؤون المالية. Billed-but-unpaid treatment costs
            //    must not be included in an income chart.
            var totals = BuildTreatmentIncomeTotals(allVisits, visits);

            // 3. Build slice list: known treatments first, then unknowns, أخرى last
            var slices = BuildIncomeSlices(knownTreatments, totals);
            DrawPie(slices);
            DrawIncomeChart(visits);
        }

        private static Dictionary<string, double> BuildTreatmentIncomeTotals(
            List<Visit> allVisits,
            IEnumerable<Visit> selectedVisits)
        {
            var totals = new Dictionary<string, double>(StringComparer.Ordinal);
            var patientVisits = allVisits
                .GroupBy(v => v.PatientId)
                .ToDictionary(g => g.Key, g => g.OrderBy(v => v.VisitDate).ThenBy(v => v.Id).ToList());

            foreach (var visit in selectedVisits)
            {
                if (visit.TodayPaid <= 0.009) continue;

                if (!patientVisits.TryGetValue(visit.PatientId, out var history))
                {
                    AddTo(totals, OtherLabel, visit.TodayPaid);
                    continue;
                }

                var queue = BuildTreatmentQueue(history);
                double cumulativePaidBefore = history
                    .Where(v => v.VisitDate < visit.VisitDate || (v.VisitDate == visit.VisitDate && v.Id < visit.Id))
                    .Sum(v => v.TodayPaid);
                var allocations = AllocatePayment(queue, cumulativePaidBefore, visit.TodayPaid);

                foreach (var allocation in allocations)
                {
                    string name = string.IsNullOrWhiteSpace(allocation.Name)
                        ? OtherLabel
                        : allocation.Name;
                    AddTo(totals, name, allocation.Amount);
                }

                double allocated = allocations.Sum(a => a.Amount);
                if (visit.TodayPaid - allocated > 0.009)
                    AddTo(totals, OtherLabel, visit.TodayPaid - allocated);
            }

            return totals;
        }

        private static List<TreatmentIncomeExportRow> BuildTreatmentIncomeExportRows(
            List<Visit> allVisits,
            List<Visit> selectedVisits,
            IEnumerable<string>? knownTreatments = null)
        {
            var incomeTotals = BuildTreatmentIncomeTotals(allVisits, selectedVisits);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var visit in selectedVisits)
            {
                foreach (var treatment in ParseTreatments(visit.SelectedTreatmentsJson))
                {
                    string name = string.IsNullOrWhiteSpace(treatment.TreatmentName)
                        ? OtherLabel
                        : treatment.TreatmentName;
                    int quantity = Math.Max(1, treatment.Quantity);
                    counts[name] = counts.GetValueOrDefault(name) + quantity;
                }
            }

            foreach (string name in incomeTotals.Keys)
            {
                if (!counts.ContainsKey(name)) counts[name] = 0;
            }

            foreach (string name in knownTreatments ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(name) && !counts.ContainsKey(name))
                    counts[name] = 0;
            }

            return incomeTotals.Keys
                .Union(counts.Keys, StringComparer.Ordinal)
                .OrderBy(name => name == OtherLabel ? 1 : 0)
                .ThenBy(name => name, StringComparer.CurrentCulture)
                .Select(name => new TreatmentIncomeExportRow(
                    name,
                    counts.GetValueOrDefault(name),
                    incomeTotals.GetValueOrDefault(name)))
                .ToList();
        }

        private async Task RefreshVisitsAsync()
        {
            _pieMetric = PieMetric.Count;
            IncomeChartPanel.Visibility = Visibility.Collapsed;

            List<Visit> visits;
            using (var ctx = new AppDbContext())
            {
                visits = await ApplyDateFilter(ctx.Visits.AsNoTracking())
                    .ToListAsync();
            }

            int newVisitCount = visits.Count(visit => ParseTreatments(visit.SelectedTreatmentsJson).Count > 0);
            int reviewCount = visits.Count - newVisitCount;

            DrawPie(new List<PieSlice>
            {
                new()
                {
                    Label = "معاينة جديدة",
                    Amount = newVisitCount,
                    Color = SliceColors[0]
                },
                new()
                {
                    Label = "مراجعة",
                    Amount = reviewCount,
                    Color = SliceColors[1]
                }
            });
        }

        private async Task RefreshTreatmentsAsync()
        {
            _pieMetric = PieMetric.Count;
            IncomeChartPanel.Visibility = Visibility.Collapsed;

            List<string> knownTreatments;
            List<Visit> visits;
            using (var ctx = new AppDbContext())
            {
                knownTreatments = (await ctx.TreatmentCosts
                    .AsNoTracking()
                    .Select(t => t.TreatmentName)
                    .ToListAsync())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name)
                    .ToList();

                visits = await ApplyDateFilter(ctx.Visits.AsNoTracking())
                    .ToListAsync();
            }

            var totals = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var visit in visits)
            {
                foreach (var treatment in ParseTreatments(visit.SelectedTreatmentsJson))
                {
                    string name = string.IsNullOrWhiteSpace(treatment.TreatmentName)
                        ? OtherLabel
                        : treatment.TreatmentName;
                    AddTo(totals, name, Math.Max(1, treatment.Quantity));
                }
            }

            DrawPie(BuildCountSlices(knownTreatments, totals));
        }

        private bool IsInSelectedPeriod(Visit visit)
        {
            return _subTab switch
            {
                SubTab.Daily => visit.VisitDate.Date == _selectedDay.Date,
                SubTab.Monthly => visit.VisitDate.Year == _selectedYear
                                  && visit.VisitDate.Month == _selectedMonth,
                SubTab.Yearly => visit.VisitDate.Year == _selectedYear,
                _ => false
            };
        }

        private static List<(string Name, double TotalCostSyp)> BuildTreatmentQueue(
            IEnumerable<Visit> patientVisits)
        {
            var queue = new List<(string, double)>();

            foreach (var visit in patientVisits.OrderBy(v => v.VisitDate).ThenBy(v => v.Id))
            {
                foreach (var treatment in ParseTreatments(visit.SelectedTreatmentsJson))
                {
                    double costSyp = (double)treatment.Cost;
                    if (treatment.Currency == "USD")
                        costSyp *= visit.UsdToSypRateSnapshot;
                    costSyp *= Math.Max(1, treatment.Quantity);

                    if (costSyp > 0.009)
                        queue.Add((treatment.TreatmentName, costSyp));
                }
            }

            return queue;
        }

        private static IReadOnlyList<(string Name, double Amount)> AllocatePayment(
            List<(string Name, double TotalCostSyp)> queue,
            double cumulativePaidBefore,
            double thisPaid)
        {
            if (thisPaid <= 0.009 || queue.Count == 0)
                return Array.Empty<(string, double)>();

            double cumulativePaidAfter = cumulativePaidBefore + thisPaid;
            double runningCost = 0;
            var result = new List<(string, double)>();

            foreach (var (name, totalCost) in queue)
            {
                double treatmentStart = runningCost;
                double treatmentEnd = runningCost + totalCost;
                runningCost = treatmentEnd;

                if (treatmentEnd <= cumulativePaidBefore) continue;
                if (treatmentStart >= cumulativePaidAfter) break;

                double allocated = Math.Min(treatmentEnd, cumulativePaidAfter)
                                 - Math.Max(treatmentStart, cumulativePaidBefore);
                if (allocated > 0.009)
                    result.Add((name, allocated));
            }

            return result;
        }

        // ── Expenses ──────────────────────────────────────────────────────────

        private async Task RefreshExpensesAsync()
        {
            _pieMetric = PieMetric.Money;
            IncomeChartPanel.Visibility = Visibility.Collapsed;
            List<ExpenseEntry> expenses;
            using (var ctx = new AppDbContext())
            {
                var query = ctx.Expenses.AsNoTracking().AsQueryable();
                query = ApplyDateFilterExpenses(query);
                expenses = await query.ToListAsync();
            }

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
                totals.TryGetValue(label, out double amount);
                slices.Add(new PieSlice
                {
                    Label  = label,
                    Amount = amount,
                    Color  = SliceColors[colorIdx % SliceColors.Length]
                });
                colorIdx++;
            }

            DrawPie(slices);
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
                TblTotal.Text        = slices.Count > 0 ? $"الإجمالي: {FormatPieValue(0)}" : "";
                LegendPanel.Children.Clear();
                if (slices.Count > 0) BuildLegend(slices, total);
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
                        Fill = brush,
                        Tag = slice.Label,
                        Cursor = Cursors.Hand,
                        ToolTip = $"{slice.Label}: {FormatPieValue(slice.Amount)}"
                    };
                    outer.MouseLeftButtonUp += ExpenseLegendRow_Click;
                    Canvas.SetLeft(outer, cx - r);
                    Canvas.SetTop(outer,  cy - r);
                    PieCanvas.Children.Add(outer);
                }
                else
                {
                    var path = CreateDonutSlice(cx, cy, r, innerR, startAngle, sweep, brush);
                    path.Tag = slice.Label;
                    path.Cursor = Cursors.Hand;
                    path.ToolTip = $"{slice.Label}: {FormatPieValue(slice.Amount)}";
                    path.MouseLeftButtonUp += ExpenseLegendRow_Click;
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
            TblTotal.Text = $"الإجمالي: {FormatPieValue(total)}";

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

                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.Tag = slice.Label;
                row.Cursor = Cursors.Hand;
                row.MouseLeftButtonUp += ExpenseLegendRow_Click;
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
                    Text              = FormatPieValue(slice.Amount),
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

        private async void ExpenseLegendRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (_mainTab != MainTab.Expenses || sender is not FrameworkElement { Tag: string category }) return;

            try
            {
                List<ExpenseDetailRow> rows;
                using (var ctx = new AppDbContext())
                {
                    List<ExpenseEntry> filteredExpenses = await ApplyDateFilterExpenses(ctx.Expenses.AsNoTracking().AsQueryable())
                        .OrderBy(expense => expense.ExpenseDate)
                        .ToListAsync();
                    rows = filteredExpenses
                        .Where(expense => ClassifyExpense(expense.Description) == category)
                        .Select(expense => new ExpenseDetailRow
                        {
                            Date = expense.ExpenseDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                            Time = expense.ExpenseDate.ToString("hh:mm tt", CultureInfo.InvariantCulture),
                            Description = expense.Description,
                            Amount = $"{expense.Amount:N0} ل.س"
                        })
                        .ToList();
                }

                Window window = new()
                {
                    Title = $"تفاصيل فئة {category} - {CurrentPeriodLabel()}",
                    Width = 780,
                    Height = 520,
                    MinWidth = 600,
                    MinHeight = 360,
                    Owner = Window.GetWindow(this),
                    FlowDirection = FlowDirection.RightToLeft,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = (Brush)FindResource("AppWindowBg")
                };

                if (rows.Count == 0)
                {
                    window.Content = new TextBlock
                    {
                        Text = "لا توجد دفعات في هذه الفئة خلال الفترة المحددة.",
                        FontSize = 18,
                        Foreground = (Brush)FindResource("AppTextSecondary"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
                else
                {
                    Brush cardBackground = (Brush)FindResource("AppCardBg");
                    Brush primaryText = (Brush)FindResource("AppTextPrimary");
                    Brush border = (Brush)FindResource("AppBorder");
                    ListView list = new() { ItemsSource = rows, Margin = new Thickness(16), Background = cardBackground, Foreground = primaryText };
                    Style itemStyle = new(typeof(ListViewItem));
                    itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, primaryText));
                    itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, cardBackground));
                    list.ItemContainerStyle = itemStyle;
                    Style headerStyle = new(typeof(GridViewColumnHeader));
                    headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, primaryText));
                    headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, cardBackground));
                    headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, border));
                    list.Resources[typeof(GridViewColumnHeader)] = headerStyle;
                    GridView view = new();
                    view.Columns.Add(new GridViewColumn { Header = "التاريخ", DisplayMemberBinding = new Binding(nameof(ExpenseDetailRow.Date)) });
                    view.Columns.Add(new GridViewColumn { Header = "الوقت", DisplayMemberBinding = new Binding(nameof(ExpenseDetailRow.Time)) });
                    view.Columns.Add(new GridViewColumn { Header = "التفاصيل", DisplayMemberBinding = new Binding(nameof(ExpenseDetailRow.Description)) });
                    view.Columns.Add(new GridViewColumn { Header = "المبلغ", DisplayMemberBinding = new Binding(nameof(ExpenseDetailRow.Amount)) });
                    list.View = view;
                    window.Content = list;
                }
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر تحميل تفاصيل الفئة.\n{ex.Message}", "المصاريف", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DrawIncomeChart(List<Visit> visits)
        {
            IncomeChartCanvas.Children.Clear();
            IncomeChartPanel.Visibility = Visibility.Visible;
            IncomeChartTitle.Text = _incomeChartType == IncomeChartType.Area
                ? "الدخل حسب الفترة"
                : "الدخل حسب الفترة - أعمدة";
            DateTime start = SelectedPeriodStart();
            DateTime end = SelectedPeriodEnd();
            List<(string Label, double Amount)> points = BuildIncomeChartPoints(visits, start, end);

            const double chartWidth = 1100, chartHeight = 360, left = 64, top = 24, right = 16, bottom = 54;
            double plotWidth = chartWidth - left - right;
            double plotHeight = chartHeight - top - bottom;
            double max = Math.Max(1, points.Max(p => p.Amount));
            double slotWidth = plotWidth / points.Count;
            var yAxis = new Rectangle { Width = 1, Height = plotHeight, Fill = Brushes.Gray };
            Canvas.SetLeft(yAxis, left);
            Canvas.SetTop(yAxis, top);
            IncomeChartCanvas.Children.Add(yAxis);

            var xAxis = new Rectangle { Width = plotWidth, Height = 1, Fill = Brushes.Gray };
            Canvas.SetLeft(xAxis, left);
            Canvas.SetTop(xAxis, top + plotHeight);
            IncomeChartCanvas.Children.Add(xAxis);
            AddChartLabel($"{max:N0}", 0, top - 5, 42, 18);
            AddChartLabel("0", 0, top + plotHeight - 8, 42, 18);

            if (_incomeChartType == IncomeChartType.Area)
            {
                DrawIncomeArea(points, max, left, top, plotWidth, plotHeight, slotWidth);
            }
            else
            {
                DrawIncomeColumns(points, max, left, top, plotHeight, slotWidth);
            }
        }

        private List<(string Label, double Amount)> BuildIncomeChartPoints(List<Visit> visits, DateTime start, DateTime end)
        {
            if (_subTab == SubTab.Daily)
                return new List<(string, double)> { (_selectedDay.ToString("dd/MM", CultureInfo.InvariantCulture), visits.Sum(v => v.TodayPaid)) };

            if (_subTab == SubTab.Monthly)
            {
                return Enumerable.Range(0, (end - start).Days + 1)
                    .Select(i => start.AddDays(i))
                    .Select(day => (day.ToString("dd", CultureInfo.InvariantCulture), visits.Where(v => v.VisitDate.Date == day.Date).Sum(v => v.TodayPaid)))
                    .ToList();
            }

            return Enumerable.Range(1, 12)
                .Select(month => ($"{month:00}", visits.Where(v => v.VisitDate.Month == month).Sum(v => v.TodayPaid)))
                .ToList();
        }

        private void DrawIncomeColumns(List<(string Label, double Amount)> points, double max, double left, double top, double plotHeight, double slotWidth)
        {
            for (int i = 0; i < points.Count; i++)
            {
                double barHeight = points[i].Amount / max * plotHeight;
                double barWidth = Math.Max(4, slotWidth * 0.62);
                double x = left + i * slotWidth + (slotWidth - barWidth) / 2;
                double y = top + plotHeight - barHeight;
                Rectangle bar = new() { Width = barWidth, Height = Math.Max(1, barHeight), Fill = HexBrush("#3B82F6"), ToolTip = $"{points[i].Label}: {points[i].Amount:N0} ل.س" };
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, y);
                IncomeChartCanvas.Children.Add(bar);
                AddChartLabel(points[i].Label, x - 8, top + plotHeight + 8, barWidth + 16, 18);
                AddChartLabel(points[i].Amount.ToString("N0", CultureInfo.CurrentCulture), x - 12, Math.Max(0, y - 20), barWidth + 24, 18);
            }
        }

        private void DrawIncomeArea(List<(string Label, double Amount)> points, double max, double left, double top, double plotWidth, double plotHeight, double slotWidth)
        {
            List<Point> coordinates = points
                .Select((point, index) => new Point(
                    left + (points.Count == 1 ? plotWidth / 2 : index * slotWidth + slotWidth / 2),
                    top + plotHeight - point.Amount / max * plotHeight))
                .ToList();
            PathGeometry geometry = new();
            PathFigure figure = new() { StartPoint = coordinates[0], IsClosed = false };
            for (int i = 1; i < coordinates.Count; i++)
            {
                Point previous = coordinates[i - 1];
                Point current = coordinates[i];
                double midpoint = (current.X - previous.X) / 2;
                figure.Segments.Add(new BezierSegment(
                    new Point(previous.X + midpoint, previous.Y),
                    new Point(current.X - midpoint, current.Y),
                    current,
                    true));
            }
            geometry.Figures.Add(figure);
            IncomeChartCanvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = geometry,
                Stroke = HexBrush("#2196F3"),
                StrokeThickness = 3,
                StrokeLineJoin = PenLineJoin.Round,
                ToolTip = "الدخل"
            });

            for (int i = 0; i < points.Count; i++)
            {
                Ellipse marker = new()
                {
                    Width = 12,
                    Height = 12,
                    Fill = Brushes.White,
                    Stroke = HexBrush("#2196F3"),
                    StrokeThickness = 3,
                    ToolTip = $"{points[i].Label}: {points[i].Amount:N0} ل.س"
                };
                Canvas.SetLeft(marker, coordinates[i].X - marker.Width / 2);
                Canvas.SetTop(marker, coordinates[i].Y - marker.Height / 2);
                IncomeChartCanvas.Children.Add(marker);
                AddChartLabel(points[i].Label, coordinates[i].X - 20, top + plotHeight + 8, 40, 18);
                AddChartLabel(points[i].Amount.ToString("N0", CultureInfo.CurrentCulture), coordinates[i].X - 30, Math.Max(0, coordinates[i].Y - 24), 60, 18);
            }
        }

        private void AddChartLabel(string text, double x, double y, double width, double height)
        {
            TextBlock label = new() { Text = text, Width = width, Height = height, FontSize = 10, TextAlignment = TextAlignment.Center, Foreground = (Brush)FindResource("AppTextSecondary") };
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, y);
            IncomeChartCanvas.Children.Add(label);
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

        private DateTime SelectedPeriodStart() => _subTab switch
        {
            SubTab.Daily => _selectedDay.Date,
            SubTab.Monthly => new DateTime(_selectedYear, _selectedMonth, 1),
            SubTab.Yearly => new DateTime(_selectedYear, 1, 1),
            _ => _selectedDay.Date
        };

        private DateTime SelectedPeriodEnd() => _subTab switch
        {
            SubTab.Daily => _selectedDay.Date,
            SubTab.Monthly => new DateTime(_selectedYear, _selectedMonth, 1).AddMonths(1).AddDays(-1),
            SubTab.Yearly => new DateTime(_selectedYear, 12, 31),
            _ => _selectedDay.Date
        };

        private string PeriodFileLabel() => _subTab switch
        {
            SubTab.Daily => _selectedDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            SubTab.Monthly => $"{_selectedYear}-{_selectedMonth:00}",
            SubTab.Yearly => _selectedYear.ToString(CultureInfo.InvariantCulture),
            _ => "الفترة"
        };

        private List<byte[]> BuildExpensePdfPages(List<ExpenseEntry> expenses)
        {
            List<string> categories = ExpenseKeywords.Select(k => k.Label).Append(OtherLabel).ToList();
            List<ExpensePdfPage> pageData = new();
            bool showGlobalHeader = true;
            for (int categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
            {
                string category = categories[categoryIndex];
                List<ExpenseEntry> rows = expenses.Where(e => ClassifyExpense(e.Description) == category).ToList();
                double categoryTotal = rows.Sum(e => e.Amount);
                if (rows.Count == 0)
                    pageData.Add(new ExpensePdfPage(category, rows, categoryIndex, categoryTotal, showGlobalHeader));
                else
                    for (int offset = 0; offset < rows.Count; offset += 16)
                    {
                        pageData.Add(new ExpensePdfPage(category, rows.Skip(offset).Take(16).ToList(), categoryIndex, categoryTotal, showGlobalHeader));
                        showGlobalHeader = false;
                    }
                showGlobalHeader = false;
            }

            List<byte[]> pages = new();
            for (int i = 0; i < pageData.Count; i++)
                pages.Add(RenderExpensePdfPage(pageData[i], i + 1, pageData.Count, expenses.Sum(e => e.Amount)));
            return pages;
        }

        private byte[] RenderExpensePdfPage(ExpensePdfPage page, int pageNumber, int pageCount, double grandTotal)
        {
            const int width = 1200;
            const int height = 850;
            DrawingVisual visual = new();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                double y = 48;
                if (page.ShowGlobalHeader)
                {
                    DrawExportText(dc, "تفاصيل المصاريف", 60, y, 30, Brushes.Black, true);
                    DrawExportText(dc, $"الفترة: {CurrentPeriodLabel()}", 60, y + 44, 18, Brushes.DimGray, false);
                    DrawExportText(dc, $"إجمالي المصاريف: {grandTotal:N0} ل.س", 60, y + 77, 18, Brushes.DarkSlateBlue, true);
                    y += 145;
                }

                Brush categoryBrush = HexBrush(SliceColors[page.CategoryIndex % SliceColors.Length]);
                DrawExportText(dc, $"فئة: {page.Category}", 60, y, 23, categoryBrush, true);
                DrawExportText(dc, $"مجموع الفئة: {page.CategoryTotal:N0} ل.س", 60, y + 36, 18, categoryBrush, true);
                DrawExportText(dc, "التاريخ    -    التفاصيل    -    المبلغ", 60, y + 78, 18, Brushes.Black, true);

                if (page.Rows.Count == 0)
                    DrawExportText(dc, "لا توجد مصاريف في هذه الفئة", 60, y + 123, 18, Brushes.Gray, false);
                else
                {
                    for (int i = 0; i < page.Rows.Count; i++)
                    {
                        ExpenseEntry expense = page.Rows[i];
                        string line = $"{expense.ExpenseDate:dd/MM/yyyy}    -    {TruncateExport(expense.Description, 72)}    -    {expense.Amount:N0} ل.س";
                        DrawExportText(dc, line, 60, y + 123 + i * 28, 16, Brushes.Black, false);
                    }
                }

                DrawExportText(dc, $"صفحة {pageNumber} من {pageCount}", 60, 795, 14, Brushes.Gray, false);
            }

            RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            JpegBitmapEncoder encoder = new() { QualityLevel = 92 };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using MemoryStream stream = new();
            encoder.Save(stream);
            return stream.ToArray();
        }

        private static void DrawExportText(DrawingContext dc, string text, double x, double y, double size, Brush brush, bool bold)
        {
            FormattedText formatted = new(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.RightToLeft,
                new Typeface(new FontFamily("Arial"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
                size,
                brush,
                1.0);
            // With right-to-left text, WPF treats the origin as the right edge.
            // Keep that edge inside the A4 raster page so the text is not clipped.
            dc.DrawText(formatted, new Point(1140, y));
        }

        private byte[] BuildIncomeWorkbook(List<(DateTime Day, double Amount)> dailyIncome)
        {
            const string mainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            int lastDataRow = 4 + dailyIncome.Count;
            StringBuilder sheet = new();
            sheet.Append($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"{mainNs}\"><sheetViews><sheetView workbookViewId=\"0\" rightToLeft=\"1\"/></sheetViews><sheetData>");
            sheet.Append($"<row r=\"1\">{InlineCell("A1", "إجمالي الدخل اليومي")}</row>");
            sheet.Append($"<row r=\"2\">{InlineCell("A2", $"الفترة: {CurrentPeriodLabel()}")}</row>");
            sheet.Append($"<row r=\"3\"><c r=\"A3\" s=\"2\" t=\"inlineStr\"><is><t>الإجمالي</t></is></c><c r=\"C3\" s=\"3\"><f>SUM(C5:C{lastDataRow})</f></c></row>");
            sheet.Append($"<row r=\"4\">{InlineCell("A4", "اليوم", 2)}{InlineCell("B4", "التاريخ", 2)}{InlineCell("C4", "الدخل (ل.س)", 2)}</row>");
            for (int i = 0; i < dailyIncome.Count; i++)
            {
                int row = i + 5;
                double serial = (dailyIncome[i].Day.Date - new DateTime(1899, 12, 30)).TotalDays;
                sheet.Append($"<row r=\"{row}\">{InlineCell($"A{row}", dailyIncome[i].Day.ToString("dddd", CultureInfo.CurrentCulture))}<c r=\"B{row}\" s=\"4\"><v>{serial:0}</v></c><c r=\"C{row}\" s=\"3\"><v>{dailyIncome[i].Amount.ToString(CultureInfo.InvariantCulture)}</v></c></row>");
            }
            sheet.Append("</sheetData><cols><col min=\"1\" max=\"1\" width=\"20\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"15\" customWidth=\"1\"/><col min=\"3\" max=\"3\" width=\"20\" customWidth=\"1\"/></cols><autoFilter ref=\"A4:C" + lastDataRow + "\"/></worksheet>");

            using MemoryStream output = new();
            using (ZipArchive zip = new(output, ZipArchiveMode.Create, true))
            {
                AddZipEntry(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
                AddZipEntry(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                AddZipEntry(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><calcPr fullCalcOnLoad=\"1\"/><sheets><sheet name=\"الدخل اليومي\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                AddZipEntry(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
                AddZipEntry(zip, "xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Arial\"/></font><font><b/><sz val=\"12\"/><name val=\"Arial\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFDDEBF7\"/><bgColor indexed=\"64\"/></patternFill></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellXfs count=\"5\"><xf/><xf fontId=\"1\" fillId=\"1\"/><xf fontId=\"1\"/><xf numFmtId=\"4\"/><xf numFmtId=\"14\"/></cellXfs></styleSheet>");
                AddZipEntry(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
            }
            return output.ToArray();
        }

        private List<IncomeExportSummary> BuildIncomeSummary(
            DateTime start,
            DateTime end,
            IEnumerable<(DateTime Date, double Amount)> incomes,
            IEnumerable<(DateTime Date, double Amount)> expenses)
        {
            List<IncomeExportSummary> result = new();
            if (_subTab == SubTab.Yearly)
            {
                for (int month = 1; month <= 12; month++)
                {
                    result.Add(new IncomeExportSummary(
                        $"{ArabicMonth(month)} {_selectedYear}",
                        incomes.Where(x => x.Date.Year == _selectedYear && x.Date.Month == month).Sum(x => x.Amount),
                        expenses.Where(x => x.Date.Year == _selectedYear && x.Date.Month == month).Sum(x => x.Amount)));
                }
            }
            else
            {
                for (DateTime day = start; day <= end; day = day.AddDays(1))
                {
                    result.Add(new IncomeExportSummary(
                        day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                        incomes.Where(x => x.Date.Date == day.Date).Sum(x => x.Amount),
                        expenses.Where(x => x.Date.Date == day.Date).Sum(x => x.Amount)));
                }
            }
            return result;
        }

        private byte[] BuildIncomeWorkbook(
            List<IncomeExportSummary> summary,
            List<IncomeExportDetail> details,
            List<TreatmentIncomeExportRow> treatmentSummary)
        {
            string summarySheet = BuildSummarySheet(summary);
            string detailSheet = BuildDetailSheet(details);
            string treatmentSheet = BuildTreatmentSummarySheet(treatmentSummary);
            using MemoryStream output = new();
            using (ZipArchive zip = new(output, ZipArchiveMode.Create, true))
            {
                AddZipEntry(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet3.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
                AddZipEntry(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                AddZipEntry(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"الملخص\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"التفاصيل\" sheetId=\"2\" r:id=\"rId2\"/><sheet name=\"العلاجات\" sheetId=\"3\" r:id=\"rId3\"/></sheets></workbook>");
                AddZipEntry(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet3.xml\"/><Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
                AddZipEntry(zip, "xl/styles.xml", ExcelStylesXml());
                AddZipEntry(zip, "xl/worksheets/sheet1.xml", summarySheet);
                AddZipEntry(zip, "xl/worksheets/sheet2.xml", detailSheet);
                AddZipEntry(zip, "xl/worksheets/sheet3.xml", treatmentSheet);
            }
            return output.ToArray();
        }

        private string BuildTreatmentSummarySheet(List<TreatmentIncomeExportRow> rows)
        {
            int totalRow = rows.Count + 4;
            StringBuilder sheet = new("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheetViews><sheetView workbookViewId=\"0\" rightToLeft=\"1\"/></sheetViews><cols><col min=\"1\" max=\"1\" width=\"36\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"16\" customWidth=\"1\"/><col min=\"3\" max=\"3\" width=\"20\" customWidth=\"1\"/></cols><sheetData>");
            sheet.Append($"<row r=\"1\">{InlineCell("A1", "ملخص العلاجات", 1)}</row><row r=\"2\">{InlineCell("A2", $"الفترة: {CurrentPeriodLabel()}")}</row>");
            sheet.Append($"<row r=\"3\">{InlineCell("A3", "اسم العلاج", 2)}{InlineCell("B3", "العدد", 2)}{InlineCell("C3", "الدخل (ل.س)", 2)}</row>");
            for (int i = 0; i < rows.Count; i++)
            {
                int row = i + 4;
                sheet.Append($"<row r=\"{row}\">{InlineCell($"A{row}", rows[i].Name)}{NumberCell($"B{row}", rows[i].Count, 3)}{NumberCell($"C{row}", rows[i].Income, 5)}</row>");
            }
            sheet.Append($"<row r=\"{totalRow}\">{InlineCell($"A{totalRow}", "الإجمالي", 3)}{NumberCell($"B{totalRow}", rows.Sum(r => r.Count), 3)}{NumberCell($"C{totalRow}", rows.Sum(r => r.Income), 5)}</row>");
            sheet.Append($"</sheetData><autoFilter ref=\"A3:C{totalRow - 1}\"/><mergeCells count=\"1\"><mergeCell ref=\"A1:C1\"/></mergeCells></worksheet>");
            return sheet.ToString();
        }

        private byte[] BuildTreatmentWorkbook(List<TreatmentIncomeExportRow> rows)
        {
            using MemoryStream output = new();
            using (ZipArchive zip = new(output, ZipArchiveMode.Create, true))
            {
                AddZipEntry(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
                AddZipEntry(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                AddZipEntry(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"العلاجات\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                AddZipEntry(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
                AddZipEntry(zip, "xl/styles.xml", ExcelStylesXml());
                AddZipEntry(zip, "xl/worksheets/sheet1.xml", BuildTreatmentSummarySheet(rows));
            }
            return output.ToArray();
        }

        private string BuildSummarySheet(List<IncomeExportSummary> rows)
        {
            int totalRow = rows.Count + 4;
            StringBuilder sheet = new("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><cols><col min=\"1\" max=\"1\" width=\"24\" customWidth=\"1\"/><col min=\"2\" max=\"4\" width=\"18\" customWidth=\"1\"/></cols><sheetData>");
            sheet.Append($"<row r=\"1\">{InlineCell("A1", "ملخص الدخل والمصاريف", 1)}</row><row r=\"2\">{InlineCell("A2", $"الفترة: {CurrentPeriodLabel()}")}</row>");
            sheet.Append($"<row r=\"3\">{InlineCell("A3", "الفترة", 2)}{InlineCell("B3", "الدخل", 2)}{InlineCell("C3", "المصاريف", 2)}{InlineCell("D3", "الصافي", 2)}</row>");
            for (int i = 0; i < rows.Count; i++)
            {
                int row = i + 4;
                sheet.Append($"<row r=\"{row}\">{InlineCell($"A{row}", rows[i].Period)}{NumberCell($"B{row}", rows[i].Income, 5)}{NumberCell($"C{row}", rows[i].Expenses, 5)}{NumberCell($"D{row}", rows[i].Income - rows[i].Expenses, 5)}</row>");
            }
            sheet.Append($"<row r=\"{totalRow}\">{InlineCell($"A{totalRow}", "الإجمالي", 3)}{NumberCell($"B{totalRow}", rows.Sum(r => r.Income), 5)}{NumberCell($"C{totalRow}", rows.Sum(r => r.Expenses), 5)}{NumberCell($"D{totalRow}", rows.Sum(r => r.Income - r.Expenses), 5)}</row>");
            sheet.Append($"</sheetData><mergeCells count=\"1\"><mergeCell ref=\"A1:D1\"/></mergeCells></worksheet>");
            return sheet.ToString();
        }

        private string BuildDetailSheet(List<IncomeExportDetail> rows)
        {
            int totalRow = rows.Count + 4;
            StringBuilder sheet = new("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><cols><col min=\"1\" max=\"1\" width=\"16\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"14\" customWidth=\"1\"/><col min=\"3\" max=\"3\" width=\"38\" customWidth=\"1\"/><col min=\"4\" max=\"4\" width=\"18\" customWidth=\"1\"/><col min=\"5\" max=\"5\" width=\"18\" customWidth=\"1\"/></cols><sheetData>");
            sheet.Append($"<row r=\"1\">{InlineCell("A1", "تفاصيل الدخل والمصاريف", 1)}</row><row r=\"2\">{InlineCell("A2", $"الفترة: {CurrentPeriodLabel()}")}</row>");
            sheet.Append($"<row r=\"3\">{InlineCell("A3", "التاريخ", 2)}{InlineCell("B3", "النوع", 2)}{InlineCell("C3", "الوصف", 2)}{InlineCell("D3", "المبلغ", 2)}{InlineCell("E3", "التصنيف", 2)}</row>");
            for (int i = 0; i < rows.Count; i++)
            {
                int row = i + 4;
                double serial = (rows[i].Date - new DateTime(1899, 12, 30)).TotalDays;
                sheet.Append($"<row r=\"{row}\">{NumberCell($"A{row}", serial, 4)}{InlineCell($"B{row}", rows[i].Type)}{InlineCell($"C{row}", rows[i].Description)}{NumberCell($"D{row}", rows[i].Amount, 5)}{InlineCell($"E{row}", rows[i].Category)}</row>");
            }
            sheet.Append($"<row r=\"{totalRow}\">{InlineCell($"C{totalRow}", "الإجمالي", 3)}{NumberCell($"D{totalRow}", rows.Sum(r => r.Amount), 5)}</row>");
            sheet.Append($"</sheetData><mergeCells count=\"1\"><mergeCell ref=\"A1:E1\"/></mergeCells></worksheet>");
            return sheet.ToString();
        }

        private static string NumberCell(string reference, double value, int style) =>
            $"<c r=\"{reference}\" s=\"{style}\"><v>{value.ToString(CultureInfo.InvariantCulture)}</v></c>";

        private static string ExcelStylesXml() => "<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Arial\"/></font><font><b/><sz val=\"14\"/><name val=\"Arial\"/></font></fonts><fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD9EAF7\"/></patternFill></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellXfs count=\"6\"><xf/><xf fontId=\"1\"/><xf fillId=\"2\" fontId=\"1\"/><xf fillId=\"2\" fontId=\"0\"/><xf numFmtId=\"14\"/><xf numFmtId=\"4\"/></cellXfs></styleSheet>";

        private static string InlineCell(string reference, string value, int style = 0) =>
            $"<c r=\"{reference}\"{(style > 0 ? $" s=\"{style}\"" : "")} t=\"inlineStr\"><is><t xml:space=\"preserve\">{XmlEscape(value)}</t></is></c>";

        private static string XmlEscape(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;

        private static void AddZipEntry(ZipArchive zip, string name, string content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Fastest);
            using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        private static byte[] BuildImagePdf(List<byte[]> images)
        {
            using MemoryStream pdf = new();
            WriteAscii(pdf, "%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
            List<long> offsets = new() { 0 };
            int pageCount = images.Count;
            int catalog = 1, pages = 2;
            List<int> pageObjects = Enumerable.Range(0, pageCount).Select(i => 3 + i * 3).ToList();
            List<string> objects = new() { "", $"<< /Type /Catalog /Pages {pages} 0 R >>", $"<< /Type /Pages /Kids [{string.Join(" ", pageObjects.Select(n => $"{n} 0 R"))}] /Count {pageCount} >>" };
            foreach (int i in Enumerable.Range(0, pageCount))
            {
                int page = pageObjects[i], content = page + 1, image = page + 2;
                objects.Add($"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 842 595] /Resources << /XObject << /Im0 {image} 0 R >> >> /Contents {content} 0 R >>");
                string stream = "q\n842 0 0 595 0 0 cm\n/Im0 Do\nQ\n";
                objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}endstream");
                objects.Add(string.Empty);
            }
            for (int i = 1; i < objects.Count; i++)
            {
                offsets.Add(pdf.Position);
                WriteAscii(pdf, $"{i} 0 obj\n");
                if (objects[i].Length > 0) WriteAscii(pdf, objects[i] + "\nendobj\n");
                else
                {
                    byte[] image = images[(i - 5) / 3];
                    WriteAscii(pdf, $"<< /Type /XObject /Subtype /Image /Width 1200 /Height 850 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {image.Length} >>\nstream\n");
                    pdf.Write(image, 0, image.Length);
                    WriteAscii(pdf, "\nendstream\nendobj\n");
                }
            }
            long xref = pdf.Position;
            WriteAscii(pdf, $"xref\n0 {objects.Count}\n0000000000 65535 f \n");
            for (int i = 1; i < objects.Count; i++) WriteAscii(pdf, $"{offsets[i]:D10} 00000 n \n");
            WriteAscii(pdf, $"trailer\n<< /Size {objects.Count} /Root {catalog} 0 R >>\nstartxref\n{xref}\n%%EOF");
            return pdf.ToArray();
        }

        private static void WriteAscii(Stream stream, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private string CurrentPeriodLabel() => _subTab switch
        {
            SubTab.Daily => _selectedDay.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture),
            SubTab.Monthly => $"{ArabicMonth(_selectedMonth)} {_selectedYear}",
            SubTab.Yearly => _selectedYear.ToString(CultureInfo.CurrentCulture),
            _ => string.Empty
        };

        private static string TruncateExport(string value, int max) =>
            string.IsNullOrWhiteSpace(value) ? "بدون تفاصيل" : value.Length <= max ? value : value[..max] + "...";

        private sealed record ExpensePdfPage(string Category, List<ExpenseEntry> Rows, int CategoryIndex, double CategoryTotal, bool ShowGlobalHeader);
        private sealed record IncomeExportSummary(string Period, double Income, double Expenses);
        private sealed record IncomeExportDetail(DateTime Date, string Type, string Description, double Amount, string Category);
        private sealed record TreatmentIncomeExportRow(string Name, int Count, double Income);

        private sealed class ExpenseDetailRow
        {
            public string Date { get; init; } = string.Empty;
            public string Time { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public string Amount { get; init; } = string.Empty;
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
                totals.TryGetValue(name, out double amt);
                slices.Add(new PieSlice
                {
                    Label  = name,
                    Amount = amt,
                    Color  = SliceColors[colorIdx % SliceColors.Length]
                });
                colorIdx++;
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

        private static List<PieSlice> BuildCountSlices(
            List<string> knownTreatments,
            Dictionary<string, double> totals)
        {
            var slices = new List<PieSlice>();
            var used = new HashSet<string>(StringComparer.Ordinal);
            int colorIdx = 0;

            foreach (var name in knownTreatments)
            {
                totals.TryGetValue(name, out double count);
                slices.Add(new PieSlice
                {
                    Label = name,
                    Amount = count,
                    Color = SliceColors[colorIdx % SliceColors.Length]
                });
                used.Add(name);
                colorIdx++;
            }

            foreach (var kv in totals)
            {
                if (used.Contains(kv.Key) || kv.Value <= 0)
                    continue;

                slices.Add(new PieSlice
                {
                    Label = kv.Key,
                    Amount = kv.Value,
                    Color = SliceColors[colorIdx % SliceColors.Length]
                });
                colorIdx++;
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
            BtnTabVisits.Style = _mainTab == MainTab.Visits ? active : inactive;
            BtnTabTreatments.Style = _mainTab == MainTab.Treatments ? active : inactive;
            BtnIncomeArea.Visibility = _mainTab == MainTab.Income ? Visibility.Visible : Visibility.Collapsed;
            BtnIncomeColumns.Visibility = _mainTab == MainTab.Income ? Visibility.Visible : Visibility.Collapsed;
            BtnExportTreatmentsExcel.Visibility = _mainTab == MainTab.Treatments ? Visibility.Visible : Visibility.Collapsed;
            BtnExportExpensesPdf.Visibility = _mainTab == MainTab.Expenses ? Visibility.Visible : Visibility.Collapsed;
            BtnExportIncomeExcel.Visibility = _mainTab == MainTab.Income ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateIncomeChartStyles()
        {
            Brush active = HexBrush("#0F766E");
            Brush inactive = (Brush)FindResource("AppTextSecondary");
            BtnIncomeArea.Foreground = _incomeChartType == IncomeChartType.Area ? active : inactive;
            BtnIncomeArea.BorderBrush = _incomeChartType == IncomeChartType.Area ? active : (Brush)FindResource("AppBorder");
            BtnIncomeColumns.Foreground = _incomeChartType == IncomeChartType.Columns ? active : inactive;
            BtnIncomeColumns.BorderBrush = _incomeChartType == IncomeChartType.Columns ? active : (Brush)FindResource("AppBorder");
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

        private string FormatPieValue(double value) =>
            _pieMetric == PieMetric.Count
                ? $"{value:N0}"
                : $"{value:N0} ل.س";

        // ── Inner types ───────────────────────────────────────────────────────

        private class PieSlice
        {
            public string Label  { get; set; } = "";
            public double Amount { get; set; }
            public string Color  { get; set; } = "#3B82F6";
        }
    }
}

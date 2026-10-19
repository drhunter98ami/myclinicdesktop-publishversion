using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MyClinic.OriginalTheme
{
    // NOTE: the combined PDF report lives in StatisticsView.Report.cs (same partial class).
    public partial class StatisticsView : UserControl
    {
        // ── State ─────────────────────────────────────────────────────────────
        private enum MainTab { Income, Expenses, Visits, Treatments }
        private enum SubTab  { Daily, Monthly, Yearly }
        private enum PieMetric { Money, Count }
        private enum IncomeChartType { Area, Columns }

        private const string OtherLabel = ExpenseCategories.OtherLabel;

        private MainTab _mainTab   = MainTab.Income;
        private SubTab  _subTab    = SubTab.Monthly;
        private PieMetric _pieMetric = PieMetric.Money;
        private IncomeChartType _incomeChartType = IncomeChartType.Area;

        private DateTime _selectedDay   = AppClock.Today;
        private int      _selectedMonth = AppClock.Today.Month;
        private int      _selectedYear  = AppClock.Today.Year;

        // ── Pie colours ───────────────────────────────────────────────────────
        private static readonly string[] SliceColors =
        {
            "#3B82F6","#10B981","#F59E0B","#EF4444","#8B5CF6",
            "#06B6D4","#F97316","#EC4899","#84CC16","#6366F1",
            "#14B8A6","#FB923C","#A855F7","#22C55E","#E11D48"
        };

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
            _selectedDay   = AppClock.Today;
            _selectedMonth = AppClock.Today.Month;
            _selectedYear  = AppClock.Today.Year;
            await RefreshAsync();
        }

        public async Task RefreshForAppTimeChangeAsync(DateTime previousAppTime)
        {
            DateTime previousDate = previousAppTime.Date;
            DateTime currentDate = AppClock.Today;

            if (_selectedDay.Date == previousDate)
                _selectedDay = currentDate;

            if (_selectedMonth == previousDate.Month && _selectedYear == previousDate.Year)
            {
                _selectedMonth = currentDate.Month;
                _selectedYear = currentDate.Year;
            }
            else if (_selectedYear == previousDate.Year)
            {
                _selectedYear = currentDate.Year;
            }

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
            var fixedOrder = ExpenseCategories.Labels;

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
            return ExpenseCategories.Classify(description);
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

        private string CurrentPeriodLabel() => _subTab switch
        {
            SubTab.Daily => _selectedDay.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            SubTab.Monthly => $"{ArabicMonth(_selectedMonth)} {_selectedYear.ToString(CultureInfo.InvariantCulture)}",
            SubTab.Yearly => _selectedYear.ToString(CultureInfo.InvariantCulture),
            _ => string.Empty
        };

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
            // BtnExportPdf (combined report) is visible on every tab.
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

        // Hex colour string → SolidColorBrush (also accepts #AARRGGBB)
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
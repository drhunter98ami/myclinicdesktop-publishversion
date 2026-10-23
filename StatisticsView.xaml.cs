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

namespace MyClinic
{
    // NOTE: the combined PDF report lives in StatisticsView.Report.cs (same partial class).
    public partial class StatisticsView : UserControl
    {
        // ── State ─────────────────────────────────────────────────────────────
        private enum MainTab { Income, Expenses, Visits, Treatments }
        private enum SubTab  { Daily, Monthly, Yearly }
        private enum PieMetric { Money, Count }

        private const string OtherLabel = ExpenseCategories.OtherLabel;

        private MainTab _mainTab   = MainTab.Income;
        private SubTab  _subTab    = SubTab.Monthly;
        private PieMetric _pieMetric = PieMetric.Money;

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
            Unloaded += (_, _) => ++_refreshVersion;
            UpdateSubTabStyles();
            Loaded += StatisticsView_Loaded;
            Unloaded += StatisticsView_Unloaded;
        }

        private async void StatisticsView_Loaded(object sender, RoutedEventArgs e)
        {
            GlobalEvents.OnThemeChanged -= HandleThemeChanged;
            GlobalEvents.OnThemeChanged += HandleThemeChanged;
            ApplyThemeVisuals(IsDarkThemeActive());
            await RefreshAsync();
        }

        private void StatisticsView_Unloaded(object sender, RoutedEventArgs e)
        {
            GlobalEvents.OnThemeChanged -= HandleThemeChanged;
        }

        private bool IsDarkThemeActive()
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
                return mainWindow.IsDarkTheme;
            if (Application.Current?.MainWindow is MainWindow appMainWindow)
                return appMainWindow.IsDarkTheme;
            return false;
        }

        private async void HandleThemeChanged(bool isDark)
        {
            ApplyThemeVisuals(isDark);
            if (IsLoaded)
                await RefreshAsync();
        }

        private void ApplyThemeVisuals(bool isDark)
        {
            if (StatisticsSpotlightCanvas is not null)
                StatisticsSpotlightCanvas.Visibility = isDark ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Statistics_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!MotionPreferences.Enabled)
            {
                Statistics_MouseLeave(sender, e);
                return;
            }

            if (!IsDarkThemeActive() || StatisticsSpotlightCanvas is null || StatisticsSpotlightTransform is null)
                return;

            if (StatisticsSpotlightCanvas.Visibility != Visibility.Visible)
                StatisticsSpotlightCanvas.Visibility = Visibility.Visible;

            Point pos = e.GetPosition(StatisticsRootGrid);
            StatisticsSpotlightTransform.X = pos.X - (StatisticsSpotlightEllipse.Width / 2.0);
            StatisticsSpotlightTransform.Y = pos.Y - (StatisticsSpotlightEllipse.Height / 2.0);
        }

        private void Statistics_MouseLeave(object sender, MouseEventArgs e)
        {
            if (StatisticsSpotlightTransform is not null)
            {
                StatisticsSpotlightTransform.X = -1200;
                StatisticsSpotlightTransform.Y = -1200;
            }
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

        private int _refreshVersion;

        private async Task RefreshAsync()
        {
            int version = ++_refreshVersion;
            UpdateDateLabel();
            AnalyticsDashboard.SetLoading();
            CategoryBreakdown.Opacity = .5;
            CategoryBreakdown.IsEnabled = false;
            var period = _subTab switch { SubTab.Daily => StatisticsPeriod.Day, SubTab.Monthly => StatisticsPeriod.Month, _ => StatisticsPeriod.Year };
            DateTime start = SelectedPeriodStart(), end = SelectedPeriodEnd();
            DateTime previousStart = StatisticsAnalytics.PreviousPeriodStart(start, period);
            DateTime exclusiveEnd = end.AddDays(1);
            MainTab tab = _mainTab;
            try
            {
                using var ctx = new AppDbContext();
                var names = tab is MainTab.Income or MainTab.Treatments
                    ? (await ctx.TreatmentCosts.AsNoTracking().Select(t => t.TreatmentName).ToListAsync())
                        .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).OrderBy(n => n).ToList()
                    : new List<string>();
                if (version != _refreshVersion) return;
                List<Visit> visits = new();
                List<ExpenseEntry> expenses = new();
                if (tab == MainTab.Expenses)
                    expenses = await ctx.Expenses.AsNoTracking().Where(e => e.ExpenseDate >= previousStart && e.ExpenseDate < exclusiveEnd).ToListAsync();
                else
                {
                    // Income allocation requires complete treatment/payment histories.
                    var query = ctx.Visits.AsNoTracking();
                    if (tab != MainTab.Income) query = query.Where(v => v.VisitDate >= previousStart && v.VisitDate < exclusiveEnd);
                    visits = await query.ToListAsync();
                }
                if (version != _refreshVersion) return;
                DateTime today = AppClock.Today;
                var selected = visits.Where(v => IsInSelectedPeriod(v) && v.VisitDate.Date <= today).ToList();
                switch (tab)
                {
                    case MainTab.Income: ShowIncome(visits, selected, names); break;
                    case MainTab.Expenses: ShowExpenses(expenses.Where(e => e.ExpenseDate >= start && e.ExpenseDate < exclusiveEnd && e.ExpenseDate.Date <= today).ToList()); break;
                    case MainTab.Visits: ShowVisits(selected); break;
                    case MainTab.Treatments: ShowTreatments(selected, names); break;
                }
                IEnumerable<StatisticsObservation> observations = tab == MainTab.Expenses
                    ? expenses.Select(e => new StatisticsObservation(e.ExpenseDate, e.Amount))
                    : visits.Select(v => new StatisticsObservation(v.VisitDate, tab switch
                    {
                        MainTab.Income => v.TodayPaid,
                        MainTab.Visits => 1,
                        _ => ParseTreatments(v.SelectedTreatmentsJson).Sum(t => Math.Max(1, t.Quantity))
                    }));
                string title = tab switch { MainTab.Income => "المبالغ المحصلة", MainTab.Expenses => "المصاريف", MainTab.Visits => "الزيارات", _ => "المعالجات" };
                AnalyticsDashboard.ShowData(StatisticsAnalytics.Build(observations, start, period, today, title,
                    tab is MainTab.Income or MainTab.Expenses, tab == MainTab.Expenses));
                CategoryBreakdown.Opacity = 1;
                CategoryBreakdown.IsEnabled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Statistics refresh failed: {ex}");
                if (version != _refreshVersion) return;
                AnalyticsDashboard.ShowError();
                CategoryBreakdown.Visibility = Visibility.Collapsed;
            }
        }

        // ── Income ────────────────────────────────────────────────────────────

        private void ShowIncome(List<Visit> allVisits, List<Visit> visits, List<string> knownTreatments)
        {
            _pieMetric = PieMetric.Money;
            // 2. Aggregate actual paid amounts per treatment using the same FIFO
            //    allocation as الشؤون المالية. Billed-but-unpaid treatment costs
            //    must not be included in an income chart.
            var totals = BuildTreatmentIncomeTotals(allVisits, visits);

            // 3. Build slice list: known treatments first, then unknowns, أخرى last
            var slices = BuildIncomeSlices(knownTreatments, totals);
            DrawPie(slices);
        }

        private static Dictionary<string, double> BuildTreatmentIncomeTotals(
            List<Visit> allVisits,
            IEnumerable<Visit> selectedVisits)
        {
            var totals = new Dictionary<string, double>(StringComparer.Ordinal);
            var patientVisits = allVisits
                .GroupBy(v => v.PatientId)
                .ToDictionary(g => g.Key, g => g.OrderBy(v => v.VisitDate).ThenBy(v => v.Id).ToList());

            // Queues are read-only during allocation; build each patient's queue once per refresh.
            var treatmentQueues = new Dictionary<int, List<(string Name, double TotalCostSyp)>>();

            foreach (var visit in selectedVisits)
            {
                if (visit.TodayPaid <= 0.009) continue;

                if (!patientVisits.TryGetValue(visit.PatientId, out var history))
                {
                    AddTo(totals, OtherLabel, visit.TodayPaid);
                    continue;
                }

                if (!treatmentQueues.TryGetValue(visit.PatientId, out var queue))
                {
                    queue = BuildTreatmentQueue(history);
                    treatmentQueues.Add(visit.PatientId, queue);
                }
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

        private void ShowVisits(List<Visit> visits)
        {
            _pieMetric = PieMetric.Count;
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

        private void ShowTreatments(List<Visit> visits, List<string> knownTreatments)
        {
            _pieMetric = PieMetric.Count;
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

        private void ShowExpenses(List<ExpenseEntry> expenses)
        {
            _pieMetric = PieMetric.Money;
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
            CategoryBreakdown.Visibility = Visibility.Visible;
            CategoryBreakdown.ShowData(slices.Select(s => new StatisticsCategory(s.Label, s.Amount, CategoryColor(s.Label))),
                _pieMetric == PieMetric.Money, _mainTab == MainTab.Expenses ? category =>
                {
                    var sender = new Grid { Tag = category };
                    ExpenseLegendRow_Click(sender, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left));
                } : null);
        }

        // Stable across sorting, tab changes, interface styles, and application restarts.
        private static string CategoryColor(string label)
        {
            uint hash = 2166136261;
            foreach (char c in label) hash = unchecked((hash ^ c) * 16777619);
            return SliceColors[hash % (uint)SliceColors.Length];
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
            // BtnExportPdf (combined report) is visible on every tab.
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
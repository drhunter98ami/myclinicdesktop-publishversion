using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MyClinic;

public partial class FinancialRecordsView : UserControl
{
    private bool _initialized, _updating, _hasLoadedData, _refreshRequested = true, _busy;
    private Task? _refreshTask;
    private bool _loadError;
    private FinancePeriod _period = FinancePeriod.Month;
    private FinanceDateRange _range;
    private List<Patient> _patients = new();
    private List<Visit> _visits = new();
    private List<ExpenseEntry> _expenses = new();
    private List<UpcomingPayment> _upcoming = new();
    private List<FinanceAccount> _accounts = new();
    private List<FinanceVisitRow> _rows = new();
    private List<FinanceUpcomingRow> _filteredUpcoming = new();
    private int _upcomingPage;
    private const int UpcomingPageSize = 50;
    private FinanceAccount? _selectedAccount;
    private int? _expenseEditId, _upcomingEditId;
    private TaskCompletionSource<bool>? _confirmation;
    private IInputElement? _confirmationFocus, _patientReturnFocus;
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private Border? _rangeIndicator;
    private Grid? _rangeHost;

    public FinancialRecordsView()
    {
        InitializeComponent();
        _updating = true;
        CmbExpenseCategory.ItemsSource = CmbUpcomingCategory.ItemsSource = ExpenseCategories.Labels;
        CategoryFilter.ItemsSource = new[] { "كل الفئات" }.Concat(ExpenseCategories.Labels).ToList();
        CategoryFilter.SelectedIndex = UpcomingStatusFilter.SelectedIndex = AccountAgeFilter.SelectedIndex = CmbUnpaidSort.SelectedIndex = 0;
        DpFilterDate.SelectedDate = DpFilterEnd.SelectedDate = DpExpenseDate.SelectedDate = DpPaymentDate.SelectedDate = AppClock.Today;
        _range = FinanceDateRange.For(_period, AppClock.Today);
        _searchDelay.Tick += (_, _) => { _searchDelay.Stop(); UpdateSuggestions(); };
        _updating = false; _initialized = true;
        ConfigureFinancePalette(false, false);
        Loaded += FinancialRecordsView_Loaded; Unloaded += FinancialRecordsView_Unloaded;
        IsVisibleChanged += FinancialRecordsView_IsVisibleChanged;
        foreach (DatePicker date in new[] { DpFilterDate, DpFilterEnd, DpExpenseDate, DpPaymentDate, DpUpcomingDue })
            date.DateValidationError += (_, e) => { e.ThrowException = false; ShowStatus("أدخل تاريخاً صحيحاً أو اختره من التقويم.", true); };
        ApplyFilters();
    }
    private void FinancialRecordsView_Loaded(object sender, RoutedEventArgs e)
    {
        GlobalEvents.OnFinancialRecordAdded -= FinancialChanged; GlobalEvents.OnFinancialRecordAdded += FinancialChanged;
        GlobalEvents.OnPatientRecordAdded -= FinancialChanged; GlobalEvents.OnPatientRecordAdded += FinancialChanged;
        MotionPreferences.Changed -= MotionChanged; MotionPreferences.Changed += MotionChanged;
        GlobalEvents.OnThemeChanged -= FinanceThemeChanged; GlobalEvents.OnThemeChanged += FinanceThemeChanged;
        bool original = Window.GetWindow(this) is OriginalTheme.MainWindow;
        bool dark = (TryFindResource("AppTextPrimary") as SolidColorBrush)?.Color.R > 180;
        ConfigureFinancePalette(original, dark);
        InstallRangeIndicator(); UpdateRangeButtons(); RefreshFinancialData();
    }
    private void FinancialRecordsView_Unloaded(object sender, RoutedEventArgs e)
    {
        GlobalEvents.OnFinancialRecordAdded -= FinancialChanged; GlobalEvents.OnPatientRecordAdded -= FinancialChanged;
        MotionPreferences.Changed -= MotionChanged; _searchDelay.Stop(); FinishConfirmation(false);
        GlobalEvents.OnThemeChanged -= FinanceThemeChanged;
    }
    private void FinancialRecordsView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    { if (IsVisible && _initialized) RefreshFinancialData(); }
    private void FinancialChanged() => Dispatcher.Invoke(() => { RequestRefresh(); if (IsVisible) _ = EnsureDataCurrentAsync(); });
    private void MotionChanged(object? sender, EventArgs e)
    {
        LoadingProgress.IsIndeterminate = MotionPreferences.Enabled;
        if (MotionPreferences.Enabled) return;
        FinanceMotion.StopTree(this);
        foreach (var label in new[] { TxtTotalIncome, TxtTotalExpenses, TxtNetAmount }) AnimatedNumber.Cancel(label);
    }
    private void FinanceThemeChanged(bool dark) => ConfigureFinancePalette(Window.GetWindow(this) is OriginalTheme.MainWindow, dark);
    private void ConfigureFinancePalette(bool original, bool dark)
    {
        SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
        Resources["FinancePositive"] = Brush(dark ? "#5EEAD4" : "#087666");
        Resources["FinanceNegative"] = Brush(dark ? "#E35D64" : "#991B1B");
        Resources["FinanceWarning"] = Brush(dark ? "#FCD34D" : "#8A5800");
        if (!original) return;
        Resources["AppTextSecondary"] = Brush(dark ? "#B0BDCE" : "#52627A");
        Resources["AppSidebarHoverBg"] = Brush(dark ? "#243449" : "#E8F2F1");
        foreach (var (key, source) in new[] { ("SettingsCardBg", "AppCardBg"), ("SettingsCardBorder", "AppBorder"),
            ("SettingsInputBg", "AppInputBg"), ("SettingsInputBorder", "AppBorder"), ("SettingsTableContainerBg", "AppInputBg"),
            ("SettingsTableRowBorder", "AppBorder"), ("SettingsOutlineBtnBg", "AppCardBg"), ("SettingsOutlineBtnBorder", "AppBorder") })
            Resources[key] = TryFindResource(source) ?? (dark ? Brushes.DarkSlateGray : Brushes.White);
        Resources["DashCalendarTodayNumFg"] = Brush(dark ? "#5EEAD4" : "#0F766E");
        Resources["DashCalendarTodayBorder"] = Resources["DashCalendarTodayNumFg"];
        Resources["DashActiveFilterBg"] = Brush("#0F766E");
        Resources["SettingsPrimaryBtnBg"] = Brush("#0F766E");
        Resources["SettingsDeleteBtnBorder"] = Resources["FinanceNegative"];
    }
    public void RefreshDateForAppTimeChange(DateTime previousAppTime)
    {
        if (DpFilterDate.SelectedDate?.Date == previousAppTime.Date) DpFilterDate.SelectedDate = AppClock.Today;
        DpExpenseDate.SelectedDate = DpPaymentDate.SelectedDate = AppClock.Today; RefreshFinancialData();
    }
    public void RequestRefresh() => _refreshRequested = true;
    public async Task FocusExpenseAsync(int id)
    {
        RequestRefresh(); await EnsureDataCurrentAsync();
        var expense = _expenses.FirstOrDefault(e => e.Id == id);
        if (expense == null) { ShowStatus("لم يعد المصروف المرتبط موجوداً.", true); return; }
        _updating = true; WorkspaceSearch.Clear(); CategoryFilter.SelectedIndex = 0;
        _period = FinancePeriod.Day; DpFilterDate.SelectedDate = expense.ExpenseDate.Date;
        FinanceTabs.SelectedIndex = 1; _updating = false; ApplyFilters();
        var row = ExpenseItemsControl.Items.OfType<FinanceExpenseRow>().FirstOrDefault(e => e.Id == id);
        if (row != null) { ExpenseItemsControl.SelectedItem = row; ExpenseItemsControl.ScrollIntoView(row); FinanceMotion.Highlight(ExpenseItemsControl, id); }
    }
    public async void RefreshFinancialData() { RequestRefresh(); await EnsureDataCurrentAsync(); }
    public Task EnsureDataCurrentAsync()
    {
        if (_refreshTask != null) return _refreshTask;
        if (!_refreshRequested && _hasLoadedData) return Task.CompletedTask;
        return _refreshTask = RefreshPendingAsync();
    }
    private async Task RefreshPendingAsync()
    {
        await Task.Yield();
        try { do { _refreshRequested = false; await RefreshDataAsync(); } while (_refreshRequested && IsVisible); }
        finally { _refreshTask = null; }
    }
    private async Task RefreshDataAsync()
    {
        LoadingProgress.IsIndeterminate = MotionPreferences.Enabled;
        if (!_hasLoadedData) LoadingOverlay.Visibility = Visibility.Visible; else ShowStatus("جارٍ تحديث البيانات…");
        try
        {
            var data = await Task.Run(async () =>
            {
                using var db = new AppDbContext();
                var patients = await db.Patients.AsNoTracking().ToListAsync();
                var visits = await db.Visits.AsNoTracking().ToListAsync();
                var expenses = await db.Expenses.AsNoTracking().ToListAsync();
                var upcoming = await db.UpcomingPayments.AsNoTracking().ToListAsync();
                return (patients, visits, expenses, upcoming);
            });
            _patients = data.patients; _visits = data.visits; _expenses = data.expenses; _upcoming = data.upcoming;
            RebuildLedger(); _hasLoadedData = true; _loadError = false; ApplyFilters();
            if (_selectedAccount != null) SelectPatient(_selectedAccount.PatientId, false);
            StatusHost.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {
            _loadError = true;
            ShowStatus(_hasLoadedData ? "تعذر تحديث البيانات. المعروض من آخر تحميل ناجح؛ أعد المحاولة." : "تعذر تحميل السجل المالي. أعد المحاولة.", true, true);
            _refreshRequested = false;
        }
        finally { LoadingOverlay.Visibility = Visibility.Collapsed; }
    }
    private void RebuildLedger()
    {
        var groups = _visits.GroupBy(v => v.PatientId).ToDictionary(g => g.Key, g => g.ToList());
        _accounts = _patients.Select(p => FinancialLedger.BuildAccount(p, groups.GetValueOrDefault(p.Id, new()).Where(v => v.VisitDate.Date <= AppClock.Today))).ToList();
        _rows = _patients.SelectMany(p => FinancialLedger.BuildAccount(p, groups.GetValueOrDefault(p.Id, new())).Rows).OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToList();
    }
    private bool TryRange(out FinanceDateRange range)
    {
        range = _range;
        if (!TryReadDate(DpFilterDate, true, out var start)) return false;
        DateTime? end = start;
        if (_period == FinancePeriod.Custom && !TryReadDate(DpFilterEnd, true, out end)) return false;
        range = FinanceDateRange.For(_period, start!.Value, end);
        if (range.End >= range.Start) return true;
        ShowStatus("يجب أن يكون تاريخ النهاية مساوياً للبداية أو بعدها.", true); return false;
    }
    private string Query => PatientRecordSearch.Normalize(WorkspaceSearch.Text);
    private bool Matches(string text) => Query.Length == 0 || PatientRecordSearch.Normalize(text).Contains(Query);
    private bool MatchesCategory(string category) => CategoryFilter.SelectedIndex <= 0 || category == CategoryFilter.SelectedItem as string;
    private void ApplyFilters()
    {
        if (!_initialized || _updating || !TryRange(out var range)) return;
        _range = range;
        string key = $"{_period}|{range.Start:yyyyMMdd}|{range.End:yyyyMMdd}|{Query}|{CategoryFilter.SelectedIndex}";
        var income = _rows.Where(r => range.Contains(r.Date) && Matches($"{r.PatientName} {r.Phone} {r.Kind} {r.Note}")).ToList();
        var expenses = _expenses.Select(e => new FinanceExpenseRow { Id = e.Id, Date = e.ExpenseDate, Description = e.Description, Amount = FinanceMoney.From(e.Amount), Category = ExpenseCategories.Classify(e.Description) })
            .Where(e => range.Contains(e.Date) && Matches(e.Description) && MatchesCategory(e.Category)).OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).ToList();
        var upcoming = _upcoming.Select(p => new FinanceUpcomingRow { Id = p.Id, Description = p.Description, Category = ExpenseCategories.Classify(p.Description), Recipient = p.Recipient,
            Due = p.DueDate, Status = p.Status, Amount = FinanceMoney.From(p.Amount), ExpenseId = p.ExpenseId, CompletedAt = p.CompletedAt })
            .Where(p => Matches(p.Description) && MatchesCategory(p.Category) && (UpcomingStatusFilter.SelectedIndex == 3 || p.Status == (UpcomingStatusFilter.SelectedIndex switch { 1 => "Paid", 2 => "Cancelled", _ => "Pending" })))
            .OrderBy(p => p.StatusOrder).ThenBy(p => p.DueSort).ThenBy(p => p.Id).ToList();
        var accounts = _accounts.Where(a => a.Balance > 0 && Matches($"{a.PatientName} {a.Phone}") && (AccountAgeFilter.SelectedIndex switch
            { 1 => a.AgeDays <= 30, 2 => a.AgeDays is > 30 and <= 60, 3 => a.AgeDays is > 60 and <= 90, 4 => a.AgeDays > 90, _ => true }));
        accounts = CmbUnpaidSort.SelectedIndex switch { 1 => accounts.OrderBy(a => a.PatientName), 2 => accounts.OrderBy(a => a.FirstDebtDate), _ => accounts.OrderByDescending(a => a.Balance) };
        SetItems(IncomeItemsControl, income, key); SetItems(ExpenseItemsControl, expenses, key);
        _filteredUpcoming = upcoming;
        _upcomingPage = Math.Clamp(_upcomingPage, 0, Math.Max(0, (upcoming.Count - 1) / UpcomingPageSize));
        SetItems(UpcomingItemsControl, upcoming.Skip(_upcomingPage * UpcomingPageSize).Take(UpcomingPageSize).ToList(), "upcoming|" + Query + CategoryFilter.SelectedIndex + UpcomingStatusFilter.SelectedIndex + "|" + _upcomingPage);
        UpcomingPageLabel.Text = $"الصفحة {_upcomingPage + 1} من {Math.Max(1, (upcoming.Count + UpcomingPageSize - 1) / UpcomingPageSize)} · {upcoming.Count:N0} نتيجة";
        BtnUpcomingPrevious.IsEnabled = _upcomingPage > 0;
        BtnUpcomingNext.IsEnabled = (_upcomingPage + 1) * UpcomingPageSize < upcoming.Count;
        SetItems(UnpaidItemsControl, accounts.ToList(), "accounts|" + Query + AccountAgeFilter.SelectedIndex);
        decimal collected = income.Sum(r => r.Paid), spent = expenses.Sum(e => e.Amount);
        AnimatedNumber.Show(TxtTotalIncome, (double)collected, "{0:#,0.##} ل.س");
        AnimatedNumber.Show(TxtTotalExpenses, (double)spent, "{0:#,0.##} ل.س");
        AnimatedNumber.Show(TxtNetAmount, (double)(collected - spent), "{0:#,0.##} ل.س");
        TxtNetAmount.SetResourceReference(TextBlock.ForegroundProperty, collected < spent ? "FinanceNegative" : "DashCalendarTodayNumFg");
        TxtOutstanding.Text = FinanceMoney.Format(_accounts.Sum(a => Math.Max(0, a.Balance)));
        TxtIncomeFooterTotal.Text = FinanceMoney.Format(collected); TxtExpenseFooterTotal.Text = FinanceMoney.Format(spent);
        var previousRange = PreviousRange(range);
        decimal previousIncome = _rows.Where(r => previousRange.Contains(r.Date) && Matches($"{r.PatientName} {r.Phone} {r.Kind} {r.Note}")).Sum(r => r.Paid);
        decimal previousExpense = _expenses.Where(e => previousRange.Contains(e.ExpenseDate) && Matches(e.Description) && MatchesCategory(ExpenseCategories.Classify(e.Description))).Sum(e => FinanceMoney.From(e.Amount));
        IncomeComparison.Text = Comparison(collected, previousIncome); ExpenseComparison.Text = Comparison(spent, previousExpense);
        NetComparison.Text = $"التغير: {FinanceMoney.Format(collected - spent - previousIncome + previousExpense)}";
        IncomeComparison.ToolTip = ExpenseComparison.ToolTip = NetComparison.ToolTip = $"المقارنة مع {previousRange.Label} بنفس الفلاتر";
        TxtVisitCount.Text = $"{income.Count(r => !r.FinancialOnly):N0} زيارة علاجية · {income.Count(r => r.FinancialOnly):N0} عملية مستقلة";
        TxtPendingCount.Text = $"{_upcoming.Count(p => p.Status == "Pending"):N0} مستحق معلق";
        TxtUnpaidTotal.Text = $"{accounts.Count():N0} مريض · الإجمالي المعروض: {FinanceMoney.Format(accounts.Sum(a => a.Balance))}";
        EmptyIncomeText.Visibility = income.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyExpenseText.Visibility = expenses.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyUpcomingText.Visibility = upcoming.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyUnpaidText.Visibility = accounts.Any() ? Visibility.Collapsed : Visibility.Visible;
        ((TextBlock)EmptyIncomeText.Children[0]).Text = _rows.Count == 0 ? "لا توجد سجلات مالية بعد." : "لا توجد سجلات دخل مطابقة للفلاتر.";
        ((TextBlock)EmptyExpenseText.Children[0]).Text = _expenses.Count == 0 ? "لم تُسجل مصاريف بعد." : "لا توجد مصاريف مطابقة للفلاتر.";
        ((TextBlock)EmptyUpcomingText.Children[0]).Text = _upcoming.Count == 0 ? "لم تُسجل مستحقات بعد." : "لا توجد مستحقات مطابقة للفلاتر.";
        ((TextBlock)EmptyUnpaidText.Children[0]).Text = _accounts.All(a => a.Balance <= 0) ? "جميع حسابات المرضى مسددة." : "لا توجد أرصدة مستحقة تطابق البحث.";
        ExpenseBreakdown.ItemsSource = expenses.GroupBy(e => e.Category).Select(g => new { Category = g.Key, Amount = g.Sum(e => e.Amount) }).OrderByDescending(g => g.Amount)
            .Select(g => new { g.Category, AmountText = FinanceMoney.Format(g.Amount), Percent = spent == 0 ? 0 : (double)(g.Amount / spent * 100), Color = ExpenseCategoryBrush(g.Category) }).ToList();
        PeriodLabel.Text = range.Label + (Query.Length > 0 ? " · نتائج البحث" : "") + (CategoryFilter.SelectedIndex > 0 ? " · فئة المصاريف: " + CategoryFilter.SelectedItem : "");
        UpdateResultsLabel(); UpdateRangeButtons();
    }
    private FinanceDateRange PreviousRange(FinanceDateRange range)
    {
        var previous = _period switch { FinancePeriod.Month => FinanceDateRange.For(FinancePeriod.Month, range.Start.AddMonths(-1)), FinancePeriod.Year => FinanceDateRange.For(FinancePeriod.Year, range.Start.AddYears(-1)), _ => range.Previous };
        if (!range.Contains(AppClock.Today)) return previous;
        if (_period == FinancePeriod.Month) return new(previous.Start, previous.Start.AddDays(Math.Min(AppClock.Today.Day, previous.End.Day) - 1));
        if (_period == FinancePeriod.Year) return new(previous.Start, AppClock.Today.AddYears(-1));
        return previous;
    }
    private static string Comparison(decimal current, decimal previous) => previous == 0 ? (current == 0 ? "لا تغير عن الفترة السابقة" : "لا قيمة مقارنة سابقة") : $"{(current >= previous ? "+" : "")}{(current - previous) / Math.Abs(previous) * 100:0.#}% عن الفترة السابقة";
    private static Brush ExpenseCategoryBrush(string category)
    {
        // Use the category's fixed position so filtering and ranking never change its color.
        string[] colors = { "#166534", "#22C55E", "#047857", "#84CC16", "#0D9488", "#4D7C0F", "#10B981", "#15803D", "#65A30D" };
        int index = ExpenseCategories.Labels.ToList().IndexOf(category);
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[Math.Max(0, index) % colors.Length]));
        brush.Freeze();
        return brush;
    }
    private void SetItems<T>(DataGrid grid, List<T> rows, string key)
    {
        using var scroll = ScrollState.Updating(grid, key);
        var sorts = CollectionViewSource.GetDefaultView(grid.ItemsSource)?.SortDescriptions.ToList() ?? new();
        grid.ItemsSource = rows; var view = CollectionViewSource.GetDefaultView(rows);
        foreach (var sort in sorts) view.SortDescriptions.Add(sort);
    }
    private DataGrid ActiveGrid => FinanceTabs.SelectedIndex switch { 1 => ExpenseItemsControl, 2 => UpcomingItemsControl, 3 => UnpaidItemsControl, _ => IncomeItemsControl };
    private void UpdateResultsLabel() => ResultsLabel.Text = FinanceTabs.SelectedIndex == 2 ? $"{UpcomingItemsControl.Items.Count:N0} معروض من {_filteredUpcoming.Count:N0} مستحق" : $"{ActiveGrid.Items.Count:N0} سجل معروض";
    private void WorkspaceFilter_Changed(object sender, RoutedEventArgs e) { if (_initialized && !_updating) { _upcomingPage = 0; ApplyFilters(); FinanceMotion.Fade(ActiveGrid); } }
    private void PreviousUpcomingPage_Click(object sender, RoutedEventArgs e) { if (_upcomingPage > 0) { _upcomingPage--; ApplyFilters(); } }
    private void NextUpcomingPage_Click(object sender, RoutedEventArgs e) { if ((_upcomingPage + 1) * UpcomingPageSize < _filteredUpcoming.Count) { _upcomingPage++; ApplyFilters(); } }
    private void DateFilter_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized && !_updating) { ApplyFilters(); FinanceMotion.Fade(ActiveGrid); } }
    private void Range_Click(object sender, RoutedEventArgs e)
    { if (sender is Button b && Enum.TryParse<FinancePeriod>(b.Tag?.ToString(), out var period)) { _period = period; ApplyFilters(); FinanceMotion.Fade(ActiveGrid); } }
    private void PreviousPeriod_Click(object sender, RoutedEventArgs e) => MovePeriod(-1);
    private void NextPeriod_Click(object sender, RoutedEventArgs e) => MovePeriod(1);
    private void MovePeriod(int direction)
    {
        if (!TryRange(out var range)) return;
        _updating = true; var date = DpFilterDate.SelectedDate ?? AppClock.Today;
        try
        {
            DpFilterDate.SelectedDate = _period switch { FinancePeriod.Month => date.AddMonths(direction), FinancePeriod.Year => date.AddYears(direction), FinancePeriod.Custom => range.Start.AddDays(((range.End - range.Start).Days + 1) * direction), _ => date.AddDays(direction) };
            if (_period == FinancePeriod.Custom) DpFilterEnd.SelectedDate = range.End.AddDays(((range.End - range.Start).Days + 1) * direction);
        }
        catch (ArgumentOutOfRangeException) { ShowStatus("لا يمكن الانتقال إلى هذا التاريخ.", true); }
        finally { _updating = false; }
        ApplyFilters(); FinanceMotion.Fade(ActiveGrid);
    }
    private void CurrentPeriod_Click(object sender, RoutedEventArgs e)
    { _updating = true; DpFilterDate.SelectedDate = DpFilterEnd.SelectedDate = AppClock.Today; _updating = false; ApplyFilters(); }
    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        _updating = true; WorkspaceSearch.Clear(); CategoryFilter.SelectedIndex = AccountAgeFilter.SelectedIndex = CmbUnpaidSort.SelectedIndex = UpcomingStatusFilter.SelectedIndex = 0;
        _upcomingPage = 0;
        DpFilterDate.SelectedDate = DpFilterEnd.SelectedDate = AppClock.Today; _period = FinancePeriod.Month;
        foreach (DataGrid grid in new[] { IncomeItemsControl, ExpenseItemsControl, UpcomingItemsControl, UnpaidItemsControl })
        { CollectionViewSource.GetDefaultView(grid.ItemsSource)?.SortDescriptions.Clear(); foreach (var column in grid.Columns) column.SortDirection = null; }
        _updating = false; ApplyFilters();
    }
    private void InstallRangeIndicator()
    {
        if (_rangeHost != null || RangeButtons.Parent is not StackPanel parent) return;
        int index = parent.Children.IndexOf(RangeButtons); parent.Children.Remove(RangeButtons);
        RangeButtons.FlowDirection = FlowDirection.RightToLeft;
        _rangeHost = new Grid { FlowDirection = FlowDirection.LeftToRight };
        _rangeIndicator = new Border { Height = 36, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new(8), IsHitTestVisible = false };
        _rangeIndicator.SetResourceReference(Border.BackgroundProperty, "AppSidebarHoverBg");
        _rangeHost.Children.Add(_rangeIndicator); _rangeHost.Children.Add(RangeButtons); parent.Children.Insert(index, _rangeHost);
        RangeButtons.SizeChanged += (_, _) => UpdateRangeButtons();
    }
    private void UpdateRangeButtons()
    {
        EndDateHost.Visibility = _period == FinancePeriod.Custom ? Visibility.Visible : Visibility.Collapsed;
        BtnCurrentDate.Content = _period switch { FinancePeriod.Day => "اليوم", FinancePeriod.Year => "هذه السنة", FinancePeriod.Custom => "اليوم", _ => "هذا الشهر" };
        foreach (Button button in RangeButtons.Children)
        {
            bool active = button.Tag?.ToString() == _period.ToString(); button.Background = Brushes.Transparent;
            button.SetResourceReference(Control.ForegroundProperty, active ? "DashCalendarTodayNumFg" : "AppTextSecondary");
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, active ? "محدد" : "");
            if (active && _rangeHost != null && _rangeIndicator != null) FinanceMotion.MoveSelector(_rangeIndicator, button, _rangeHost);
        }
    }
    private void FinanceTabs_Changed(object sender, SelectionChangedEventArgs e)
    { if (_initialized && ReferenceEquals(e.OriginalSource, FinanceTabs)) { UpdateResultsLabel(); FinanceMotion.Fade(ActiveGrid); } }
    private void IncomeCard_Click(object sender, RoutedEventArgs e) => FinanceTabs.SelectedIndex = 0;
    private void ExpenseCard_Click(object sender, RoutedEventArgs e) => FinanceTabs.SelectedIndex = 1;
    private void AccountsCard_Click(object sender, RoutedEventArgs e) => FinanceTabs.SelectedIndex = 3;
    private void ToggleDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || Ancestor<DataGridRow>(button) is not DataGridRow row) return;
        bool open = row.DetailsVisibility != Visibility.Visible; row.DetailsVisibility = open ? Visibility.Visible : Visibility.Collapsed;
        var arrow = new TextBlock { Text = "⌄", RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(open ? 180 : 0), Margin = new Thickness(0, 0, 4, 0) };
        var content = new StackPanel { Orientation = Orientation.Horizontal }; content.Children.Add(arrow); content.Children.Add(new TextBlock { Text = open ? "إخفاء" : "عرض" }); button.Content = content;
        if (MotionPreferences.Enabled) ((RotateTransform)arrow.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,
            new System.Windows.Media.Animation.DoubleAnimation(open ? 0 : 180, open ? 180 : 0, MotionPreferences.Duration(165)) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
        System.Windows.Automation.AutomationProperties.SetName(button, open ? "طي تفاصيل العلاجات" : "توسيع تفاصيل العلاجات");
    }
    private static T? Ancestor<T>(DependencyObject item) where T : DependencyObject
    { for (DependencyObject? current = item; current != null; current = VisualTreeHelper.GetParent(current)) if (current is T found) return found; return null; }
}

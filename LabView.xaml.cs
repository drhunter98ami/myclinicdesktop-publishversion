using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace MyClinic;

public sealed class LabRow
{
    public LabWork Work { get; init; } = new();
    public int Id => Work.Id;
    public string Patient => Work.PatientName;
    public string Lab => Work.LabName ?? "—";
    public string WorkType => Work.WorkType;
    public string WorkStatus => Work.WorkStatus;
    public DateTime? ExpectedDelivery => Work.ExpectedDelivery;
    public decimal Balance => Math.Max(0, Work.Cost - Work.AmountPaid);
    public string BalanceText => WorkspaceUi.Money(Balance, Work.Currency);
    public string PaymentStatus => Work.AmountPaid <= 0 ? "غير مدفوع" : Balance > 0 ? "مدفوع جزئياً" : "مدفوع";
    public bool Overdue => !Work.DateReceived.HasValue && Work.ExpectedDelivery?.Date < AppClock.Today;
    public string Delivery => Work.DateReceived.HasValue ? "تم الاستلام" : Work.ExpectedDelivery is DateTime due ? (Overdue ? "متأخر · " : "") + due.ToString("dd/MM/yyyy") : "غير محدد";
}
public sealed record LabTimeline(string Description, string Detail, int? ExpenseId = null)
{ public Visibility ExpenseVisibility => ExpenseId.HasValue ? Visibility.Visible : Visibility.Collapsed; }

public partial class LabView : UserControl
{
    List<LabWork> _works = new(); List<LabName> _labs = new(); List<Patient> _patients = new(); List<ClinicOperation> _operations = new();
    LabWork? _selected; int? _editId, _patientId; bool _ready, _loadingPatient, _loadingEditor, _busy, _refreshing; decimal _rate;
    IInputElement? _returnFocus;
    string _costCurrency = "USD"; decimal? _automaticCost, _conversionSourceCost; string _conversionSourceCurrency = "USD";
    static readonly string[] Stages = ["تم الإرسال", "قيد العمل", "جاهز", "تم الاستلام"];
    readonly Func<AppDbContext> _createContext;
    public LabView() : this(() => new AppDbContext()) { }
    public LabView(Func<AppDbContext> createContext)
    {
        _createContext = createContext;
        InitializeComponent();
        WorkFilter.ItemsSource = new[] { "كل الحالات" }.Concat(Stages); WorkFilter.SelectedIndex = 0;
        PaymentFilter.ItemsSource = new[] { "كل الدفعات", "غير مدفوع", "مدفوع جزئياً", "مدفوع" }; PaymentFilter.SelectedIndex = 0;
        DateBasis.ItemsSource = new[] { "تاريخ الإرسال", "موعد التسليم", "تاريخ الاستلام", "تاريخ الدفع" }; DateBasis.SelectedIndex = 0;
        Period.ItemsSource = new[] { "الكل", "يومي", "شهري", "سنوي", "مخصص" }; Period.SelectedIndex = 2;
        WorkCurrency.ItemsSource = new[] { "USD", "SYP" }; WorkCurrency.SelectedIndex = 0;
        FromDate.SelectedDate = ToDate.SelectedDate = AppClock.Today; _ready = true;
        Loaded += LoadedView; Unloaded += UnloadedView;
    }
    void LoadedView(object sender, RoutedEventArgs e)
    { WorkspaceUi.Configure(this); Refresh(); GlobalEvents.OnExchangeRateChanged += RateChanged; GlobalEvents.OnThemeChanged += ThemeChanged; MotionPreferences.Changed += MotionChanged; }
    void UnloadedView(object sender, RoutedEventArgs e)
    { ReleaseResources(); GlobalEvents.OnThemeChanged -= ThemeChanged; MotionPreferences.Changed -= MotionChanged; CloseSide(); }
    public void ReleaseResources() => GlobalEvents.OnExchangeRateChanged -= RateChanged;
    void ThemeChanged(bool dark) => WorkspaceUi.Configure(this);
    void MotionChanged(object? sender, EventArgs e) { if (!MotionPreferences.Enabled) FinanceMotion.StopTree(this); }
    void RateChanged() => Dispatcher.Invoke(Refresh);
    public void RefreshDateForAppTimeChange(DateTime previousAppTime)
    { if (FromDate.SelectedDate?.Date == previousAppTime.Date) FromDate.SelectedDate = AppClock.Today; Filter(); }
    void Refresh()
    {
        if (_refreshing) return; _refreshing = true;
        try
        {
            using var db = _createContext(); _rate = WorkspaceUi.Rate(db);
            _works = db.LabWorks.AsNoTracking().OrderByDescending(w => w.DateSent).ToList();
            _labs = db.LabNames.AsNoTracking().OrderBy(l => l.Name).ToList();
            _patients = db.Patients.AsNoTracking().OrderBy(p => p.FullName).ToList();
            _operations = db.ClinicOperations.AsNoTracking().Where(o => o.Kind == "LabPayment" || o.Kind == "LabStatus" || o.Kind == "LabEdit" || o.Kind == "LabCurrency").ToList();
            string? selected = LabFilter.SelectedItem?.ToString(); LabFilter.ItemsSource = new[] { "كل المخابر" }.Concat(_works.Select(w => w.LabName ?? "—").Concat(_labs.Where(l => !l.IsArchived).Select(l => l.Name)).Distinct().Order());
            LabFilter.SelectedItem = selected; if (LabFilter.SelectedIndex < 0) LabFilter.SelectedIndex = 0;
            Filter();
        }
        catch (Exception ex) { Status.Text = "تعذر تحميل الأعمال: " + ex.Message; }
        finally { _refreshing = false; }
    }
    bool InPeriod(LabWork work, DateTime start, DateTime end)
    {
        if (Period.SelectedIndex == 0) return true;
        if (DateBasis.SelectedIndex == 3)
        {
            var payments = _operations.Where(o => o.Kind == "LabPayment" && o.SourceId == work.Id).ToList();
            return payments.Count > 0 ? payments.Any(o => o.OccurredAt.Date >= start && o.OccurredAt.Date <= end) : work.DatePaid is DateTime paid && paid.Date >= start && paid.Date <= end;
        }
        DateTime? date = DateBasis.SelectedIndex switch { 1 => work.ExpectedDelivery, 2 => work.DateReceived, _ => work.DateSent };
        return date.HasValue && date.Value.Date >= start && date.Value.Date <= end;
    }
    void Filter()
    {
        if (!_ready) return;
        var anchor = FromDate.SelectedDate ?? AppClock.Today;
        DateTime start = Period.SelectedIndex switch { 2 => new(anchor.Year, anchor.Month, 1), 3 => new(anchor.Year, 1, 1), _ => anchor.Date };
        DateTime end = Period.SelectedIndex switch { 2 => start.AddMonths(1).AddDays(-1), 3 => start.AddYears(1).AddDays(-1), 4 => ToDate.SelectedDate ?? anchor, _ => start };
        if (end < start) { Status.Text = "نهاية الفترة تسبق بدايتها."; return; }
        int? selectedId = (WorksGrid.SelectedItem as LabRow)?.Id;
        var sort = CollectionViewSource.GetDefaultView(WorksGrid.ItemsSource)?.SortDescriptions.ToList() ?? new();
        string search = Search.Text.Trim();
        var rows = _works.Where(w => InPeriod(w, start, end)).Select(w => new LabRow { Work = w })
            .Where(r => r.Patient.Contains(search, StringComparison.CurrentCultureIgnoreCase) || r.WorkType.Contains(search, StringComparison.CurrentCultureIgnoreCase) || (r.Work.Notes ?? "").Contains(search, StringComparison.CurrentCultureIgnoreCase))
            .Where(r => LabFilter.SelectedIndex <= 0 || r.Lab == LabFilter.SelectedItem?.ToString())
            .Where(r => WorkFilter.SelectedIndex <= 0 || r.WorkStatus == WorkFilter.SelectedItem?.ToString())
            .Where(r => PaymentFilter.SelectedIndex <= 0 || r.PaymentStatus == PaymentFilter.SelectedItem?.ToString()).ToList();
        WorksGrid.ItemsSource = rows;
        var view = CollectionViewSource.GetDefaultView(WorksGrid.ItemsSource); foreach (var s in sort) view.SortDescriptions.Add(s);
        if (selectedId.HasValue) WorksGrid.SelectedItem = rows.FirstOrDefault(r => r.Id == selectedId);
        Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PendingSummary.Text = $"{rows.Count(r => !r.Work.DateReceived.HasValue)} / {rows.Count(r => r.Overdue)}";
        string Totals(Func<LabRow, decimal> selector) => string.Join(" · ", new[] { "USD", "SYP" }.Select(c => WorkspaceUi.Money(rows.Where(r => r.Work.Currency == c).Sum(selector), c)));
        void Summary(TextBlock target, string text) { if (target.Text != text) { target.Text = text; FinanceMotion.Fade(target, 160); } }
        Summary(CostSummary, Totals(r => r.Work.Cost)); Summary(PaidSummary, Totals(r => r.Work.AmountPaid)); Summary(AdvanceSummary, Totals(r => !r.Work.DateReceived.HasValue ? r.Work.AmountPaid : 0)); Summary(BalanceSummary, Totals(r => r.Balance));
        SummaryScope.Text = $"{rows.Count} أعمال · الملخص يطابق التصفية · " + (Period.SelectedIndex == 0 ? "كل الفترات" : $"{DateBasis.SelectedItem}: {start:dd/MM/yyyy} – {end:dd/MM/yyyy}");
        SummaryScope.ToolTip = "القيم هي أرصدة الأعمال المطابقة؛ سجل الدفعات يعرض تواريخ وقيم العمليات الفعلية.";
        Status.Text = $"{rows.Count} نتائج · انقر فتح لعرض التفاصيل والإجراءات.";
    }
    void Filter_Changed(object sender, RoutedEventArgs e) { if (!_refreshing) Filter(); }
    void Period_Changed(object sender, SelectionChangedEventArgs e)
    { if (!_ready) return; ToDate.Visibility = Period.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed; FromDate.IsEnabled = Period.SelectedIndex != 0; Filter(); }
    void Today_Click(object sender, RoutedEventArgs e) { FromDate.SelectedDate = ToDate.SelectedDate = AppClock.Today; Filter(); }
    void Clear_Click(object sender, RoutedEventArgs e) { Search.Clear(); LabFilter.SelectedIndex = WorkFilter.SelectedIndex = PaymentFilter.SelectedIndex = DateBasis.SelectedIndex = Period.SelectedIndex = 0; Filter(); }
    void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
    void New_Click(object sender, RoutedEventArgs e) => OpenEditor(null);
    void ShowSide(bool editor)
    {
        if (SideOverlay.Visibility != Visibility.Visible) _returnFocus = Keyboard.FocusedElement;
        SideOverlay.Visibility = Visibility.Visible;
        Editor.Visibility = editor ? Visibility.Visible : Visibility.Collapsed; Details.Visibility = editor ? Visibility.Collapsed : Visibility.Visible;
        // The underlying page is excluded from keyboard navigation while the panel is open.
        foreach (UIElement child in Root.Children) if (!ReferenceEquals(child, SideOverlay)) child.IsEnabled = false;
        WorkspaceUi.Reveal(SidePanel); SidePanel.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }
    void CloseSide()
    {
        if (_busy) return;
        SideOverlay.Visibility = Visibility.Collapsed; foreach (UIElement child in Root.Children) child.IsEnabled = true;
        _returnFocus?.Focus();
    }
    void CloseSide_Click(object sender, RoutedEventArgs e) => CloseSide();
    void Page_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape && SideOverlay.Visibility == Visibility.Visible) { CloseSide(); e.Handled = true; } }
    void Root_SizeChanged(object sender, SizeChangedEventArgs e) { SidePanel.Width = Math.Max(240, Math.Min(540, Root.ActualWidth)); WorkTypeColumn.Visibility = Root.ActualWidth < 850 ? Visibility.Collapsed : Visibility.Visible; SummaryCards.Columns = Root.ActualWidth < 560 ? 2 : 4; }
    void OpenEditor(LabWork? work)
    {
        _loadingEditor = true;
        _editId = work?.Id; _selected = work; _loadingPatient = true; PatientName.Text = work?.PatientName ?? ""; _patientId = work?.PatientId; _loadingPatient = false;
        PatientSuggestions.Visibility = Visibility.Collapsed;
        LabSearch.Clear(); LabName.ItemsSource = _labs.Where(l => !l.IsArchived || l.Id == work?.LabNameId).ToList();
        LabName.SelectedItem = _labs.FirstOrDefault(l => l.Id == work?.LabNameId || l.Name == work?.LabName);
        WorkType.Text = work?.WorkType ?? ""; Cost.Text = work?.Cost.ToString("0.####") ?? ""; WorkCurrency.SelectedItem = work?.Currency ?? "USD";
        WorkCurrency.IsEnabled = true; _costCurrency = work?.Currency ?? "USD"; _automaticCost = _conversionSourceCost = null;
        CurrencyConversionRate.Text = _rate.ToString("0.####"); CurrencyConversionHost.Visibility = Visibility.Collapsed;
        ExpectedDate.SelectedDate = work?.ExpectedDelivery ?? (work == null ? AppClock.Today.AddDays(7) : null);
        Teeth.Text = work?.Teeth ?? ""; Notes.Text = work?.Notes ?? ""; CorrectionReason.Clear(); EditorError.Text = "";
        CorrectionHost.Visibility = work == null ? Visibility.Collapsed : Visibility.Visible; SideTitle.Text = work == null ? "إضافة عمل مخبري" : "تصحيح تفاصيل العمل";
        _loadingEditor = false;
        ShowSide(true); PatientName.Focus();
    }
    void Patient_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _loadingPatient) return; _patientId = null;
        string value = PatientName.Text.Trim(); var matches = value.Length < 1 ? new List<Patient>() : _patients.Where(p => (p.FullName ?? "").Contains(value, StringComparison.CurrentCultureIgnoreCase) || p.PhoneNumber.Contains(value)).Take(6).ToList();
        PatientSuggestions.ItemsSource = matches; PatientSuggestions.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    void Patient_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (PatientSuggestions.SelectedItem is not Patient patient) return;
        _loadingPatient = true; PatientName.Text = patient.FullName ?? patient.PhoneNumber; _patientId = patient.Id; _loadingPatient = false; PatientSuggestions.Visibility = Visibility.Collapsed;
    }
    void LabSearch_Changed(object sender, TextChangedEventArgs e)
    { if (!_ready) return; LabName.ItemsSource = _labs.Where(l => !l.IsArchived && l.Name.Contains(LabSearch.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList(); }
    async void SaveWork_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (string.IsNullOrWhiteSpace(PatientName.Text)) { EditorError.Text = "أدخل اسم المريض."; PatientName.Focus(); return; }
        if (LabName.SelectedItem is not LabName lab || string.IsNullOrWhiteSpace(WorkType.Text)) { EditorError.Text = "اختر المخبر وأدخل نوع العمل."; return; }
        if (!WorkspaceUi.Number(Cost.Text, out decimal cost) || cost <= 0 || cost > 1_000_000_000m) { ValidationAssist.Show(Cost, "أدخل تكلفة موجبة ضمن الحد المسموح."); return; }
        string teeth = WorkspaceUi.Normalize(Teeth.Text).Replace('،', ',');
        var toothIds = teeth.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        if (toothIds.Any(t => !ValidTooth(t))) { ValidationAssist.Show(Teeth, "أدخل أرقام FDI صحيحة مفصولة بفاصلة، مثل 11, 21."); return; }
        if (_editId.HasValue && string.IsNullOrWhiteSpace(CorrectionReason.Text)) { ValidationAssist.Show(CorrectionReason, "أدخل سبب التصحيح."); return; }
        string currency = WorkCurrency.SelectedItem?.ToString() ?? "USD";
        bool changingCurrency = _selected != null && _selected.Currency != currency;
        decimal conversionRate = _rate;
        if (changingCurrency && (!WorkspaceUi.Number(CurrencyConversionRate.Text, out conversionRate) || conversionRate <= 0 || conversionRate > 1_000_000_000m))
        { ValidationAssist.Show(CurrencyConversionRate, "أدخل سعر تحويل موجباً."); return; }
        var details = new LabWorkDetails(PatientName.Text.Trim(), _patientId, lab.Id, WorkType.Text.Trim(), cost, currency,
            string.Join(", ", toothIds), Notes.Text.Trim(), ExpectedDate.SelectedDate, CorrectionReason.Text.Trim(), conversionRate);
        _busy = true; Editor.IsEnabled = false;
        try
        {
            using var db = _createContext();
            int id = await new ClinicTransactions(db).SaveLabDetailsAsync(_editId, _selected?.Revision ?? 0, details);
            _busy = false; CloseSide(); Refresh(); Status.Text = "✓ تم حفظ العمل."; FinanceMotion.Highlight(WorksGrid, id);
        }
        catch (Exception ex) { EditorError.Text = ex.Message; }
        finally { _busy = false; Editor.IsEnabled = true; }
    }
    void WorkCurrency_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _loadingEditor) return;
        string target = WorkCurrency.SelectedItem?.ToString() ?? "USD";
        if (_selected != null && target != _costCurrency && WorkspaceUi.Number(Cost.Text, out decimal cost) && cost is > 0 and <= 1_000_000_000m && WorkspaceUi.Number(CurrencyConversionRate.Text, out decimal rate) && rate is > 0 and <= 1_000_000_000m)
        {
            _conversionSourceCost = cost; _conversionSourceCurrency = _costCurrency;
            _automaticCost = ClinicTransactions.ConvertLabAmount(cost, _costCurrency, target, rate);
            Cost.Text = _automaticCost.Value.ToString("0.####");
        }
        _costCurrency = target; UpdateCurrencyPreview();
    }
    void ConversionRate_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _loadingEditor) return;
        if (_automaticCost.HasValue && _conversionSourceCost.HasValue && WorkspaceUi.Number(Cost.Text, out decimal cost) && cost == _automaticCost &&
            WorkspaceUi.Number(CurrencyConversionRate.Text, out decimal rate) && rate > 0 && rate <= 1_000_000_000m)
        {
            _automaticCost = ClinicTransactions.ConvertLabAmount(_conversionSourceCost.Value, _conversionSourceCurrency, _costCurrency, rate);
            Cost.Text = _automaticCost.Value.ToString("0.####");
        }
        UpdateCurrencyPreview();
    }
    void CurrencyPreview_Changed(object sender, TextChangedEventArgs e) { if (_ready && !_loadingEditor) UpdateCurrencyPreview(); }
    void UpdateCurrencyPreview()
    {
        string currency = WorkCurrency.SelectedItem?.ToString() ?? "USD";
        bool changing = _selected != null && currency != _selected.Currency;
        CurrencyConversionHost.Visibility = changing ? Visibility.Visible : Visibility.Collapsed;
        if (!changing) return;
        if (!WorkspaceUi.Number(CurrencyConversionRate.Text, out decimal rate) || rate <= 0 || rate > 1_000_000_000m)
        { CurrencyConversionPreview.Text = "أدخل سعر التحويل لحساب المدفوع والرصيد بالعملة الجديدة."; return; }
        decimal paid = ClinicTransactions.ConvertLabAmount(_selected!.AmountPaid, _selected.Currency, currency, rate);
        string balance = WorkspaceUi.Number(Cost.Text, out decimal cost) && cost is >= 0 and <= 1_000_000_000_000m ? WorkspaceUi.Money(cost - paid, currency) : "—";
        CurrencyConversionPreview.Text = $"المدفوع بعد التحويل: {WorkspaceUi.Money(paid, currency)}\nالمتبقي: {balance}\nيمكنك تعديل التكلفة المحوّلة. لا تتغير إيصالات الدفعات والمصاريف السابقة.";
    }
    static bool ValidTooth(string id) => id.Length == 2 && id[0] is >= '1' and <= '8' && id[1] >= '1' && id[1] <= (id[0] <= '4' ? '8' : '5');
    void PickTeeth_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ClinicDialog(this, "اختيار الأسنان", "تطبيق الأسنان")
        { Width = Math.Min(920, SystemParameters.WorkArea.Width - 60) };
        var selected = WorkspaceUi.Normalize(Teeth.Text).Replace('،', ',').Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var chart = new LabTeethPicker(selected); dialog.Fields.Children.Add(chart);
        dialog.OnSubmit(() => { Teeth.Text = string.Join(", ", chart.SelectedTeeth); dialog.Complete(); return Task.CompletedTask; }); dialog.ShowDialog();
    }
    void Grid_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { Details_Click(sender, e); e.Handled = true; } }
    void Details_Click(object sender, RoutedEventArgs e)
    {
        var row = (sender as FrameworkElement)?.DataContext as LabRow ?? WorksGrid.SelectedItem as LabRow;
        if (row == null) return; OpenDetails(row.Work.Id);
    }
    void OpenDetails(int id)
    {
        _selected = _works.FirstOrDefault(w => w.Id == id); if (_selected == null) return; var row = new LabRow { Work = _selected };
        SideTitle.Text = "تفاصيل العمل";
        DetailSummary.Text = $"{row.Patient}\n{row.Lab} · {row.WorkType}\n{row.WorkStatus} · {row.PaymentStatus}\nالتكلفة: {WorkspaceUi.Money(_selected.Cost, _selected.Currency)}\nالمدفوع: {WorkspaceUi.Money(_selected.AmountPaid, _selected.Currency)}\nالمتبقي: {row.BalanceText}";
        DetailNotes.Text = $"الأسنان: {_selected.Teeth ?? "—"}\nالتسليم: {row.Delivery}\n{_selected.Notes}";
        AdvanceWork.IsEnabled = !_selected.DateReceived.HasValue; PayWork.IsEnabled = row.Balance > 0;
        var events = new List<LabTimeline> { new("تم الإرسال", _selected.DateSent.ToString("dd/MM/yyyy HH:mm")) };
        var records = _operations.Where(o => o.SourceId == id).OrderBy(o => o.OccurredAt).ToList();
        if (_selected.DateReceived.HasValue && !records.Any(o => o.Kind == "LabStatus" && o.Description == "تم الاستلام")) events.Add(new("تم الاستلام · سجل سابق", _selected.DateReceived.Value.ToString("dd/MM/yyyy")));
        decimal historicalPaid = ClinicTransactions.LegacyLabPaid(_selected, records);
        if (historicalPaid > 0) events.Add(new("دفعات سابقة", WorkspaceUi.Money(historicalPaid, _selected.Currency) + " · تفاصيل المصروف غير مرتبطة في السجلات القديمة"));
        events.AddRange(records.Select(o => new LabTimeline(o.Kind == "LabPayment" ? "تسجيل دفعة" : o.Description, $"{o.OccurredAt:dd/MM/yyyy HH:mm} · {o.Actor}" + (o.Kind == "LabPayment" ? $"\n{WorkspaceUi.Money(o.Amount, o.Currency)} · المصروف {o.AmountSyp:N2} SYP · سعر الصرف {o.RateSnapshot:N2}" : ""), o.ExpenseId)));
        Timeline.ItemsSource = events; ShowSide(false);
    }
    void EditWork_Click(object sender, RoutedEventArgs e) { if (_selected != null) OpenEditor(_selected); }
    void Advance_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return; var work = _selected; int current = Array.IndexOf(Stages, work.WorkStatus);
        var dialog = new ClinicDialog(this, "تحديث حالة العمل", "تأكيد الحالة"); var stage = dialog.Choice("الحالة الجديدة", Stages.Skip(Math.Max(0, current + 1)));
        dialog.Note("تسجيل الاستلام يحفظ تاريخ اليوم. لا تتغير حالة الدفع.");
        dialog.OnSubmit(async () => { using var db = _createContext(); bool changed = await new ClinicTransactions(db).SetWorkStatusAsync(work.Id, work.Revision, stage.SelectedItem?.ToString() ?? ""); if (!changed) throw new InvalidOperationException("اكتملت هذه الخطوة مسبقاً؛ حدّث القائمة."); dialog.Complete(); Refresh(); OpenDetails(work.Id); FinanceMotion.Highlight(WorksGrid, work.Id); Status.Text = "✓ تم تحديث حالة العمل."; }); dialog.ShowDialog();
    }
    void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return; var work = _selected; var dialog = new ClinicDialog(this, "تسجيل دفعة للمخبر", "تأكيد الدفعة والمصروف");
        dialog.Note($"{work.PatientName} · {work.LabName}\nالمتبقي: {WorkspaceUi.Money(work.Cost - work.AmountPaid, work.Currency)}");
        var amount = dialog.Input("قيمة الدفعة", (work.Cost - work.AmountPaid).ToString("0.####"), true);
        var currency = dialog.Choice("عملة الدفعة", new[] { "USD", "SYP" }, work.Currency);
        var rate = dialog.Input("سعر الصرف · SYP لكل USD", _rate.ToString("0.##"), true); var date = dialog.Date("تاريخ الدفعة", AppClock.Today);
        var preview = new TextBlock { Style = (Style)FindResource("WorkspaceHint"), Margin = new(0, 8, 0, 8) }; dialog.Fields.Children.Add(preview);
        void Preview() { if (WorkspaceUi.Number(amount.Text, out var a) && WorkspaceUi.Number(rate.Text, out var r) && r > 0) { string c = currency.SelectedItem?.ToString() ?? work.Currency; decimal native = c == work.Currency ? a : work.Currency == "USD" ? a / r : a * r; preview.Text = $"سيُسجّل كمصروف: {a * (c == "USD" ? r : 1):N2} SYP\nالمتبقي بعد الدفعة: {work.Cost - work.AmountPaid - native:N4} {work.Currency}"; } }
        amount.TextChanged += (_, _) => Preview(); rate.TextChanged += (_, _) => Preview(); currency.SelectionChanged += (_, _) => Preview(); Preview(); string key = Guid.NewGuid().ToString();
        dialog.OnSubmit(async () =>
        {
            if (!WorkspaceUi.Number(amount.Text, out var a) || !WorkspaceUi.Number(rate.Text, out var r) || !date.SelectedDate.HasValue) throw new InvalidOperationException("أكمل القيمة والتاريخ وسعر الصرف.");
            using var db = _createContext(); await new ClinicTransactions(db).PayLabAsync(work.Id, work.Revision, a, currency.SelectedItem!.ToString()!, r, date.SelectedDate.Value, key);
            dialog.Complete(); Refresh(); OpenDetails(work.Id); Status.Text = "✓ تم تسجيل الدفعة والمصروف."; GlobalEvents.NotifyFinancialRecordAdded(); FinanceMotion.Highlight(WorksGrid, work.Id);
        }); dialog.ShowDialog();
    }
    void TimelineExpense_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is LabTimeline { ExpenseId: int id }) { CloseSide(); WorkspaceUi.OpenExpense(this, id); } }
    void AddLab_Click(object sender, RoutedEventArgs e) => EditLab(null);
    void ManageLabs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ClinicDialog(this, "إدارة المخابر", "تعديل المخبر المحدد");
        var choices = dialog.Choice("المخابر · يشمل المؤرشفة", _labs.Select(l => l.Name + (l.IsArchived ? " · مؤرشف" : "")));
        dialog.OnSubmit(() => { int index = choices.SelectedIndex; dialog.Complete(); if (index >= 0) EditLab(_labs[index]); return Task.CompletedTask; });
        var add = new Button { Content = "إضافة مخبر", Style = (Style)FindResource("WorkspaceButton") }; add.Click += (_, _) => { dialog.Close(); EditLab(null); }; dialog.Fields.Children.Add(add); dialog.ShowDialog();
    }
    void EditLab(LabName? existing)
    {
        var dialog = new ClinicDialog(this, existing == null ? "إضافة مخبر" : "تعديل المخبر");
        var name = dialog.Input("اسم المخبر", existing?.Name ?? ""); var phone = dialog.Input("رقم التواصل", existing?.Phone ?? "", true);
        var archived = new CheckBox { Content = "أرشفة المخبر · تبقى الأعمال السابقة محفوظة", IsChecked = existing?.IsArchived ?? false, Margin = new(0, 6, 0, 12) }; archived.SetResourceReference(Control.ForegroundProperty, "AppTextPrimary"); dialog.Fields.Children.Add(archived);
        dialog.OnSubmit(async () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("أدخل اسم المخبر.");
            using var db = _createContext(); using var tx = await db.Database.BeginTransactionAsync();
            if ((await db.LabNames.AsNoTracking().ToListAsync()).Any(l => l.Id != existing?.Id && l.Name.Equals(name.Text.Trim(), StringComparison.CurrentCultureIgnoreCase))) throw new InvalidOperationException("اسم المخبر موجود مسبقاً.");
            var lab = existing == null ? new LabName() : await db.LabNames.SingleAsync(l => l.Id == existing.Id); string oldName = lab.Name;
            lab.Name = name.Text.Trim(); lab.Phone = phone.Text.Trim(); lab.IsArchived = archived.IsChecked == true;
            if (existing == null) db.LabNames.Add(lab); await db.SaveChangesAsync();
            if (existing != null) await db.LabWorks.Where(w => w.LabNameId == lab.Id || (w.LabNameId == null && w.LabName == oldName)).ExecuteUpdateAsync(s => s.SetProperty(w => w.LabName, lab.Name).SetProperty(w => w.LabNameId, (int?)lab.Id));
            await tx.CommitAsync(); dialog.Complete(); Refresh(); LabSearch.Clear(); LabName.ItemsSource = _labs.Where(l => !l.IsArchived).ToList(); LabName.SelectedItem = _labs.FirstOrDefault(l => l.Id == lab.Id); Status.Text = "✓ تم حفظ المخبر.";
        }); dialog.ShowDialog();
    }
}

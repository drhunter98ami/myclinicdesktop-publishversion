using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MyClinic.Models;

namespace MyClinic;

public partial class PatientRecordsView
{
    private readonly List<VisitHistoryItem> _allPatientVisits = new();
    private IInputElement? _patientReturnFocus;
    private IInputElement? _visitReturnFocus;
    private bool _loadFailed;
    private bool _visitsLoading;
    private bool _clearingFilters;
    private bool _experienceReady;
    private int _statusVersion;

    private void AddPatient_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow modern) modern.ShowAddPatient();
        else if (Window.GetWindow(this) is OriginalTheme.MainWindow original) original.ShowAddPatient();
    }
    private void ClearSearch_Click(object sender, RoutedEventArgs e) { TxtNameSearch.Clear(); TxtNameSearch.Focus(); }
    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        _clearingFilters = true;
        TxtNameSearch.Clear(); SortBox.SelectedIndex = VisitFilterBox.SelectedIndex = RecentFilterBox.SelectedIndex = 0;
        _clearingFilters = false; ApplyFilters(); TxtNameSearch.Focus();
    }
    private void PatientFilters_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_experienceReady && !_clearingFilters) ApplyFilters();
    }
    private void Density_Changed(object sender, RoutedEventArgs e) { PatientsListBox?.UpdateLayout(); }
    private void Spotlight_Changed(object sender, RoutedEventArgs e)
    {
        if (PatientRecordsSpotlightCanvas is not null) ApplySpotlightTheme(IsDarkThemeActive());
    }
    private void EmptyAction_Click(object sender, RoutedEventArgs e)
    {
        if (_loadFailed) RefreshPatientData();
        else if (_allPatients.Count == 0) AddPatient_Click(sender, e);
        else ClearFilters_Click(sender, e);
    }
    private void CopyPhone_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string phone } || string.IsNullOrWhiteSpace(phone)) return;
        try { Clipboard.SetText(phone); ShowRecordsStatus("تم نسخ رقم الهاتف ✓"); }
        catch (System.Runtime.InteropServices.COMException) { ShowRecordsStatus("تعذر النسخ حالياً. حاول مرة أخرى."); }
    }
    private void ShowRecordsStatus(string message)
    {
        int version = ++_statusVersion;
        RecordsStatus.Text = message;
        RecordsStatus.Visibility = Visibility.Visible;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) => { timer.Stop(); if (_statusVersion == version) { RecordsStatus.Text = ""; RecordsStatus.Visibility = Visibility.Collapsed; } };
        timer.Start();
    }
    private void RenderPatientFilters(int count)
    {
        ResultSummary.Children.Clear();
        var text = new TextBlock { Text = $"{count:N0} من {_allPatients.Count:N0} مريض", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 4) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "AppTextSecondary");
        ResultSummary.Children.Add(text);
        void Chip(string label, Action clear)
        {
            var button = new Button { Content = label + "  ×", Style = (Style)FindResource("RecordButton"), Height = 30, MaxWidth = 250 };
            System.Windows.Automation.AutomationProperties.SetName(button, "إزالة تصفية " + label);
            button.Click += (_, _) => clear(); ResultSummary.Children.Add(button);
        }
        if (TxtNameSearch.Text.Trim().Length > 0) Chip("البحث: " + TxtNameSearch.Text.Trim(), () => TxtNameSearch.Clear());
        if (VisitFilterBox.SelectedIndex > 0) Chip(((ComboBoxItem)VisitFilterBox.SelectedItem).Content.ToString()!, () => VisitFilterBox.SelectedIndex = 0);
        if (RecentFilterBox.SelectedIndex > 0) Chip(((ComboBoxItem)RecentFilterBox.SelectedItem).Content.ToString()!, () => RecentFilterBox.SelectedIndex = 0);
    }
    private void VisitFilters_Changed(object sender, TextChangedEventArgs e) { if (_experienceReady && !_clearingFilters) ApplyVisitFilters(); }
    private void VisitDate_Changed(object? sender, SelectionChangedEventArgs e) { if (_experienceReady && !_clearingFilters) ApplyVisitFilters(); }
    private void ClearVisitFilters_Click(object sender, RoutedEventArgs e)
    {
        _clearingFilters = true;
        VisitSearchBox.Clear(); VisitFromDate.SelectedDate = VisitToDate.SelectedDate = null;
        _clearingFilters = false; ApplyVisitFilters();
    }
    private void ApplyVisitFilters()
    {
        if (VisitsListBox is null || _visitsLoading) return;
        string query = PatientRecordSearch.Normalize(VisitSearchBox.Text);
        DateTime? from = VisitFromDate.SelectedDate, to = VisitToDate.SelectedDate;
        bool invalidRange = from.HasValue && to.HasValue && from > to;
        using var state = ScrollState.Updating(VisitsListBox, query + from + "/" + to);
        _selectedPatientVisits.ReplaceAll(_allPatientVisits.Where(v => !invalidRange &&
            (!from.HasValue || v.VisitDate.Date >= from.Value.Date) && (!to.HasValue || v.VisitDate.Date <= to.Value.Date) &&
            (query.Length == 0 || PatientRecordSearch.Normalize(string.Join(" ", v.Title, v.Details, v.DiagnosisText, v.SymptomsText, v.TreatmentPlanText, v.FinalTreatmentText)).Contains(query))));
        VisitCounterBadge.Text = $"{_selectedPatientVisits.Count} / {_allPatientVisits.Count}";
        EmptyVisitsText.Text = invalidRange ? "تاريخ البداية يجب أن يسبق تاريخ النهاية." : _allPatientVisits.Count == 0 ? "لا توجد زيارات محفوظة لهذا المريض حتى الآن." : "لا توجد زيارات مطابقة. جرّب مسح التصفية.";
        EmptyVisitsText.Visibility = _selectedPatientVisits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RetryVisitsButton.Visibility = Visibility.Collapsed;
    }
    private async void RetryVisits_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPatient is not null) await LoadPatientVisitsAsync(_selectedPatient.Id);
    }
    private void Records_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (MedicalRecordDrawer is null) return;
        MedicalRecordDrawer.Width = Math.Min(500, Math.Max(260, ActualWidth - 24));
        VisitDetailsModal.Width = Math.Min(900, Math.Max(280, ActualWidth - 48));
        VisitDetailsModal.MaxHeight = Math.Max(240, ActualHeight - 48);
    }
    private void Records_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && VisitDetailsOverlay.Visibility != Visibility.Visible)
        {
            TextBox search = MedicalRecordOverlay.Visibility == Visibility.Visible ? VisitSearchBox : TxtNameSearch;
            search.Focus(); search.SelectAll(); e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (VisitDetailsOverlay.Visibility == Visibility.Visible) HideVisitDetailsOverlay();
            else if (MedicalRecordOverlay.Visibility == Visibility.Visible) HideMedicalRecordDrawer();
            else TxtNameSearch.Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && MedicalRecordOverlay.Visibility != Visibility.Visible && e.OriginalSource is not Button)
        {
            _searchDebounceTimer?.Stop(); ApplyFilters();
            var patient = PatientsListBox.SelectedItem as PatientCardModel ?? _filteredPatients.FirstOrDefault();
            if (patient is not null) { OpenMedicalRecord_Click(new Button { Tag = patient }, e); e.Handled = true; }
        }
    }
    private void VisitTabs_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, VisitTabs) || VisitTabs.SelectedContent is not UIElement content) return;
        content.BeginAnimation(OpacityProperty, new DoubleAnimation(.55, 1, MotionPreferences.Duration(110)));
    }
    private void RecordedTooth_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id } || VisitDetailsModal.DataContext is not VisitDetailsViewModel vm) return;
        var treatments = vm.SelectedTreatments.Where(t => t.ToothId == id || t.ToothDetails.Any(d => d.ToothId == id));
        string names = string.Join("، ", treatments.Select(t => t.TreatmentName));
        ToothTreatmentHint.Text = $"السن {id}: " + (names.Length > 0 ? names : vm.Teeth.Contains(id) ? "مسجل لهذه الزيارة دون علاج مرتبط محدد." : "غير مسجل ضمن هذه الزيارة.");
    }
    private void OpenImageViewer_Click(object sender, RoutedEventArgs e)
    {
        if (VisitDetailsModal.DataContext is not VisitDetailsViewModel vm) return;
        if (vm.IsLoadingAttachments) { ShowRecordsStatus("جارٍ تجهيز الصور…"); return; }
        if (vm.AttachmentItems.Count == 0) return;
        new PatientImageViewer(vm.AttachmentItems, vm.AttachmentItems[0].FilePath, vm.PatientName) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
    private void PrintVisit_Click(object sender, RoutedEventArgs e)
    {
        if (VisitDetailsModal.DataContext is not VisitDetailsViewModel vm) return;
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        var doc = new FlowDocument { FlowDirection = FlowDirection.RightToLeft, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, PagePadding = new Thickness(40), ColumnWidth = double.PositiveInfinity, PageWidth = dialog.PrintableAreaWidth, PageHeight = dialog.PrintableAreaHeight };
        void Add(string title, string value) { doc.Blocks.Add(new Paragraph(new Bold(new Run(title))) { Margin = new Thickness(0, 12, 0, 4) }); doc.Blocks.Add(new Paragraph(new Run(value)) { Margin = new Thickness(0, 0, 0, 6) }); }
        Add("ملخص الزيارة", vm.PatientName + " · " + vm.VisitDateTimeText);
        Add("معلومات المريض الحالية", $"الحساسية: {vm.AllergiesText}\nالأمراض المزمنة: {vm.ChronicDiseasesText}");
        Add("الشكوى والتشخيص", vm.SymptomsText + "\n" + vm.DiagnosisText);
        Add("القياسات المسجلة وقت الزيارة", $"ضغط الدم: {vm.BloodPressureText} · النبض: {vm.HeartRateText} · الحرارة: {vm.TemperatureText}\nالتنفس: {vm.RespiratoryRateText} · الوزن: {vm.WeightText} · الطول: {vm.HeightText}\nسكر الدم: {vm.BloodSugarText} · BMI: {vm.BmiText}");
        Add("العلاج", vm.TreatmentPlanText + "\n" + vm.FinalTreatmentText);
        Add("الأسنان والعلاجات المحددة", string.Join("\n", vm.SelectedTreatments.Select(t => $"{t.TreatmentName} · {t.DetailsDisplay} · {t.Quantity} × {t.Cost:0.##} {t.Currency}")));
        Add("الوصفة الطبية", vm.PrescriptionItems.Count == 0 ? "لا توجد وصفة مسجلة." : string.Join("\n", vm.PrescriptionItems.Select(p => p.MedicineName + "\n" + p.InstructionText)));
        try { dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "ملخص زيارة"); }
        catch (Exception ex) when (ex is System.Printing.PrintSystemException or InvalidOperationException) { ShowRecordsStatus("تعذر إكمال الطباعة. تحقق من الطابعة ثم حاول مرة أخرى."); }
    }
}

public static class PatientRecordSearch
{
    public static string Normalize(string? value)
    {
        var result = new StringBuilder();
        foreach (char c in (value ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || c == '\u0640') continue;
            if (c is >= '\u0660' and <= '\u0669') result.Append((char)('0' + c - '\u0660'));
            else if (c is >= '\u06f0' and <= '\u06f9') result.Append((char)('0' + c - '\u06f0'));
            else if (char.IsLetterOrDigit(c)) result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }
    public static (int Start, int Length) FindMatch(string text, string query, int offset)
    {
        string target = Normalize(query);
        if (target.Length == 0) return (-1, 0);
        var normalized = new StringBuilder();
        var positions = new List<int>();
        for (int i = offset; i < text.Length; i++)
        {
            string piece = Normalize(text[i].ToString());
            normalized.Append(piece);
            for (int j = 0; j < piece.Length; j++) positions.Add(i);
        }
        int match = normalized.ToString().IndexOf(target, StringComparison.Ordinal);
        return match < 0 ? (-1, 0) : (positions[match], positions[match + target.Length - 1] - positions[match] + 1);
    }
    public static IReadOnlyList<PatientCardModel> Filter(IEnumerable<PatientCardModel> patients, string query, int visitFilter, int recentFilter, int sort, DateTime today)
    {
        string normalized = Normalize(query);
        int days = recentFilter switch { 1 => 30, 2 => 90, 3 => 365, _ => 0 };
        var filtered = patients.Where(p =>
            (normalized.Length == 0 || Normalize(p.DisplayName).Contains(normalized) || Normalize(p.PhoneNumber).Contains(normalized) || Normalize(p.Id.ToString()).Equals(normalized)) &&
            (visitFilter != 1 || p.VisitCount > 0) && (visitFilter != 2 || p.VisitCount == 0) &&
            (days == 0 || p.VisitCount > 0 && p.LatestVisitDate.Date >= today.AddDays(1 - days) && p.LatestVisitDate.Date <= today));
        return sort switch
        {
            1 => filtered.OrderBy(p => p.DisplayName, StringComparer.CurrentCulture).ThenBy(p => p.Id).ToList(),
            2 => filtered.OrderByDescending(p => p.Id).ToList(),
            _ => filtered.OrderByDescending(p => p.LatestVisitDate).ThenBy(p => p.DisplayName, StringComparer.CurrentCulture).ThenBy(p => p.Id).ToList()
        };
    }
}

public sealed record RecordedTooth(string Id, bool Recorded)
{
    public string Description => ToothGlyph.Description(Id) + (Recorded ? " · مسجل لهذه الزيارة" : " · غير مسجل لهذه الزيارة");
}

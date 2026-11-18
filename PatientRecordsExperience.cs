using Microsoft.EntityFrameworkCore;
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

    private async void DeletePatientProfile_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { Tag: PatientCardModel patient })
            return;

        if (!await PatientProfileDeletionService.ConfirmAndDeletePatientAsync(this, patient.Id, patient.DisplayName))
            return;

        if (_selectedPatient?.Id == patient.Id)
        {
            HideVisitDetailsOverlay(immediate: true);
            HideMedicalRecordDrawer();
            _selectedPatient = null;
        }
        RequestRefresh();
        await EnsureDataCurrentAsync();
        ShowRecordsStatus("تم حذف ملف المريض بنجاح ✓");
    }

    private async void DeleteSelectedPatientProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPatient is null)
            return;

        var patient = _selectedPatient;
        if (!await PatientProfileDeletionService.ConfirmAndDeletePatientAsync(this, patient.Id, patient.DisplayName))
            return;

        HideVisitDetailsOverlay(immediate: true);
        HideMedicalRecordDrawer();
        _selectedPatient = null;
        RequestRefresh();
        await EnsureDataCurrentAsync();
        ShowRecordsStatus("تم حذف ملف المريض بنجاح ✓");
    }

    private async void DeleteVisit_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        int? visitId = sender switch
        {
            FrameworkElement { Tag: int id } => id,
            FrameworkElement { Tag: VisitHistoryItem item } => item.VisitId,
            _ => null
        };
        if (!visitId.HasValue || _selectedPatient is null)
            return;

        var targetVisit = _allPatientVisits.FirstOrDefault(v => v.VisitId == visitId.Value);
        string label = targetVisit != null ? $"{targetVisit.VisitDateTimeText} · {targetVisit.Title}" : $"#{visitId.Value}";
        if (!await PatientProfileDeletionService.ConfirmAndDeleteVisitAsync(this, visitId.Value, label))
            return;

        HideVisitDetailsOverlay(immediate: true);
        _attachmentCache.Remove(visitId.Value);
        RequestRefresh();
        await EnsureDataCurrentAsync();
        if (_selectedPatient is not null)
            await LoadPatientVisitsAsync(_selectedPatient.Id);
        ShowRecordsStatus("تم حذف الزيارة بنجاح ✓");
    }

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

public static class PatientProfileDeletionService
{
    public static Task<bool> ConfirmAndDeletePatientAsync(FrameworkElement owner, int patientId, string patientName)
    {
        bool deleted = false;
        var dialog = new ClinicDialog(owner, "حذف ملف المريض", "تأكيد حذف الملف");
        dialog.Note($"هل أنت متأكد من حذف ملف المريض ({patientName}) وجميع زياراته السريرية؟");
        var deleteFinancials = new CheckBox
        {
            Content = "حذف جميع البيانات المالية المرتبطة بهذا الملف",
            IsChecked = true,
            Margin = new Thickness(0, 6, 0, 14),
            FontWeight = FontWeights.SemiBold
        };
        deleteFinancials.SetResourceReference(Control.ForegroundProperty, "AppTextPrimary");
        dialog.Fields.Children.Add(deleteFinancials);
        dialog.Note("عند إلغاء تحديد الخيار أعلاه، يُحذف الملف السريري والزيارات من سجل المرضى مع الاحتفاظ بالسجلات والدفعات المالية في الشؤون المالية.");
        dialog.Submit.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
        dialog.Submit.BorderBrush = dialog.Submit.Background;
        dialog.Submit.Foreground = Brushes.White;

        dialog.OnSubmit(async () =>
        {
            bool removeFinance = deleteFinancials.IsChecked == true;
            using var db = new AppDbContext();
            using var tx = await db.Database.BeginTransactionAsync();
            var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patientId);
            if (patient == null)
            {
                dialog.Complete();
                return;
            }

            var visits = await db.Visits.AsNoTracking().Where(v => v.PatientId == patientId).ToListAsync();
            var visitIds = visits.Select(v => v.Id).ToList();

            await db.Appointments
                .Where(a => a.PatientId == patientId || (a.PatientId == null && a.PatientName == patient.FullName && a.PhoneNumber == patient.PhoneNumber))
                .ExecuteDeleteAsync();

            if (removeFinance)
            {
                await db.LabWorks
                    .Where(w => w.PatientId == patientId)
                    .ExecuteUpdateAsync(s => s.SetProperty(w => w.PatientId, (int?)null));

                if (visitIds.Count > 0)
                {
                    await db.FinancialAuditEntries.Where(a => a.EntityType == "Visit" && visitIds.Contains(a.EntityId)).ExecuteDeleteAsync();
                    await db.ToothRecords.Where(t => visitIds.Contains(t.VisitId)).ExecuteDeleteAsync();
                    await db.VisitEditHistories.Where(h => visitIds.Contains(h.VisitId)).ExecuteDeleteAsync();
                    await db.Visits.Where(v => v.PatientId == patientId).ExecuteDeleteAsync();
                }

                await db.Patients.Where(p => p.Id == patientId).ExecuteDeleteAsync();
            }
            else
            {
                var nonFinancialVisitIds = visits
                    .Where(v => v.CurrentCost == 0 && v.TodayPaid == 0)
                    .Select(v => v.Id)
                    .ToList();
                if (nonFinancialVisitIds.Count > 0)
                {
                    await db.ToothRecords.Where(t => nonFinancialVisitIds.Contains(t.VisitId)).ExecuteDeleteAsync();
                    await db.VisitEditHistories.Where(h => nonFinancialVisitIds.Contains(h.VisitId)).ExecuteDeleteAsync();
                    await db.Visits.Where(v => nonFinancialVisitIds.Contains(v.Id)).ExecuteDeleteAsync();
                }

                var financialVisits = visits.Where(v => v.CurrentCost != 0 || v.TodayPaid != 0).ToList();
                if (financialVisits.Count > 0)
                {
                    var finIds = financialVisits.Select(v => v.Id).ToList();
                    await db.ToothRecords.Where(t => finIds.Contains(t.VisitId)).ExecuteDeleteAsync();
                    await db.VisitEditHistories.Where(h => finIds.Contains(h.VisitId)).ExecuteDeleteAsync();
                    await db.Visits
                        .Where(v => finIds.Contains(v.Id))
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(v => v.IsFinancialOnly, true)
                            .SetProperty(v => v.Symptoms, (string?)null)
                            .SetProperty(v => v.Diagnosis, (string?)null)
                            .SetProperty(v => v.PrescriptionJson, (string?)null)
                            .SetProperty(v => v.TreatmentPlanNotes, (string?)null)
                            .SetProperty(v => v.FinalTreatment, (string?)null)
                            .SetProperty(v => v.AttachedImagePathsJson, (string?)null)
                            .SetProperty(v => v.FinancialNote, "بيانات مالية لملف مريض محذوف"));

                    await db.Patients
                        .Where(p => p.Id == patientId)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDeleted, true));
                }
                else
                {
                    await db.LabWorks
                        .Where(w => w.PatientId == patientId)
                        .ExecuteUpdateAsync(s => s.SetProperty(w => w.PatientId, (int?)null));
                    await db.Patients.Where(p => p.Id == patientId).ExecuteDeleteAsync();
                }
            }

            await tx.CommitAsync();
            AppRuntimeCache.RemovePatientVisits(patientId);
            deleted = true;
            dialog.Complete();
            GlobalEvents.NotifyPatientRecordAdded();
            GlobalEvents.NotifyFinancialRecordAdded();
            if (Window.GetWindow(owner) is MainWindow mainWindow)
            {
                mainWindow.InvalidatePatientRecords();
                mainWindow.InvalidateFinancialRecords();
            }
        });

        dialog.ShowDialog();
        return Task.FromResult(deleted);
    }

    public static Task<bool> ConfirmAndDeleteVisitAsync(FrameworkElement owner, int visitId, string visitLabel)
    {
        bool deleted = false;
        var dialog = new ClinicDialog(owner, "حذف الزيارة", "تأكيد حذف الزيارة");
        dialog.Note($"هل أنت متأكد من حذف الزيارة ({visitLabel})؟");
        var deleteFinancials = new CheckBox
        {
            Content = "حذف جميع البيانات المالية المرتبطة بهذه الزيارة",
            IsChecked = true,
            Margin = new Thickness(0, 6, 0, 14),
            FontWeight = FontWeights.SemiBold
        };
        deleteFinancials.SetResourceReference(Control.ForegroundProperty, "AppTextPrimary");
        dialog.Fields.Children.Add(deleteFinancials);
        dialog.Note("عند إلغاء تحديد الخيار أعلاه، تُحذف البيانات السريرية للزيارة من سجل المريض مع الاحتفاظ بتكلفتها ودفعاتها في الشؤون المالية.");
        dialog.Submit.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
        dialog.Submit.BorderBrush = dialog.Submit.Background;
        dialog.Submit.Foreground = Brushes.White;

        dialog.OnSubmit(async () =>
        {
            bool removeFinance = deleteFinancials.IsChecked == true;
            using var db = new AppDbContext();
            using var tx = await db.Database.BeginTransactionAsync();
            var visit = await db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visitId);
            if (visit == null)
            {
                dialog.Complete();
                return;
            }

            await db.Appointments
                .Where(a => a.IsAutomaticallyGenerated && a.PatientId == visit.PatientId && a.AppointmentDateTime == visit.VisitDate)
                .ExecuteDeleteAsync();

            await db.ToothRecords.Where(t => t.VisitId == visitId).ExecuteDeleteAsync();
            await db.VisitEditHistories.Where(h => h.VisitId == visitId).ExecuteDeleteAsync();

            if (removeFinance || (visit.CurrentCost == 0 && visit.TodayPaid == 0))
            {
                await db.FinancialAuditEntries.Where(a => a.EntityType == "Visit" && a.EntityId == visitId).ExecuteDeleteAsync();
                await db.Visits.Where(v => v.Id == visitId).ExecuteDeleteAsync();
                AppRuntimeCache.RemoveVisit(visitId);
            }
            else
            {
                string note = string.IsNullOrWhiteSpace(visit.FinancialNote)
                    ? $"بيانات مالية لزيارة محذوفة ({visit.VisitDate:dd/MM/yyyy})"
                    : visit.FinancialNote;
                await db.Visits
                    .Where(v => v.Id == visitId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(v => v.IsFinancialOnly, true)
                        .SetProperty(v => v.Symptoms, (string?)null)
                        .SetProperty(v => v.Diagnosis, (string?)null)
                        .SetProperty(v => v.PrescriptionJson, (string?)null)
                        .SetProperty(v => v.TreatmentPlanNotes, (string?)null)
                        .SetProperty(v => v.FinalTreatment, (string?)null)
                        .SetProperty(v => v.AttachedImagePathsJson, (string?)null)
                        .SetProperty(v => v.FinancialNote, note));
                AppRuntimeCache.RemoveVisit(visitId);
            }

            await tx.CommitAsync();
            deleted = true;
            dialog.Complete();
            GlobalEvents.NotifyPatientRecordAdded();
            GlobalEvents.NotifyFinancialRecordAdded();
            if (Window.GetWindow(owner) is MainWindow mainWindow)
            {
                mainWindow.InvalidatePatientRecords();
                mainWindow.InvalidateFinancialRecords();
            }
        });

        dialog.ShowDialog();
        return Task.FromResult(deleted);
    }
}

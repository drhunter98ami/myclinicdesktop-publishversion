using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MyClinic.OriginalTheme;

public partial class AddPatientView : IRecoverableClinicForm
{
    private readonly Func<AppDbContext> _createContext;
    private readonly bool _draftsEnabled;
    private readonly bool _medicinePersistenceEnabled;
    private readonly string? _draftPathOverride;
    private bool _workflowReady, _suspendDraft, _frequentOnly, _draftReadFailed;
    private string _baseline = string.Empty, _lastSavedDraft = string.Empty;
    private Task _treatmentsTask = Task.CompletedTask;
    private readonly DispatcherTimer _draftTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private ICollectionView? _treatmentView;
    private readonly HashSet<int> _frequentTreatmentIds = new();
    private readonly Dictionary<int, int> _treatmentFrequency = new();
    private string _currentSection = "PatientSection";
    private string? _activeTooth;
    private bool _visitCommitted;
    private DateTime? _legacyBirthDate;

    private void InitializeWorkflow()
    {
        _treatmentView = CollectionViewSource.GetDefaultView(_treatmentSelectionItems);
        _treatmentView.Filter = o => o is TreatmentSelectionItem t && (t.IsSelected ||
            (!_frequentOnly || _frequentTreatmentIds.Contains(t.TreatmentId)) &&
            t.TreatmentName.Contains(TxtTreatmentSearch.Text.Trim(), StringComparison.CurrentCultureIgnoreCase));
        TreatmentItemsControl.ItemsSource = _treatmentView;
        _draftTimer.Tick += (_, _) => { _draftTimer.Stop(); PreserveDraft(); };
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(FormInputChanged));
        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => ScheduleDraft()));
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(ToothOrCheckChanged));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(ToothOrCheckChanged));
        FormSections.IsEnabled = false;
        BtnSaveVisit.IsEnabled = BtnSaveDraft.IsEnabled = false;
        foreach (var label in LogicalDescendants<TextBlock>(this).Where(t => t.Style == Resources["FormLabel"]))
        {
            var parent = LogicalTreeHelper.GetParent(label);
            if (parent != null)
            {
                var control = LogicalTreeHelper.GetChildren(parent).OfType<Control>().FirstOrDefault();
                if (control != null) AutomationProperties.SetLabeledBy(control, label);
            }
        }
    }

    private async Task StartWorkflowAsync()
    {
        await _treatmentsTask;
        if (!IsLoaded) return;
        _suspendDraft = true;
        _baseline = Fingerprint(CaptureDraft());
        _workflowReady = true;
        try
        {
            var draft = _draftsEnabled ? PatientDraftStore.LoadAt(_draftPathOverride ?? PatientDraftStore.DraftPath) : null;
            if (draft != null)
            {
                RestoreDraft(draft);
                _lastSavedDraft = Fingerprint(CaptureDraft());
                DraftBanner.Visibility = Visibility.Visible;
                TxtDraftStatus.Text = $"تم استرجاع مسودة {draft.SavedAt:dd/MM/yyyy HH:mm} · لم تُحفظ كزيارة بعد";
            }
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            _draftReadFailed = true;
            DraftBanner.Visibility = Visibility.Visible;
            TxtDraftStatus.Text = "تعذر استرجاع المسودة. احذف المسودة التالفة هنا قبل حفظ مسودة جديدة.";
        }
        finally { _suspendDraft = false; }
        UpdateIdentity(); UpdateSelectedTreatmentSummary(); UpdateAllergyAppearance(); UpdateChronicDiseaseAppearance(); SetActiveSection("PatientSection");
        FormSections.IsEnabled = true;
        BtnSaveVisit.IsEnabled = BtnSaveDraft.IsEnabled = true;
        TxtFullName.Focus();
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found) yield return found;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in LogicalDescendants<T>(child)) yield return nested;
    }
    private PatientFormDraft CaptureDraft()
    {
        var result = new PatientFormDraft
        {
            PatientId = _selectedPatientLookup?.Id, DateOfBirth = _legacyBirthDate, BirthYear = ReadBirthYear(),
            Treatments = GetSelectedTreatments(), Prescription = _prescriptionItems.ToList(),
            Attachments = _selectedImagePaths.ToList(), AdultTeeth = AdultChartControl.GetSelectedTeeth().Order().ToList(),
            ChildTeeth = ChildChartControl.GetSelectedTeeth().Order().ToList(), ChildChart = ChildChartControl.Visibility == Visibility.Visible
        };
        foreach (var text in LogicalDescendants<TextBox>(FormSections).Where(c => c.Name.Length > 0 && !c.IsReadOnly && c.Name != "TxtTreatmentSearch" && c.Name != "TxtQuantity"))
            result.Text[text.Name] = text.Text;
        foreach (var combo in LogicalDescendants<ComboBox>(FormSections).Where(c => c.Name.Length > 0)) result.Choices[combo.Name] = combo.SelectedIndex;
        foreach (var check in LogicalDescendants<CheckBox>(FormSections).Where(c => c.Name.Length > 0)) result.Checks[check.Name] = check.IsChecked == true;
        return result;
    }
    private static string Fingerprint(PatientFormDraft draft) { draft.SavedAt = default; return JsonSerializer.Serialize(draft); }
    private bool HasUnsavedChanges() => _workflowReady && Fingerprint(CaptureDraft()) != _baseline;
    private void ScheduleDraft()
    {
        if (!_workflowReady || _suspendDraft || !IsLoaded) return;
        _draftTimer.Stop(); _draftTimer.Start();
    }
    private void FormInputChanged(object sender, TextChangedEventArgs e)
    {
        if (!_workflowReady || _suspendDraft) return;
        ScheduleDraft();
        if (e.OriginalSource == TxtFullName || e.OriginalSource == TxtPhoneNumber || e.OriginalSource == TxtAllergies || e.OriginalSource == TxtChronicDiseases)
            UpdateIdentity();
    }
    private void ToothOrCheckChanged(object sender, RoutedEventArgs e)
    {
        ScheduleDraft();
        if (e.OriginalSource is ToggleButton tooth && int.TryParse(tooth.Content?.ToString(), out int id) && ToothGlyph.IsValidId(id))
        {
            if (tooth.IsChecked == true)
            {
                _activeTooth = id.ToString(CultureInfo.InvariantCulture);
                TxtSelectedTooth.Text = ToothGlyph.Description(id.ToString(CultureInfo.InvariantCulture));
                FormMotion.Reveal(ToothSelectionHint);
            }
            else if (_activeTooth == id.ToString(CultureInfo.InvariantCulture))
            {
                _activeTooth = AdultChartControl.GetSelectedTeeth().Concat(ChildChartControl.GetSelectedTeeth()).LastOrDefault();
                TxtSelectedTooth.Text = _activeTooth == null ? "اختر السن ثم حدّد العلاج المناسب له." : ToothGlyph.Description(_activeTooth);
            }
        }
    }

    public bool PreserveDraft()
    {
        if (!_draftsEnabled || !_workflowReady || _suspendDraft) return true;
        if (!HasUnsavedChanges())
        {
            if (_lastSavedDraft.Length > 0 && !DeleteDraft()) return false;
            if (!_draftReadFailed) DraftBanner.Visibility = Visibility.Collapsed;
            return true;
        }
        if (_draftReadFailed) return false;
        try
        {
            var draft = CaptureDraft(); string fingerprint = Fingerprint(draft);
            if (fingerprint == _lastSavedDraft) return true;
            draft.SavedAt = AppClock.Now; PatientDraftStore.SaveAt(_draftPathOverride ?? PatientDraftStore.DraftPath, draft); _lastSavedDraft = fingerprint;
            DraftBanner.Visibility = Visibility.Visible;
            TxtDraftStatus.Text = $"حُفظت المسودة محلياً {draft.SavedAt:HH:mm} · لم تُحفظ كزيارة بعد";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            DraftBanner.Visibility = Visibility.Visible;
            TxtDraftStatus.Text = "تعذر حفظ المسودة. تبقى البيانات في هذه الصفحة؛ أعد المحاولة قبل الإغلاق.";
            return false;
        }
    }
    private void SaveDraft_Click(object sender, RoutedEventArgs e)
    {
        if (PreserveDraft()) UiFeedback.Notify(this, HasUnsavedChanges() ? "تم حفظ المسودة على هذا الجهاز." : "لا توجد تغييرات لحفظها.");
        else UiFeedback.Notify(this, "تعذر حفظ المسودة؛ تحقق من مساحة القرص والصلاحيات.");
    }
    private bool DeleteDraft()
    {
        try
        {
            if (_draftsEnabled)
            {
                string path = _draftPathOverride ?? PatientDraftStore.DraftPath;
                if (File.Exists(path)) File.Delete(path);
            }
            _lastSavedDraft = ""; _draftReadFailed = false; return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { UiFeedback.Notify(this, "تعذر حذف المسودة. أعد المحاولة قبل بدء نموذج آخر."); return false; }
    }
    private void DiscardDraft_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("حذف المسودة والتغييرات الحالية؟", "المسودة", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (!DeleteDraft()) return;
        ResetWorkflowForm();
    }
    private void ResetWorkflowForm()
    {
        _draftTimer.Stop(); _suspendDraft = true;
        try
        {
            ResetForm(); _legacyBirthDate = null; TxtBirthYear.Clear(); CmbAllergyStatus.SelectedIndex = 0; CmbChronicDiseaseStatus.SelectedIndex = 0;
            _activeTooth = null;
            _visitCommitted = false;
            TxtSelectedTooth.Text = "اختر السن ثم حدّد العلاج المناسب له.";
            TxtTreatmentSearch.Clear(); _frequentOnly = false; _treatmentView?.Refresh();
            UpdateIdentity(); DraftBanner.Visibility = Visibility.Collapsed;
            _baseline = Fingerprint(CaptureDraft()); _lastSavedDraft = "";
        }
        finally { _suspendDraft = false; }
    }
    private void RestoreDraft(PatientFormDraft draft)
    {
        if (draft.PatientId.HasValue)
        {
            using var db = _createContext();
            var p = db.Patients.AsNoTracking().FirstOrDefault(p => p.Id == draft.PatientId.Value);
            if (p == null) throw new InvalidDataException("The draft's patient no longer exists.");
            _selectedPatientLookup = Snapshot(p); LoadPreviousVisits(_selectedPatientLookup);
        }
        _isApplyingPatientLookup = true;
        try
        {
            foreach (var (name, value) in draft.Text) if (FindName(name) is TextBox text) text.Text = value;
            foreach (var (name, index) in draft.Choices) if (FindName(name) is ComboBox combo) combo.SelectedIndex = index;
            foreach (var (name, value) in draft.Checks) if (FindName(name) is CheckBox check) check.IsChecked = value;
            _legacyBirthDate = draft.DateOfBirth;
            if (!draft.Text.ContainsKey(nameof(TxtBirthYear)))
                TxtBirthYear.Text = (draft.BirthYear ?? draft.DateOfBirth?.Year)?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            if (!draft.Choices.ContainsKey(nameof(CmbChronicDiseaseStatus)))
                CmbChronicDiseaseStatus.SelectedIndex = ClinicInput.ChronicDiseaseState(TxtChronicDiseases.Text);
            UpdateChronicDiseaseAppearance();
            foreach (var saved in draft.Treatments)
            {
                var t = _treatmentSelectionItems.FirstOrDefault(t => t.TreatmentId == saved.TreatmentId);
                if (t == null)
                {
                    t = new TreatmentSelectionItem { TreatmentId = saved.TreatmentId, TreatmentName = saved.TreatmentName, Cost = saved.Cost, Currency = saved.Currency };
                    _treatmentSelectionItems.Add(t); SubscribeTreatment(t);
                }
                t.IsSelected = true; t.Quantity = Math.Max(1, saved.Quantity); t.ToothDetails.Clear();
                foreach (var d in saved.ToothDetails) t.ToothDetails.Add(d);
                if (t.ToothDetails.Count == 0) t.ToothDetails.Add(new TreatmentToothDetail { ToothId = saved.ToothId, IsRootCanal = saved.IsRootCanal, CanalCount = saved.CanalCount });
            }
            _prescriptionItems.Clear(); _prescriptionItems.AddRange(draft.Prescription);
            _selectedImagePaths.Clear(); _selectedImagePaths.AddRange(draft.Attachments);
            foreach (var id in draft.AdultTeeth) AdultChartControl.SelectTooth(id);
            foreach (var id in draft.ChildTeeth) ChildChartControl.SelectTooth(id);
            AdultChartControl.Visibility = draft.ChildChart ? Visibility.Collapsed : Visibility.Visible;
            ChildChartControl.Visibility = draft.ChildChart ? Visibility.Visible : Visibility.Collapsed;
            // Restore the manually entered cost after treatment-control events have run.
            if (draft.Text.TryGetValue(nameof(TxtCurrentCost), out var cost)) TxtCurrentCost.Text = cost;
            RefreshPrescriptionList(); RefreshSelectedImagesList(); UpdateChartModeButtons();
            _treatmentView?.Refresh();
        }
        finally { _isApplyingPatientLookup = false; }
    }

    private static PatientLookupSnapshot Snapshot(Patient p) => new()
    {
        Id = p.Id, FullName = p.FullName, PhoneNumber = p.PhoneNumber, Age = p.Age, DateOfBirth = p.DateOfBirth, BirthYear = p.BirthYear,
        Gender = p.Gender, BloodType = p.BloodType, IsSmoker = p.IsSmoker, SmokingType = p.SmokingType,
        SmokingFrequency = p.SmokingFrequency, Allergies = p.Allergies, ChronicDiseases = p.ChronicDiseases
    };
    private int? ReadBirthYear() => int.TryParse(ClinicInput.NormalizeDigits(TxtBirthYear.Text), out int year) ? year : null;
    private void BirthYear_Changed(object sender, TextChangedEventArgs e)
    {
        if (TxtAge == null || TxtBirthYear == null) return;
        int? year = ReadBirthYear();
        bool valid = year >= AppClock.Today.Year - 130 && year <= AppClock.Today.Year;
        TxtAge.IsReadOnly = valid;
        if (valid) TxtAge.Text = (AppClock.Today.Year - year!.Value).ToString(CultureInfo.InvariantCulture);
        ScheduleDraft();
    }
    private void ChronicDiseaseStatus_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TxtChronicDiseases == null) return;
        UpdateChronicDiseaseAppearance(); UpdateIdentity(); ScheduleDraft();
    }
    private void UpdateChronicDiseaseAppearance()
    {
        if (CmbChronicDiseaseStatus == null || TxtChronicDiseases == null) return;
        bool specified = CmbChronicDiseaseStatus.SelectedIndex == 2;
        TxtChronicDiseases.Visibility = specified ? Visibility.Visible : Visibility.Collapsed;
        if (ChronicDiseaseCard != null)
        {
            ChronicDiseaseCard.SetResourceReference(Border.BorderBrushProperty, specified ? "VisitWarningCardBorder" : "VisitCardBorder");
            ChronicDiseaseCard.SetResourceReference(Border.BackgroundProperty, specified ? "VisitWarningCardBg" : "VisitCardBg");
        }
    }
    private void PreviousVisits_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FormScroll == null || e.Delta == 0) return;
        double distance = SystemParameters.WheelScrollLines < 0 ? FormScroll.ViewportHeight : SystemParameters.WheelScrollLines * 20d;
        FormScroll.ScrollToVerticalOffset(FormScroll.VerticalOffset - e.Delta / 120d * distance);
        e.Handled = true;
    }
    private void AllergyStatus_Changed(object sender, SelectionChangedEventArgs e)
    { if (TxtAllergies != null) { UpdateAllergyAppearance(); UpdateIdentity(); ScheduleDraft(); } }
    private void UpdateAllergyAppearance()
    {
        if (CmbAllergyStatus == null || TxtAllergies == null) return;
        TxtAllergies.Visibility = CmbAllergyStatus.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (AllergyCard != null)
        {
            AllergyCard.SetResourceReference(Border.BorderBrushProperty, CmbAllergyStatus.SelectedIndex == 2 ? "VisitWarningCardBorder" : "VisitCardBorder");
            AllergyCard.SetResourceReference(Border.BackgroundProperty, CmbAllergyStatus.SelectedIndex == 2 ? "VisitWarningCardBg" : "VisitCardBg");
        }
    }
    private void UpdateIdentity()
    {
        if (TxtIdentity == null || TxtFullName == null || CmbAllergyStatus == null || CmbChronicDiseaseStatus == null) return;
        string name = string.IsNullOrWhiteSpace(TxtFullName.Text) ? "مريض جديد" : TxtFullName.Text.Trim();
        TxtIdentity.Text = _selectedPatientLookup == null ? $"{name} · مريض جديد" : $"{name} · ملف #{_selectedPatientLookup.Id} · {ClinicInput.NormalizeDigits(TxtPhoneNumber.Text)}";
        string allergy = ClinicInput.AllergyValue(CmbAllergyStatus.SelectedIndex, TxtAllergies.Text) ?? "غير معروفة";
        string chronic = ClinicInput.ChronicDiseaseValue(CmbChronicDiseaseStatus.SelectedIndex, TxtChronicDiseases.Text) ?? "غير معروفة";
        TxtIdentityAlert.Text = $"الحساسية: {allergy} · الأمراض المزمنة: {chronic}";
        TxtIdentityAlert.SetResourceReference(TextBlock.ForegroundProperty, CmbAllergyStatus.SelectedIndex == 2 || CmbChronicDiseaseStatus.SelectedIndex == 2 ? "VisitWarningFg" : "AppTextSecondary");
    }
    private void ChangePatient_Click(object sender, RoutedEventArgs e)
    {
        if (HasUnsavedChanges() && MessageBox.Show("سيتم مسح النموذج الحالي لاختيار مريض آخر. احفظ المسودة أو الزيارة أولاً إذا أردت الاحتفاظ بها. متابعة؟", "تغيير المريض", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (!DeleteDraft()) return;
        ResetWorkflowForm(); JumpToSection("PatientSection"); TxtFullName.Focus();
    }
    private void SectionNavigation_Click(object sender, RoutedEventArgs e)
    { if (sender is Button { Tag: string section }) JumpToSection(section); }
    private void JumpToSection(string section)
    {
        if (FindName(section) is not AnimatedSection target) return;
        target.IsExpanded = true; target.UpdateLayout();
        new[] { NavPatient, NavMedical, NavClinical, NavPayment }.FirstOrDefault(b => b.Tag as string == section)?.Focus();
        FormScroll.ScrollToVerticalOffset(target.TranslatePoint(new Point(), FormSections).Y);
        SetActiveSection(section);
    }
    private void SetActiveSection(string section)
    {
        _currentSection = section;
        var buttons = new[] { NavPatient, NavMedical, NavClinical, NavPayment };
        foreach (var button in buttons)
        {
            bool active = button.Tag as string == section;
            button.SetResourceReference(Control.BackgroundProperty, active ? "VisitPrimaryBtnBg" : "VisitSubCardBg");
            button.SetResourceReference(Control.ForegroundProperty, active ? "VisitPrimaryBtnFg" : "AppTextPrimary");
        }
        var selected = buttons.FirstOrDefault(b => b.Tag as string == section);
        if (selected == null || !selected.IsLoaded) return;
        var parent = VisualTreeHelper.GetParent(selected) as FrameworkElement;
        if (parent == null) return;
        double x = selected.TranslatePoint(new Point(), parent).X;
        // The indicator is right-aligned in the RTL header.
        double destination = -(parent.ActualWidth - x - selected.ActualWidth);
        double from = SectionIndicator.X; SectionIndicator.BeginAnimation(TranslateTransform.XProperty, null); SectionIndicator.X = destination;
        if (MotionPreferences.Enabled) SectionIndicator.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(from, destination, MotionPreferences.Duration(180)) { FillBehavior = FillBehavior.Stop });
    }
    private void FormScroll_Changed(object sender, ScrollChangedEventArgs e)
    {
        if (!_workflowReady || Math.Abs(e.VerticalChange) < .01) return;
        string current = "PatientSection";
        foreach (var name in new[] { "PatientSection", "MedicalSection", "ClinicalSection", "PaymentSection" })
            if (FindName(name) is FrameworkElement section && section.TranslatePoint(new Point(), FormSections).Y <= FormScroll.VerticalOffset + 45) current = name;
        if (current != _currentSection) SetActiveSection(current);
    }
    private void TreatmentSearch_Changed(object sender, TextChangedEventArgs e) => _treatmentView?.Refresh();
    private void SelectTreatmentForTooth_Click(object sender, RoutedEventArgs e)
    {
        ClinicalSection.IsExpanded = true; ClinicalSection.UpdateLayout();
        FormScroll.ScrollToVerticalOffset(TreatmentCard.TranslatePoint(new Point(), FormSections).Y);
        TxtTreatmentSearch.Focus();
    }
    private void FrequentTreatments_Click(object sender, RoutedEventArgs e) { _frequentOnly = true; _treatmentView?.Refresh(); }
    private void AllTreatments_Click(object sender, RoutedEventArgs e) { _frequentOnly = false; TxtTreatmentSearch.Clear(); _treatmentView?.Refresh(); }
    private void SubscribeTreatment(TreatmentSelectionItem item)
    {
        item.PropertyChanged += (_, args) =>
        {
            ScheduleDraft(); UpdateSelectedTreatmentSummary();
            if (args.PropertyName == nameof(TreatmentSelectionItem.IsSelected)) Dispatcher.BeginInvoke(new Action(() => _treatmentView?.Refresh()));
        };
        item.ToothDetails.CollectionChanged += (_, _) => ScheduleDraft();
    }
    private void UpdateSelectedTreatmentSummary()
    {
        if (TxtSelectedTreatments == null) return;
        var selected = _treatmentSelectionItems.Where(t => t.IsSelected).ToList();
        TxtSelectedTreatments.Text = selected.Count == 0 ? "لم يتم اختيار علاج" :
            string.Join(" · ", selected.Select(t => $"{t.TreatmentName} × {Math.Max(1,t.Quantity)}"));
    }

    private bool ValidatePatientFields()
    {
        bool valid = ValidationAssist.Check(TxtFullName, !string.IsNullOrWhiteSpace(TxtFullName.Text), "اسم المريض مطلوب.");
        valid &= ValidationAssist.Check(TxtPhoneNumber, ClinicInput.NormalizeDigits(TxtPhoneNumber.Text).Count(char.IsDigit) >= 7, "أدخل رقم هاتف صالحاً (7 أرقام على الأقل).");
        valid &= ValidationAssist.Check(TxtAge, TryParseNullableInt(TxtAge.Text, out var age) && (age == null || age is >= 0 and <= 130), "أدخل عمراً من 0 إلى 130 أو اتركه فارغاً.");
        int? birthYear = ReadBirthYear();
        valid &= ValidationAssist.Check(TxtBirthYear, string.IsNullOrWhiteSpace(TxtBirthYear.Text) || birthYear >= AppClock.Today.Year - 130 && birthYear <= AppClock.Today.Year,
            $"أدخل سنة ميلاد بين {AppClock.Today.Year - 130} و{AppClock.Today.Year}.");
        valid &= ValidationAssist.Check(TxtAllergies, CmbAllergyStatus.SelectedIndex != 2 || !string.IsNullOrWhiteSpace(TxtAllergies.Text), "أدخل تفاصيل الحساسية.");
        valid &= ValidationAssist.Check(TxtChronicDiseases, CmbChronicDiseaseStatus.SelectedIndex != 2 || !string.IsNullOrWhiteSpace(TxtChronicDiseases.Text), "أدخل تفاصيل الأمراض المزمنة.");
        if (!valid) ValidationAssist.FocusFirst(TxtFullName, TxtPhoneNumber, TxtAge, TxtBirthYear, TxtAllergies, TxtChronicDiseases);
        return valid;
    }
    private void WritePatientFields(Patient patient)
    {
        patient.PhoneNumber = ClinicInput.NormalizeDigits(TxtPhoneNumber.Text);
        patient.FullName = NullIfWhiteSpace(TxtFullName.Text);
        TryParseNullableInt(TxtAge.Text, out var age); patient.Age = age;
        patient.BirthYear = ReadBirthYear();
        // Retain an existing exact date only when its year still matches; year-only input never invents a month or day.
        DateTime? existingDate = patient.DateOfBirth ?? _legacyBirthDate;
        patient.DateOfBirth = existingDate?.Year == patient.BirthYear ? existingDate : null;
        patient.Gender = GetSelectedComboBoxText(CmbGender);
        patient.BloodType = NormalizeBloodType(GetSelectedComboBoxText(CmbBloodType));
        patient.IsSmoker = ChkSmoker.IsChecked == true;
        patient.SmokingType = patient.IsSmoker ? NullIfWhiteSpace(TxtSmokingType.Text) : null;
        patient.SmokingFrequency = patient.IsSmoker ? NullIfWhiteSpace(TxtSmokingFrequency.Text) : null;
        patient.Allergies = ClinicInput.AllergyValue(CmbAllergyStatus.SelectedIndex, TxtAllergies.Text);
        patient.ChronicDiseases = ClinicInput.ChronicDiseaseValue(CmbChronicDiseaseStatus.SelectedIndex, TxtChronicDiseases.Text);
    }
}

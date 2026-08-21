using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MyClinic
{
    public partial class PatientRecordsView : UserControl
    {
        private static string VisitDoctorName => string.IsNullOrWhiteSpace(LoginSessionStore.CurrentUsername)
            ? "د."
            : $"د. {LoginSessionStore.CurrentUsername}";

        private readonly List<PatientCardModel> _allPatients = new();

        // Use a plain List + manual Dispatcher refresh instead of ObservableCollection
        // to batch all updates in one pass (avoids O(n) individual CollectionChanged events)
        private readonly ObservableCollection<PatientCardModel> _filteredPatients = new();
        private readonly ObservableCollection<VisitHistoryItem> _selectedPatientVisits = new();

        // Cache keyed by visit DB id (unique) instead of DateTime ticks (can collide)
        private readonly Dictionary<int, IReadOnlyList<VisitAttachmentItem>> _attachmentCache = new();

        private PatientCardModel? _selectedPatient;
        private bool _hasLoadedData;
        private bool _refreshRequested = true;
        private Task? _refreshTask;

        // Debounce search to avoid filtering on every keystroke
        private DispatcherTimer? _searchDebounceTimer;
        private const int SearchDebounceMs = 180;

        public PatientRecordsView()
        {
            InitializeComponent();

            GlobalEvents.OnPatientRecordAdded += HandlePatientAdded;

            PatientsListBox.ItemsSource = _filteredPatients;
            VisitsListBox.ItemsSource = _selectedPatientVisits;

            // Pre-render overlay at Collapsed so the first open is instant (avoids cold Measure/Arrange)
            MedicalRecordOverlay.Visibility = Visibility.Collapsed;
            VisitDetailsOverlay.Visibility = Visibility.Collapsed;

            SetLoadingState(true);

            // --- الكود الجديد: الاشتراك في أحداث دورة حياة الواجهة ---
            this.Loaded += PatientRecordsView_Loaded;
            this.Unloaded += PatientRecordsView_Unloaded;
            this.Unloaded += (s, e) => GlobalEvents.OnPatientRecordAdded -= HandlePatientAdded;
            this.IsVisibleChanged += PatientRecordsView_IsVisibleChanged;
            
        }

        // ── Auto-Refresh & Lifecycle ──────────────────────────────────────────────

        private void PatientRecordsView_Loaded(object sender, RoutedEventArgs e)
        {
            GlobalEvents.OnPatientRecordAdded -= HandlePatientAdded;
            GlobalEvents.OnPatientRecordAdded += HandlePatientAdded;
            RefreshPatientData();
        }

        private void PatientRecordsView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible)
            {
                // Always force the flag: a background task triggered by HandlePatientAdded
                // may have cleared _refreshRequested while the view was hidden, so without
                // this line EnsureDataCurrentAsync would skip loading and the new patient
                // would not appear until the next app restart.
                _refreshRequested = true;
                _ = EnsureDataCurrentAsync();
            }
        }

        private void PatientRecordsView_Unloaded(object sender, RoutedEventArgs e)
        {
            GlobalEvents.OnPatientRecordAdded -= HandlePatientAdded;
        }

        private void HandlePatientAdded()
        {
            // Use Dispatcher to ensure thread safety when updating the UI
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Always mark as needing a refresh
                _refreshRequested = true;

                // If the view is currently visible, kick off a refresh immediately.
                // Use EnsureDataCurrentAsync so _refreshTask is tracked correctly
                // and concurrent calls are safely de-duplicated.
                if (this.IsVisible)
                {
                    _ = EnsureDataCurrentAsync();
                }
            });
        }

        public async void RefreshPatientData()
        {
            // نستخدم الدوال الموجودة مسبقاً لطلب تحديث البيانات وجلبها
            RequestRefresh();
            await EnsureDataCurrentAsync();
        }

        // ── Data Management ───────────────────────────────────────────────────────

        public void RequestRefresh()
        {
            _refreshRequested = true;
        }

        public Task EnsureDataCurrentAsync()
        {
            // If a refresh is already in flight, return it — don't start a second one
            if (_refreshTask is not null)
                return _refreshTask;

            // Only skip if we have data AND no refresh was requested
            if (!_refreshRequested && _hasLoadedData)
                return Task.CompletedTask;

            // Assign _refreshTask BEFORE awaiting so any concurrent caller
            // (e.g. IsVisibleChanged firing while HandlePatientAdded already started
            // a refresh) returns the same task instead of spawning a duplicate.
            _refreshTask = RefreshDataAsync();
            return _refreshTask;
        }

        // ── Search ────────────────────────────────────────────────────────────────

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Debounce: reset timer on every keystroke, only apply filter after idle period
            if (_searchDebounceTimer is null)
            {
                _searchDebounceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(SearchDebounceMs)
                };
                _searchDebounceTimer.Tick += (_, _) =>
                {
                    _searchDebounceTimer.Stop();
                    ApplyFilters();
                };
            }

            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        // ── Open / Close handlers ─────────────────────────────────────────────────

        private async void OpenMedicalRecord_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: PatientCardModel patient })
                return;

            _selectedPatient = patient;
            DrawerPatientInfoCard.DataContext = patient;
            VisitCounterBadge.Text = patient.VisitCount.ToString(CultureInfo.CurrentCulture);
            VisitScopeText.Text = patient.VisitCount == 1 ? "زيارة محفوظة" : "الزيارات المسجلة";

            ShowMedicalRecordDrawer();
            HideVisitDetailsOverlay(immediate: true);

            _selectedPatientVisits.Clear();
            EmptyVisitsText.Text = "جارٍ تحميل الزيارات...";
            EmptyVisitsText.Visibility = Visibility.Visible;

            await LoadPatientVisitsAsync(patient.Id);
        }

        private async void OpenVisitDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: VisitHistoryItem visit } || _selectedPatient is null)
                return;

            // Build the view model immediately with whatever is cached — show the modal right away
            ObservableCollection<VisitAttachmentItem> attachmentCollection = new();

            bool alreadyCached = _attachmentCache.TryGetValue(visit.VisitId, out IReadOnlyList<VisitAttachmentItem>? cachedItems);
            if (alreadyCached && cachedItems is not null)
            {
                foreach (VisitAttachmentItem item in cachedItems)
                    attachmentCollection.Add(item);
            }

            VisitDetailsViewModel viewModel = new()
            {
                PatientName    = _selectedPatient.DisplayName,
                DoctorName     = VisitDoctorName,
                VisitDateTimeText      = visit.VisitDateTimeText,
                BloodTypeText          = _selectedPatient.BloodType,
                AllergiesText          = _selectedPatient.Allergies,
                ChronicDiseasesText    = _selectedPatient.ChronicDiseases,
                SmokingText            = _selectedPatient.SmokingSummary,
                PregnancyText          = visit.PregnancySummary,
                BloodPressureText      = visit.BloodPressureText,
                HeartRateText          = visit.HeartRateText,
                TemperatureText        = visit.TemperatureText,
                RespiratoryRateText    = visit.RespiratoryRateText,
                WeightText             = visit.WeightText,
                HeightText             = visit.HeightText,
                BmiText                = visit.BmiText,
                SymptomsText           = visit.SymptomsText,
                DiagnosisText          = visit.DiagnosisText,
                DentalChartCategoryText = visit.ChartModeText,
                Teeth                  = visit.ToothIds,
                TreatmentPlanText      = visit.TreatmentPlanText,
                FinalTreatmentText     = visit.FinalTreatmentText,
                PrescriptionItems      = visit.PrescriptionItems,
                SelectedTreatments     = visit.SelectedTreatments,
                AttachmentItems        = attachmentCollection
            };

            VisitDetailsModal.DataContext = viewModel;
            ShowVisitDetailsOverlay();

            // Load attachments on background only if not cached yet
            if (!alreadyCached && visit.AttachmentPaths.Count > 0)
            {
                List<VisitAttachmentItem> items = await Task.Run(
                    () => LoadAttachmentItems(visit.AttachmentPaths).ToList());

                _attachmentCache[visit.VisitId] = items;

                // Only update if this modal is still the active one
                if (ReferenceEquals(VisitDetailsModal.DataContext, viewModel))
                {
                    foreach (VisitAttachmentItem item in items)
                        viewModel.AttachmentItems.Add(item);
                }
            }
        }

        private void CloseMedicalRecord_Click(object sender, RoutedEventArgs e)
            => HideMedicalRecordDrawer();

        private void CloseVisitDetails_Click(object sender, RoutedEventArgs e)
            => HideVisitDetailsOverlay();

        private void OpenAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: VisitAttachmentItem attachment })
                return;

            if (!File.Exists(attachment.FilePath))
            {
                MessageBox.Show("تعذر العثور على الملف المطلوب.", "المرفقات",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = attachment.FilePath,
                    UseShellExecute = true
                });
            }
            catch
            {
                MessageBox.Show("تعذر فتح الملف حالياً.", "المرفقات",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OverlayDimmer_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => HideMedicalRecordDrawer();

        private void VisitDetailsDimmer_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => HideVisitDetailsOverlay();

        // ── Data loading ──────────────────────────────────────────────────────────

        private async Task RefreshDataAsync()
        {
            SetLoadingState(true);

            try
            {
                List<PatientCardModel> loaded = await LoadPatientsAsync();

                _allPatients.Clear();
                _allPatients.AddRange(loaded);
                _hasLoadedData     = true;
                _refreshRequested  = false;

                ApplyFilters();
                UpdateSelectedPatientState();
            }
            catch
            {
                _allPatients.Clear();
                _filteredPatients.Clear();
                PatientsListBox.Visibility    = Visibility.Collapsed;
                EmptyStateTitle.Text          = "تعذر تحميل السجلات";
                EmptyStateDescription.Text    = "حدث خطأ أثناء تحميل بيانات المرضى. حاول فتح السجل مرة أخرى.";
                EmptyStateCard.Visibility     = Visibility.Visible;
            }
            finally
            {
                SetLoadingState(false);
                _refreshTask = null;
            }
        }

        // ── Filtering ─────────────────────────────────────────────────────────────

        private void ApplyFilters()
        {
            string nameFilter  = TxtNameSearch.Text.Trim();
            string phoneFilter = TxtPhoneSearch.Text.Trim();

            // Build the result list first, then do a single batched UI update
            List<PatientCardModel> result = _allPatients
                .Where(p =>
                    (string.IsNullOrWhiteSpace(nameFilter)  || p.DisplayName.Contains(nameFilter,  StringComparison.CurrentCultureIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(phoneFilter) || p.PhoneNumber.Contains(phoneFilter, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // Batch update: remove stale, add new — minimises CollectionChanged events
            // compared to Clear() + re-add which fires n+1 layout passes
            for (int i = _filteredPatients.Count - 1; i >= 0; i--)
            {
                var existing = _filteredPatients[i];
                
                // Match by ID instead of memory reference
                var matchedItem = result.FirstOrDefault(x => x.Id == existing.Id);

                if (matchedItem == null)
                {
                    // Item is no longer in the DB or doesn't match the search
                    _filteredPatients.RemoveAt(i);
                }
                else
                {
                    // Item exists. Replace it to refresh data (like VisitCount and LatestVisitDate)
                    if (!ReferenceEquals(existing, matchedItem))
                    {
                        _filteredPatients[i] = matchedItem;
                    }
                }
            }

            // Add newly created patients
            foreach (PatientCardModel p in result)
            {
                if (!_filteredPatients.Any(x => x.Id == p.Id))
                {
                    _filteredPatients.Add(p);
                }
            }

            // Re-order to match original sort
            for (int i = 0; i < result.Count; i++)
            {
                // Since we replaced items above, IndexOf will now correctly find the exact reference
                int current = _filteredPatients.IndexOf(result[i]);
                if (current != i && current >= 0)
                {
                    _filteredPatients.Move(current, i);
                }
            }

            EmptyStateTitle.Text       = "لا توجد نتائج مطابقة";
            EmptyStateDescription.Text = "جرّب البحث باسم مختلف أو رقم هاتف آخر.";

            bool hasItems = _filteredPatients.Count > 0;
            PatientsListBox.Visibility = hasItems ? Visibility.Visible   : Visibility.Collapsed;
            EmptyStateCard.Visibility  = hasItems ? Visibility.Collapsed : Visibility.Visible;
        }

        // ── DB loading ────────────────────────────────────────────────────────────

        private static async Task<List<PatientCardModel>> LoadPatientsAsync()
        {

            
            using var db = new AppDbContext();

            HashSet<int> dbVisitIds = await db.Visits
                .AsNoTracking()
                .Select(v => v.Id)
                .ToHashSetAsync();

            List<PatientSummarySnapshot> summaries = await db.Patients
                .AsNoTracking()
                .Select(p => new PatientSummarySnapshot
                {
                    Id               = p.Id,
                    FullName         = p.FullName,
                    PhoneNumber      = p.PhoneNumber,
                    Age              = p.Age,
                    Gender           = p.Gender,
                    BloodType        = p.BloodType,
                    Allergies        = p.Allergies,
                    ChronicDiseases  = p.ChronicDiseases,
                    IsSmoker         = p.IsSmoker,
                    SmokingType      = p.SmokingType,
                    SmokingFrequency = p.SmokingFrequency,
                    VisitCount       = p.Visits.Count(),
                    LatestVisitDate  = p.Visits.Max(v => (DateTime?)v.VisitDate)
                })
                .ToListAsync();

            List<Visit> pending = AppRuntimeCache.RecentVisits
                .Where(v => !dbVisitIds.Contains(v.Id))
                .ToList();

            List<PatientSummarySnapshot> merged = MergePendingVisitsIntoPatientSummaries(summaries, pending);

            return merged
                .Select(MapPatient)
                .OrderByDescending(p => p.LatestVisitDate)
                .ThenBy(p => p.DisplayName, StringComparer.CurrentCulture)
                .ToList();
        }

        private async Task LoadPatientVisitsAsync(int patientId)
        {
            try
            {
                using var db = new AppDbContext();

                List<Visit> dbVisits = await db.Visits
                    .AsNoTracking()
                    .Where(v => v.PatientId == patientId)
                    .Include(v => v.ToothRecords)
                    .OrderByDescending(v => v.VisitDate)
                    .ToListAsync();

                HashSet<int> dbIds = dbVisits.Select(v => v.Id).ToHashSet();

                List<Visit> allVisits = dbVisits
                    .Concat(AppRuntimeCache.RecentVisits
                        .Where(v => v.PatientId == patientId && !dbIds.Contains(v.Id)))
                    .OrderByDescending(v => v.VisitDate)
                    .ToList();

                // ── Phase 1: Map visits on background (fast, no I/O) ──────────────
                List<VisitHistoryItem> mapped = await Task.Run(
                    () => allVisits.Select(MapVisit).ToList());

                // Show the list immediately — user sees visits right away
                _selectedPatientVisits.Clear();
                foreach (VisitHistoryItem v in mapped)
                    _selectedPatientVisits.Add(v);

                EmptyVisitsText.Text       = "لا توجد زيارات محفوظة لهذا المريض حتى الآن.";
                EmptyVisitsText.Visibility = _selectedPatientVisits.Count == 0
                    ? Visibility.Visible : Visibility.Collapsed;

                // ── Phase 2: Pre-load attachment thumbnails in background ──────────
                // Only for visits not already in cache — runs silently after UI is shown
                List<VisitHistoryItem> needsLoad = mapped
                    .Where(v => v.AttachmentPaths.Count > 0 && !_attachmentCache.ContainsKey(v.VisitId))
                    .ToList();

                if (needsLoad.Count > 0)
                {
                    // Load thumbnails in parallel, bounded to 3 concurrent tasks to avoid
                    // flooding the thread pool / disk I/O with too many simultaneous decodes
                    using SemaphoreSlim sem = new(3);
                    await Task.WhenAll(needsLoad.Select(async v =>
                    {
                        await sem.WaitAsync();
                        try
                        {
                            IReadOnlyList<VisitAttachmentItem> items =
                                await Task.Run(() => LoadAttachmentItems(v.AttachmentPaths));
                            _attachmentCache[v.VisitId] = items;
                        }
                        finally
                        {
                            sem.Release();
                        }
                    }));
                }
            }
            catch
            {
                _selectedPatientVisits.Clear();
                EmptyVisitsText.Text       = "تعذر تحميل الزيارات حالياً.";
                EmptyVisitsText.Visibility = Visibility.Visible;
            }
        }

        // ── Animation helpers ─────────────────────────────────────────────────────

        private void ShowMedicalRecordDrawer()
        {
            MedicalRecordOverlay.Visibility = Visibility.Visible;
            OverlayDimmer.Opacity    = 0;
            DrawerTranslate.X        = -48;

            OverlayDimmer.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)));

            DrawerTranslate.BeginAnimation(
                System.Windows.Media.TranslateTransform.XProperty,
                new DoubleAnimation(-48, 0, TimeSpan.FromMilliseconds(210))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        private void HideMedicalRecordDrawer()
        {
            HideVisitDetailsOverlay(immediate: true);

            if (MedicalRecordOverlay.Visibility != Visibility.Visible)
                return;

            var fade = new DoubleAnimation(OverlayDimmer.Opacity, 0, TimeSpan.FromMilliseconds(140));
            OverlayDimmer.BeginAnimation(OpacityProperty, fade);

            var slide = new DoubleAnimation(DrawerTranslate.X, -48, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            slide.Completed += (_, _) => MedicalRecordOverlay.Visibility = Visibility.Collapsed;
            DrawerTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slide);
        }

        private void ShowVisitDetailsOverlay()
        {
            VisitDetailsOverlay.Visibility = Visibility.Visible;
            VisitDetailsDimmer.Opacity     = 0;
            VisitDetailsTranslate.Y        = 24;

            VisitDetailsDimmer.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)));

            VisitDetailsTranslate.BeginAnimation(
                System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(210))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        private void HideVisitDetailsOverlay(bool immediate = false)
        {
            if (VisitDetailsOverlay.Visibility != Visibility.Visible)
                return;

            if (immediate)
            {
                VisitDetailsDimmer.BeginAnimation(OpacityProperty, null);
                VisitDetailsTranslate.BeginAnimation(
                    System.Windows.Media.TranslateTransform.YProperty, null);
                VisitDetailsDimmer.Opacity  = 0;
                VisitDetailsTranslate.Y     = 24;
                VisitDetailsOverlay.Visibility = Visibility.Collapsed;

                // Explicitly free BitmapImage references to release memory sooner
                if (VisitDetailsModal.DataContext is VisitDetailsViewModel vm)
                    vm.AttachmentItems.Clear();

                VisitDetailsModal.DataContext = null;
                return;
            }

            var fade = new DoubleAnimation(VisitDetailsDimmer.Opacity, 0, TimeSpan.FromMilliseconds(140));
            VisitDetailsDimmer.BeginAnimation(OpacityProperty, fade);

            var slide = new DoubleAnimation(VisitDetailsTranslate.Y, 24, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            slide.Completed += (_, _) =>
            {
                VisitDetailsOverlay.Visibility = Visibility.Collapsed;
                if (VisitDetailsModal.DataContext is VisitDetailsViewModel vm)
                    vm.AttachmentItems.Clear();
                VisitDetailsModal.DataContext = null;
            };
            VisitDetailsTranslate.BeginAnimation(
                System.Windows.Media.TranslateTransform.YProperty, slide);
        }

        // ── State sync ────────────────────────────────────────────────────────────

        private void UpdateSelectedPatientState()
        {
            if (_selectedPatient is null)
                return;

            PatientCardModel? refreshed = _allPatients.FirstOrDefault(p => p.Id == _selectedPatient.Id);
            if (refreshed is null)
            {
                HideVisitDetailsOverlay(immediate: true);
                HideMedicalRecordDrawer();
                _selectedPatient = null;
                return;
            }

            _selectedPatient = refreshed;
            DrawerPatientInfoCard.DataContext = refreshed;
            VisitCounterBadge.Text = refreshed.VisitCount.ToString(CultureInfo.CurrentCulture);
            VisitScopeText.Text    = refreshed.VisitCount == 1 ? "زيارة محفوظة" : "الزيارات المسجلة";
        }

        private void SetLoadingState(bool isLoading)
        {
            LoadingOverlay.Visibility  = isLoading ? Visibility.Visible : Visibility.Collapsed;
            PatientsListBox.Visibility = isLoading
                ? Visibility.Collapsed
                : (_filteredPatients.Count > 0 ? Visibility.Visible : Visibility.Collapsed);
            EmptyStateCard.Visibility  = isLoading
                ? Visibility.Collapsed
                : (_filteredPatients.Count == 0 ? Visibility.Visible : Visibility.Collapsed);

            TxtNameSearch.IsEnabled  = !isLoading;
            TxtPhoneSearch.IsEnabled = !isLoading;
        }

        // ── Static mapping helpers ────────────────────────────────────────────────

        private static List<PatientSummarySnapshot> MergePendingVisitsIntoPatientSummaries(
            IEnumerable<PatientSummarySnapshot> summaries, IEnumerable<Visit> pending)
        {
            Dictionary<int, PatientSummarySnapshot> merged = summaries.ToDictionary(s => s.Id);

            foreach (Visit v in pending)
            {
                if (v.Patient is null)
                    continue;

                if (merged.TryGetValue(v.PatientId, out PatientSummarySnapshot? existing))
                {
                    existing.VisitCount += 1;
                    if (!existing.LatestVisitDate.HasValue || v.VisitDate > existing.LatestVisitDate)
                        existing.LatestVisitDate = v.VisitDate;

                    existing.FullName        = string.IsNullOrWhiteSpace(existing.FullName)        ? v.Patient.FullName        : existing.FullName;
                    existing.PhoneNumber     = string.IsNullOrWhiteSpace(existing.PhoneNumber)     ? v.Patient.PhoneNumber     : existing.PhoneNumber;
                    existing.Age             ??= v.Patient.Age;
                    existing.Gender          ??= v.Patient.Gender;
                    existing.BloodType       ??= v.Patient.BloodType;
                    existing.Allergies       ??= v.Patient.Allergies;
                    existing.ChronicDiseases ??= v.Patient.ChronicDiseases;
                    existing.IsSmoker        = existing.IsSmoker || v.Patient.IsSmoker;
                    existing.SmokingType     ??= v.Patient.SmokingType;
                    existing.SmokingFrequency ??= v.Patient.SmokingFrequency;
                    continue;
                }

                merged[v.PatientId] = new PatientSummarySnapshot
                {
                    Id               = v.PatientId,
                    FullName         = v.Patient.FullName,
                    PhoneNumber      = v.Patient.PhoneNumber,
                    Age              = v.Patient.Age,
                    Gender           = v.Patient.Gender,
                    BloodType        = v.Patient.BloodType,
                    Allergies        = v.Patient.Allergies,
                    ChronicDiseases  = v.Patient.ChronicDiseases,
                    IsSmoker         = v.Patient.IsSmoker,
                    SmokingType      = v.Patient.SmokingType,
                    SmokingFrequency = v.Patient.SmokingFrequency,
                    VisitCount       = 1,
                    LatestVisitDate  = v.VisitDate
                };
            }

            return merged.Values.ToList();
        }

        private static PatientCardModel MapPatient(PatientSummarySnapshot p) => new()
        {
            Id              = p.Id,
            DisplayName     = string.IsNullOrWhiteSpace(p.FullName) ? "مريض بدون اسم" : p.FullName.Trim(),
            PhoneNumber     = p.PhoneNumber,
            AgeText         = p.Age?.ToString(CultureInfo.CurrentCulture) ?? "--",
            AgeWithUnit     = p.Age.HasValue ? $"{p.Age.Value} سنة" : "العمر غير محدد",
            GenderLabel     = string.IsNullOrWhiteSpace(p.Gender) ? "غير محدد" : p.Gender.Trim(),
            VisitCount      = p.VisitCount,
            LatestVisitDate = p.LatestVisitDate ?? DateTime.MinValue,
            BloodType       = NormalizeOrFallback(p.BloodType,        "غير محدد"),
            Allergies       = NormalizeOrFallback(p.Allergies,        "غير محددة"),
            ChronicDiseases = NormalizeOrFallback(p.ChronicDiseases,  "غير محددة"),
            SmokingSummary  = BuildSmokingSummary(p)
        };

        private static VisitHistoryItem MapVisit(Visit v)
        {
            IReadOnlyList<string> paths = ParseAttachmentPaths(v.AttachedImagePathsJson);
            IReadOnlyList<string> teeth = v.ToothRecords?
                .Select(r => r.ToothId?.Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.CurrentCulture)
                .Cast<string>()
                .ToList()
                ?? (IReadOnlyList<string>)Array.Empty<string>();

            IReadOnlyList<PrescriptionDisplayItem> rx = ParsePrescriptionItems(v.PrescriptionJson);
            int toothCount = teeth.Count;

            string title = Truncate(
                FirstNonEmpty(v.FinalTreatment, v.Diagnosis, v.Symptoms, "زيارة متابعة"),
                72);

            string details = Truncate(
                FirstNonEmpty(
                    ToLabelValue("خطة العلاج", v.TreatmentPlanNotes),
                    toothCount > 0 ? $"تم تسجيل {toothCount} أسنان في مخطط الزيارة." : null,
                    ToLabelValue("الأعراض", v.Symptoms),
                    ToLabelValue("الضغط", v.BloodPressure),
                    "لا توجد ملاحظات إضافية."),
                120);

            return new VisitHistoryItem
            {
                VisitId              = v.Id,      // ← use DB id as cache key (unique)
                VisitDate            = v.VisitDate,
                VisitDateText        = v.VisitDate.ToString("dd/MM/yyyy",          CultureInfo.InvariantCulture),
                VisitDateTimeText    = v.VisitDate.ToString("dd/MM/yyyy - hh:mm tt", CultureInfo.InvariantCulture),
                Title                = title,
                Details              = details,
                HasAttachments       = paths.Count > 0,
                AttachmentLabel      = paths.Count > 0 ? BuildAttachmentLabel(paths.Count) : string.Empty,
                PregnancySummary     = BuildPregnancySummary(v),
                BloodPressureText    = NormalizeOrFallback(v.BloodPressure,    "--"),
                HeartRateText        = FormatMetric(v.HeartRate,      "BPM"),
                TemperatureText      = FormatMetric(v.Temperature,    "C°"),
                RespiratoryRateText  = FormatMetric(v.RespiratoryRate, "/min"),
                WeightText           = FormatMetric(v.Weight,         "كغ"),
                HeightText           = FormatMetric(v.Height,         "سم"),
                BmiText              = ComputeBmiText(v.Weight, v.Height),
                SymptomsText         = NormalizeOrFallback(v.Symptoms,          "لا توجد أعراض مسجلة"),
                DiagnosisText        = NormalizeOrFallback(v.Diagnosis,         "لا يوجد تشخيص مسجل"),
                ChartModeText        = FormatChartMode(v.ChartMode),
                ToothIds             = teeth,
                TreatmentPlanText    = NormalizeOrFallback(v.TreatmentPlanNotes, "لا توجد خطة علاج أو ملاحظات إضافية."),
                FinalTreatmentText   = NormalizeOrFallback(v.FinalTreatment,     "لا يوجد إجراء نهائي مسجل."),
                PrescriptionItems    = rx,
                AttachmentPaths      = paths,
                SelectedTreatments   = ParseSelectedTreatments(v.SelectedTreatmentsJson)
            };
        }

        private static IReadOnlyList<SelectedTreatment> ParseSelectedTreatments(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<SelectedTreatment>();
            try
            {
                var items = JsonSerializer.Deserialize<List<SelectedTreatment>>(json);
                return items?.Where(t => !string.IsNullOrWhiteSpace(t.TreatmentName)).ToList()
                       ?? (IReadOnlyList<SelectedTreatment>)Array.Empty<SelectedTreatment>();
            }
            catch (JsonException)
            {
                return Array.Empty<SelectedTreatment>();
            }
        }

        // Loads bitmaps with reduced decode size to save memory & speed up decode
        private static IReadOnlyList<VisitAttachmentItem> LoadAttachmentItems(
            IReadOnlyList<string> paths)
        {
            return paths
                .Select(CreateAttachmentItem)
                .Where(item => item is not null)
                .Cast<VisitAttachmentItem>()
                .ToList();
        }

        private static string BuildSmokingSummary(PatientSummarySnapshot p)
        {
            if (!p.IsSmoker)
                return "غير مدخن";

            var parts = new List<string> { "مدخن" };
            if (!string.IsNullOrWhiteSpace(p.SmokingType))      parts.Add(p.SmokingType!.Trim());
            if (!string.IsNullOrWhiteSpace(p.SmokingFrequency)) parts.Add(p.SmokingFrequency!.Trim());
            return string.Join(" - ", parts);
        }

        private static string BuildPregnancySummary(Visit v)
        {
            var parts = new List<string>();
            if (v.IsPregnant)
                parts.Add(v.PregnancyMonth.HasValue ? $"حامل - الشهر {v.PregnancyMonth.Value}" : "حامل");
            if (v.IsNursing)
                parts.Add("مرضعة");
            return parts.Count == 0 ? "غير مسجل" : string.Join(" - ", parts);
        }

        private static string FormatChartMode(string? mode) =>
            string.Equals(mode, "Child", StringComparison.OrdinalIgnoreCase)
                ? "أطفال (Child)" : "بالغين (Adult)";

        private static string BuildAttachmentLabel(int count) =>
            count == 1 ? "1 مرفق" : $"{count} مرفقات";

        private static string FormatMetric(string? value, string suffix) =>
            string.IsNullOrWhiteSpace(value) ? "--" : $"{value.Trim()} {suffix}".Trim();

        private static string ComputeBmiText(string? w, string? h)
        {
            if (!TryParseDecimal(w, out decimal weight) ||
                !TryParseDecimal(h, out decimal height) ||
                weight <= 0 || height <= 0)
                return "--";

            decimal m = height > 10 ? height / 100m : height;
            if (m <= 0) return "--";
            return (weight / (m * m)).ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static bool TryParseDecimal(string? value, out decimal result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string t = value.Trim();
            return decimal.TryParse(t, NumberStyles.Number, CultureInfo.CurrentCulture,  out result)
                || decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
        }

        private static IReadOnlyList<PrescriptionDisplayItem> ParsePrescriptionItems(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<PrescriptionDisplayItem>();
            try
            {
                var items = JsonSerializer.Deserialize<List<PrescriptionItem>>(json);
                if (items is null) return Array.Empty<PrescriptionDisplayItem>();
                return items
                    .Where(i => !string.IsNullOrWhiteSpace(i.MedicineName))
                    .Select(i => new PrescriptionDisplayItem
                    {
                        MedicineName    = i.MedicineName.Trim(),
                        InstructionText = BuildPrescriptionInstruction(i)
                    })
                    .ToList();
            }
            catch (JsonException)
            {
                return Array.Empty<PrescriptionDisplayItem>();
            }
        }

        private static string BuildPrescriptionInstruction(PrescriptionItem item)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(item.Dosage))          parts.Add(item.Dosage.Trim());
            if (!string.IsNullOrWhiteSpace(item.FrequencyHours))  parts.Add($"كل {item.FrequencyHours.Trim()} ساعات");
            if (!string.IsNullOrWhiteSpace(item.Timing))          parts.Add(item.Timing.Trim());
            if (!string.IsNullOrWhiteSpace(item.Notes))           parts.Add(item.Notes.Trim());
            return parts.Count == 0 ? "بدون تعليمات إضافية" : string.Join(" - ", parts);
        }

        private static IReadOnlyList<string> ParseAttachmentPaths(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<string>();
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list is null) return Array.Empty<string>();
                return list
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => ResolveVisitImagePath(p.Trim()))
                    .ToList();
            }
            catch (JsonException)
            {
                return Array.Empty<string>();
            }
        }

        private static string ResolveVisitImagePath(string path)
        {
            if (Path.IsPathRooted(path)) return path;
            string rel = path
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(ResolveVisitImagesRoot(), rel));
        }

        private static string ResolveVisitImagesRoot()
        {
            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string clinicFolder = Path.Combine(appDataFolder, "MyClinicApp");
            string imagesFolder = Path.Combine(clinicFolder, "PatientVisitImages");

            if (!Directory.Exists(imagesFolder))
            {
                Directory.CreateDirectory(imagesFolder);
            }

            return imagesFolder;
        }

        private static VisitAttachmentItem? CreateAttachmentItem(string filePath)
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption      = BitmapCacheOption.OnLoad;
                img.DecodePixelWidth = 320;           // decode at display size — saves memory & CPU
                img.CreateOptions    = BitmapCreateOptions.IgnoreColorProfile; // skip slow ICC profile parsing
                img.UriSource        = new Uri(filePath, UriKind.Absolute);
                img.EndInit();
                img.Freeze();                          // makes it cross-thread safe with zero marshalling

                return new VisitAttachmentItem
                {
                    FilePath  = filePath,
                    FileName  = Path.GetFileName(filePath),
                    Thumbnail = img
                };
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeOrFallback(string? value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        private static string ToLabelValue(string label, string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : $"{label}: {value.Trim()}";

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (string? v in values)
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            return string.Empty;
        }

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..(max - 1)].TrimEnd() + "…";

        // ── Inner snapshot type ───────────────────────────────────────────────────

        private sealed class PatientSummarySnapshot
        {
            public int      Id               { get; set; }
            public string?  FullName         { get; set; }
            public string   PhoneNumber      { get; set; } = string.Empty;
            public int?     Age              { get; set; }
            public string?  Gender           { get; set; }
            public string?  BloodType        { get; set; }
            public bool     IsSmoker         { get; set; }
            public string?  SmokingType      { get; set; }
            public string?  SmokingFrequency { get; set; }
            public string?  Allergies        { get; set; }
            public string?  ChronicDiseases  { get; set; }
            public int      VisitCount       { get; set; }
            public DateTime? LatestVisitDate { get; set; }
        }
    }

    // ── Public model types ────────────────────────────────────────────────────────

    public sealed class PatientCardModel
    {
        public int      Id              { get; init; }
        public string   DisplayName     { get; init; } = string.Empty;
        public string   PhoneNumber     { get; init; } = string.Empty;
        public string   AgeText         { get; init; } = "--";
        public string   AgeWithUnit     { get; init; } = "العمر غير محدد";
        public string   GenderLabel     { get; init; } = "غير محدد";
        public int      VisitCount      { get; init; }
        public DateTime LatestVisitDate { get; init; }
        public string   BloodType       { get; init; } = "غير محدد";
        public string   Allergies       { get; init; } = "غير محددة";
        public string   ChronicDiseases { get; init; } = "غير محددة";
        public string   SmokingSummary  { get; init; } = "غير مدخن";

        public string VisitCountLabel =>
            VisitCount == 1 ? "1 زيارة" : $"{VisitCount} زيارات";
    }

    public sealed class VisitHistoryItem
    {
        public int      VisitId             { get; init; }       // unique DB id — used as cache key
        public DateTime VisitDate           { get; init; }
        public string   VisitDateText       { get; init; } = string.Empty;
        public string   VisitDateTimeText   { get; init; } = string.Empty;
        public string   Title               { get; init; } = string.Empty;
        public string   Details             { get; init; } = string.Empty;
        public bool     HasAttachments      { get; init; }
        public string   AttachmentLabel     { get; init; } = string.Empty;
        public string   PregnancySummary    { get; init; } = "غير مسجل";
        public string   BloodPressureText   { get; init; } = "--";
        public string   HeartRateText       { get; init; } = "--";
        public string   TemperatureText     { get; init; } = "--";
        public string   RespiratoryRateText { get; init; } = "--";
        public string   WeightText          { get; init; } = "--";
        public string   HeightText          { get; init; } = "--";
        public string   BmiText             { get; init; } = "--";
        public string   SymptomsText        { get; init; } = "لا توجد أعراض مسجلة";
        public string   DiagnosisText       { get; init; } = "لا يوجد تشخيص مسجل";
        public string   ChartModeText       { get; init; } = "بالغين (Adult)";
        public IReadOnlyList<string>                  ToothIds          { get; init; } = Array.Empty<string>();
        public string   TreatmentPlanText   { get; init; } = "لا توجد خطة علاج أو ملاحظات إضافية.";
        public string   FinalTreatmentText  { get; init; } = "لا يوجد إجراء نهائي مسجل.";
        public IReadOnlyList<PrescriptionDisplayItem> PrescriptionItems { get; init; } = Array.Empty<PrescriptionDisplayItem>();
        public IReadOnlyList<string>                  AttachmentPaths   { get; init; } = Array.Empty<string>();
        public IReadOnlyList<SelectedTreatment>       SelectedTreatments { get; init; } = Array.Empty<SelectedTreatment>();
    }

    public sealed class VisitDetailsViewModel
    {
        public string PatientName           { get; init; } = string.Empty;
        public string? ChartModeText        { get; set; }
        public string DoctorName            { get; init; } = string.Empty;
        public string VisitDateTimeText     { get; init; } = string.Empty;
        public string BloodTypeText         { get; init; } = "غير محدد";
        public string AllergiesText         { get; init; } = "غير محددة";
        public string ChronicDiseasesText   { get; init; } = "غير محددة";
        public string SmokingText           { get; init; } = "غير مدخن";
        public string PregnancyText         { get; init; } = "غير مسجل";
        public string BloodPressureText     { get; init; } = "--";
        public string HeartRateText         { get; init; } = "--";
        public string TemperatureText       { get; init; } = "--";
        public string RespiratoryRateText   { get; init; } = "--";
        public string WeightText            { get; init; } = "--";
        public string HeightText            { get; init; } = "--";
        public string BmiText               { get; init; } = "--";
        public string SymptomsText          { get; init; } = "لا توجد أعراض مسجلة";
        public string DiagnosisText         { get; init; } = "لا يوجد تشخيص مسجل";
        public string DentalChartCategoryText { get; init; } = "بالغين (Adult)";
        public IReadOnlyList<string>                  Teeth             { get; init; } = Array.Empty<string>();
        public string TreatmentPlanText     { get; init; } = "لا توجد خطة علاج أو ملاحظات إضافية.";
        public string FinalTreatmentText    { get; init; } = "لا يوجد إجراء نهائي مسجل.";
        public IReadOnlyList<PrescriptionDisplayItem> PrescriptionItems { get; init; } = Array.Empty<PrescriptionDisplayItem>();
        public IReadOnlyList<SelectedTreatment>       SelectedTreatments { get; init; } = Array.Empty<SelectedTreatment>();
        public ObservableCollection<VisitAttachmentItem> AttachmentItems { get; init; } = new();

        public bool   HasTeeth               => Teeth.Count > 0;
        public bool   HasSelectedTreatments  => SelectedTreatments.Count > 0;
        public bool   HasFinalTreatment  => !string.IsNullOrWhiteSpace(FinalTreatmentText)
                                         && FinalTreatmentText != "لا يوجد إجراء نهائي مسجل.";
        public bool   HasPrescriptions   => PrescriptionItems.Count > 0;
        public bool   HasAttachments     => AttachmentItems.Count > 0;
        public string AttachmentCountText => AttachmentItems.Count == 1
                                         ? "1 ملف" : $"{AttachmentItems.Count} ملفات";
    }

    public sealed class PrescriptionDisplayItem
    {
        public string MedicineName    { get; init; } = string.Empty;
        public string InstructionText { get; init; } = "بدون تعليمات إضافية";
    }

    public sealed class VisitAttachmentItem
    {
        public string      FilePath  { get; init; } = string.Empty;
        public string      FileName  { get; init; } = string.Empty;
        public BitmapImage Thumbnail { get; init; } = null!;
    }
}

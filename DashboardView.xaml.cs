using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MyClinic
{
    public partial class DashboardView : UserControl
    {
        private enum ViewMode { Daily, Monthly }
        private enum StatusFilter { Upcoming, Past, All }

        private readonly ObservableCollection<AppointmentCardModel> _appointments = new();
        private readonly List<AppointmentEntry> _allAppointments = new();
        private readonly List<Patient> _allPatients = new();
        private readonly List<VisitAttendanceRecord> _visitAttendanceRecords = new();
        private readonly List<string> _appointmentNameSuggestions = new();
        private readonly List<string> _appointmentPhoneSuggestions = new();
        private int? _selectedAppointmentPatientId;
        private bool _isUpdatingAppointmentSuggestions;
        private bool _isUpdatingAppointmentFields;
        private bool _refreshRequested = true;
        private bool _hasLoadedData;
        private bool _isLoadingFileKitCanalCount;
        private Task? _refreshTask;
        private ViewMode _viewMode;
        private StatusFilter _statusFilter;

        public DashboardView()
        {
            _isLoadingFileKitCanalCount = true;
            InitializeComponent();

            RefreshWelcomeName();

            LoadFileKitCanalCount();
            _isLoadingFileKitCanalCount = false;

            _viewMode = ViewMode.Monthly;
            _statusFilter = StatusFilter.Upcoming;

            AppointmentsItemsControl.ItemsSource = _appointments;

            DateTime syrianNow = GetSyrianTime();
            DpAppointmentsFilter.SelectedDate = syrianNow.Date;
            DpAppointmentDate.SelectedDate = syrianNow.Date;

            PopulateTimeOptions();
            ApplyThemeVisuals(IsDarkThemeActive());
            UpdateButtonStyles();

            GlobalEvents.OnPatientRecordAdded += HandlePatientRecordAdded;
            GlobalEvents.OnFileKitCanalCountChanged += HandleFileKitCanalCountChanged;
            GlobalEvents.OnThemeChanged += HandleThemeChanged;
            Loaded += DashboardView_Loaded;
        }

        private void DashboardView_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyThemeVisuals(IsDarkThemeActive());
            UpdateButtonStyles();
            if (_hasLoadedData)
            {
                ApplyAppointmentsFilter();
            }
        }

        private bool IsDarkThemeActive()
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
                return mainWindow.IsDarkTheme;
            if (Application.Current?.MainWindow is MainWindow appMainWindow)
                return appMainWindow.IsDarkTheme;
            return false;
        }

        private static SolidColorBrush CreateBrush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));

        private static LinearGradientBrush CreateGradient(string startHex, string endHex)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(startHex), 0.0));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(endHex), 1.0));
            return brush;
        }

        private void HandleThemeChanged(bool isDark)
        {
            ApplyThemeVisuals(isDark);
            UpdateButtonStyles();
            if (_hasLoadedData)
            {
                ApplyAppointmentsFilter();
            }
        }

        private void ApplyThemeVisuals(bool isDark)
        {
            // تبديل عناصر الترحيب والإضاءة بين الوضعين الفاتح والداكن
            if (TxtWelcomeWave is not null)
                TxtWelcomeWave.Visibility = isDark ? Visibility.Collapsed : Visibility.Visible;
            if (IconWelcomeSparkles is not null)
                IconWelcomeSparkles.Visibility = isDark ? Visibility.Visible : Visibility.Collapsed;
            if (WelcomeDarkGlow is not null)
                WelcomeDarkGlow.Visibility = isDark ? Visibility.Visible : Visibility.Collapsed;

            // تفعيل/إخفاء طبقات إضاءة مؤشر الفأرة (Spotlight) في الوضع الداكن
            if (DashboardBackSpotlightCanvas is not null)
                DashboardBackSpotlightCanvas.Visibility = isDark ? Visibility.Visible : Visibility.Collapsed;
            if (DashboardSurfaceSpotlightCanvas is not null)
                DashboardSurfaceSpotlightCanvas.Visibility = isDark ? Visibility.Visible : Visibility.Collapsed;

            // تنسيق وترتيب أزرار بطاقة الإجراءات السريعة لتطابق الصور المرفقة تماماً
            if (BtnAddPatient is not null && BtnAddAppointment is not null)
            {
                if (isDark)
                {
                    // في الوضع الداكن: زر (+ إضافة موعد جديد) يميناً بالأخضر الزمردي المضيء، وزر (+ إضافة مريض) يساراً بالرمادي الداكن
                    Grid.SetColumn(BtnAddAppointment, 0);
                    Grid.SetColumn(BtnAddPatient, 2);

                    BtnAddAppointment.Background = CreateGradient("#14B8A6", "#0D9488");
                    BtnAddAppointment.BorderBrush = CreateBrush("#5EEAD4");
                    BtnAddAppointment.BorderThickness = new Thickness(1);
                    BtnAddAppointment.Foreground = Brushes.White;

                    BtnAddPatient.Background = CreateBrush("#47535E");
                    BtnAddPatient.BorderBrush = CreateBrush("#64748B");
                    BtnAddPatient.BorderThickness = new Thickness(1);
                    BtnAddPatient.Foreground = Brushes.White;

                    if (IconAddAppointment is not null) IconAddAppointment.Visibility = Visibility.Visible;
                    if (IconAddPatient is not null) IconAddPatient.Visibility = Visibility.Visible;
                }
                else
                {
                    // في الوضع الفاتح: زر (+ إضافة مريض) يميناً بالأخضر النعناعي الفاتح، وزر (+ إضافة موعد جديد) يساراً بالأزرق الملكي
                    Grid.SetColumn(BtnAddPatient, 0);
                    Grid.SetColumn(BtnAddAppointment, 2);

                    BtnAddPatient.Background = CreateBrush("#C3DCD8");
                    BtnAddPatient.BorderBrush = CreateBrush("#8EB4AE");
                    BtnAddPatient.BorderThickness = new Thickness(1);
                    BtnAddPatient.Foreground = CreateBrush("#284B46");

                    BtnAddAppointment.Background = CreateBrush("#3348BD");
                    BtnAddAppointment.BorderBrush = Brushes.Transparent;
                    BtnAddAppointment.BorderThickness = new Thickness(0);
                    BtnAddAppointment.Foreground = Brushes.White;

                    if (IconAddAppointment is not null) IconAddAppointment.Visibility = Visibility.Collapsed;
                    if (IconAddPatient is not null) IconAddPatient.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void Dashboard_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!IsDarkThemeActive())
                return;

            Point pos = e.GetPosition(DashboardRootGrid);

            if (DashboardBackSpotlightCanvas is not null && DashboardBackSpotlightTransform is not null)
            {
                if (DashboardBackSpotlightCanvas.Visibility != Visibility.Visible)
                    DashboardBackSpotlightCanvas.Visibility = Visibility.Visible;

                DashboardBackSpotlightTransform.X = pos.X - (DashboardBackSpotlight.Width / 2.0);
                DashboardBackSpotlightTransform.Y = pos.Y - (DashboardBackSpotlight.Height / 2.0);
            }

            if (DashboardSurfaceSpotlightCanvas is not null && DashboardSurfaceSpotlightTransform is not null)
            {
                if (DashboardSurfaceSpotlightCanvas.Visibility != Visibility.Visible)
                    DashboardSurfaceSpotlightCanvas.Visibility = Visibility.Visible;

                DashboardSurfaceSpotlightTransform.X = pos.X - (DashboardSurfaceSpotlight.Width / 2.0);
                DashboardSurfaceSpotlightTransform.Y = pos.Y - (DashboardSurfaceSpotlight.Height / 2.0);
            }
        }

        private void Dashboard_MouseLeave(object sender, MouseEventArgs e)
        {
            if (DashboardBackSpotlightTransform is not null)
            {
                DashboardBackSpotlightTransform.X = -1200;
                DashboardBackSpotlightTransform.Y = -1200;
            }

            if (DashboardSurfaceSpotlightTransform is not null)
            {
                DashboardSurfaceSpotlightTransform.X = -1200;
                DashboardSurfaceSpotlightTransform.Y = -1200;
            }
        }

        public void RefreshWelcomeName()
        {
            string username = LoginSessionStore.CurrentUsername;
            TxtWelcome.Text = string.IsNullOrWhiteSpace(username)
                ? "أهلاً بك"
                : $"أهلاً د. {username}";
        }

        public void RefreshDatesForAppTimeChange(DateTime previousAppTime)
        {
            DateTime previousDate = previousAppTime.Date;
            DateTime currentDate = AppClock.Today;
            if (DpAppointmentsFilter.SelectedDate?.Date == previousDate)
                DpAppointmentsFilter.SelectedDate = currentDate;
            if (DpAppointmentDate.SelectedDate?.Date == previousDate)
                DpAppointmentDate.SelectedDate = currentDate;

            RequestRefresh();
            _ = RefreshNowAsync();
        }

        private void HandlePatientRecordAdded()
        {
            RequestRefresh();
            _ = RefreshNowAsync();
        }

        private void HandleFileKitCanalCountChanged()
        {
            LoadFileKitCanalCount();
        }

        private void BtnResetFileKitCanalCount_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("هل تريد تصفير عدد الأقنية لهذا الكيت؟", "تأكيد التصفير", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            using var db = new AppDbContext();
            var settings = db.AppSettings.FirstOrDefault() ?? new AppSettings();
            if (settings.Id == 0)
                db.AppSettings.Add(settings);
            settings.FileKitCanalCount = 0;
            settings.UpdatedAt = AppClock.Now;
            db.SaveChanges();

            _isLoadingFileKitCanalCount = true;
            TxtFileKitCanalCount.Text = "0";
            _isLoadingFileKitCanalCount = false;
            UpdateFileKitWarning();
            GlobalEvents.NotifyFileKitCanalCountChanged();
        }

        private void LoadFileKitCanalCount()
        {
            int count = 0;
            try
            {
                using var db = new AppDbContext();
                var settings = db.AppSettings.FirstOrDefault();
                count = settings?.FileKitCanalCount ?? 0;
            }
            catch { }

            TxtFileKitCanalCount.Text = count.ToString(CultureInfo.InvariantCulture);
            UpdateFileKitWarning();
        }

        private void TxtFileKitCanalCount_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtFileKitCanalCount is null || _isLoadingFileKitCanalCount) return;

            SaveFileKitCanalCount();
            UpdateFileKitWarning();
        }

        private void TxtFileKitCanalCount_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            e.Handled = true;
            textBox.Focus();
            textBox.SelectAll();
        }

        private void SaveFileKitCanalCount()
        {
            if (!int.TryParse(TxtFileKitCanalCount.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count < 0)
                return;

            try
            {
                using var db = new AppDbContext();
                var settings = db.AppSettings.FirstOrDefault();
                if (settings == null)
                {
                    settings = new AppSettings();
                    db.AppSettings.Add(settings);
                }

                settings.FileKitCanalCount = count;
                settings.UpdatedAt = AppClock.Now;
                db.SaveChanges();
            }
            catch
            {
            }
        }

        private void UpdateFileKitWarning()
        {
            if (TxtFileKitWarning is null || TxtFileKitCanalCount is null) return;

            bool exceededLimit = int.TryParse(TxtFileKitCanalCount.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                && count > 10;
            TxtFileKitWarning.Visibility = exceededLimit ? Visibility.Visible : Visibility.Collapsed;
        }

        private DateTime GetSyrianTime()
        {
            return AppClock.Now;
        }

        public void RequestRefresh()
        {
            _refreshRequested = true;
        }

        public void AddSavedVisitImmediately(string patientName, string phoneNumber, string reason, DateTime visitDateTime, int visitId, int patientId)
        {
            AppointmentEntry savedVisit = new()
            {
                Id = -Math.Abs(visitId),
                PatientId = patientId,
                PatientName = string.IsNullOrWhiteSpace(patientName) ? "بدون اسم" : patientName,
                PhoneNumber = phoneNumber ?? string.Empty,
                Reason = string.IsNullOrWhiteSpace(reason) ? "مراجعة" : reason,
                AppointmentDateTime = visitDateTime,
                IsVisitRecord = true
            };

            _allAppointments.RemoveAll(a => a.IsVisitRecord
                && IsSamePatient(a, savedVisit)
                && a.AppointmentDateTime == visitDateTime);

            bool visitIsRepresentedByManualAppointment = _allAppointments.Any(appointment =>
                !appointment.IsVisitRecord
                && !appointment.IsAutomaticallyGenerated
                && appointment.AppointmentDateTime.Date == visitDateTime.Date
                && appointment.CreatedAt is DateTime createdAt
                && visitDateTime > createdAt
                && IsSamePatient(appointment, savedVisit));
            if (!visitIsRepresentedByManualAppointment)
                _allAppointments.Add(savedVisit);

            _visitAttendanceRecords.Add(new VisitAttendanceRecord(
                patientId,
                phoneNumber ?? string.Empty,
                patientName ?? string.Empty,
                visitDateTime));
            foreach (AppointmentEntry appointment in _allAppointments)
            {
                if (appointment.PatientId is null
                    && string.Equals(appointment.PatientName?.Trim(), patientName?.Trim(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(appointment.PhoneNumber?.Trim(), phoneNumber?.Trim(), StringComparison.Ordinal))
                {
                    appointment.PatientId = patientId;
                }
                UpdateAppointmentAttendance(appointment);
            }

            _hasLoadedData = true;
            _refreshRequested = false;
            ApplyAppointmentsFilter();
            UpdateSearchSuggestions();
            _ = RefreshNowAsync();
        }

        public Task EnsureDataCurrentAsync()
        {
            if (_refreshTask is not null)
            {
                return _refreshTask;
            }

            if (!_refreshRequested && _hasLoadedData)
            {
                return Task.CompletedTask;
            }

            _refreshTask = RefreshAppointmentsAsync();
            return _refreshTask;
        }

        public async Task RefreshNowAsync()
        {
            if (_refreshTask is not null)
            {
                await _refreshTask;
            }

            _refreshRequested = true;
            await EnsureDataCurrentAsync();
        }

        private async Task RefreshAppointmentsAsync()
        {
            SetLoadingState(true);

            try
            {
                using var db = new AppDbContext();

                List<AppointmentEntry> appointments = await db.Appointments
                    .AsNoTracking()
                    .ToListAsync();

                List<Visit> visits = await db.Visits
                    .AsNoTracking()
                    .Include(visit => visit.Patient)
                    .OrderBy(visit => visit.VisitDate)
                    .ToListAsync();

                List<Patient> patients = await db.Patients
                    .AsNoTracking()
                    .ToListAsync();

                var appointmentKeys = appointments
                    .Select(appointment => BuildCalendarKey(appointment.PhoneNumber, appointment.AppointmentDateTime))
                    .ToHashSet(StringComparer.Ordinal);
                var automaticallyGeneratedKeys = visits
                    .Select(visit => BuildCalendarKey(visit.Patient?.PhoneNumber, visit.VisitDate))
                    .ToHashSet(StringComparer.Ordinal);

                foreach (AppointmentEntry appointment in appointments)
                {
                    if (automaticallyGeneratedKeys.Contains(BuildCalendarKey(appointment.PhoneNumber, appointment.AppointmentDateTime)))
                        appointment.IsAutomaticallyGenerated = true;

                    appointment.PatientId ??= FindPatientId(appointment.PatientName, appointment.PhoneNumber, patients);
                }

                List<AppointmentEntry> manualAppointments = appointments
                    .Where(appointment => !appointment.IsAutomaticallyGenerated)
                    .ToList();
                var visitNumberByPatient = new Dictionary<int, int>();

                foreach (Visit visit in visits)
                {
                    string phoneNumber = visit.Patient?.PhoneNumber ?? string.Empty;
                    string key = BuildCalendarKey(phoneNumber, visit.VisitDate);
                    visitNumberByPatient.TryGetValue(visit.PatientId, out int visitNumber);
                    visitNumber++;
                    visitNumberByPatient[visit.PatientId] = visitNumber;

                    if (appointmentKeys.Contains(key)
                        || manualAppointments.Any(appointment =>
                            appointment.AppointmentDateTime.Date == visit.VisitDate.Date
                            && appointment.CreatedAt is DateTime createdAt
                            && visit.VisitDate > createdAt
                            && IsSamePatient(appointment, visit)))
                        continue;

                    appointments.Add(new AppointmentEntry
                    {
                        Id = -visit.Id,
                        PatientId = visit.PatientId,
                        PatientName = visit.Patient?.FullName ?? "بدون اسم",
                        PhoneNumber = phoneNumber,
                        Reason = visitNumber == 1 ? "معاينة جديدة" : "مراجعة",
                        AppointmentDateTime = visit.VisitDate,
                        IsVisitRecord = true
                    });
                }

                appointments.RemoveAll(appointment =>
                    appointment.IsAutomaticallyGenerated
                    && manualAppointments.Any(manualAppointment =>
                        manualAppointment.AppointmentDateTime.Date == appointment.AppointmentDateTime.Date
                        && manualAppointment.CreatedAt is DateTime createdAt
                        && appointment.AppointmentDateTime > createdAt
                        && IsSamePatient(manualAppointment, appointment)));

                _allAppointments.Clear();
                _allAppointments.AddRange(appointments);

                _allPatients.Clear();
                _allPatients.AddRange(patients);

                _visitAttendanceRecords.Clear();
                foreach (Visit visit in visits)
                {
                    _visitAttendanceRecords.Add(new VisitAttendanceRecord(
                        visit.PatientId,
                        visit.Patient?.PhoneNumber ?? string.Empty,
                        visit.Patient?.FullName ?? string.Empty,
                        visit.VisitDate));
                }
                foreach (AppointmentEntry appointment in _allAppointments)
                    UpdateAppointmentAttendance(appointment);

                ApplyAppointmentsFilter();
                UpdateSearchSuggestions();

                _hasLoadedData = true;
                _refreshRequested = false;
            }
            catch (Exception)
            {
                _allAppointments.Clear();
                _allPatients.Clear();
                ReplaceAppointments(Array.Empty<AppointmentCardModel>());
                EmptyAppointmentsState.Visibility = Visibility.Visible;
                MessageBox.Show("تعذر تحميل المواعيد حالياً.", "المواعيد", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                SetLoadingState(false);
                _refreshTask = null;
            }
        }

        private void UpdateSearchSuggestions()
        {
            var appointmentNames = _allAppointments
                .Select(a => a.PatientName)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim());
            var patientNames = _allPatients
                .Select(p => p.FullName)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim());
            var allNames = appointmentNames.Concat(patientNames).Distinct().ToList();

            var appointmentPhones = _allAppointments
                .Select(a => a.PhoneNumber)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim());
            var patientPhones = _allPatients
                .Select(p => p.PhoneNumber)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim());
            var allPhones = appointmentPhones.Concat(patientPhones).Distinct().ToList();

            var allSuggestions = allNames.Concat(allPhones).Distinct().ToList();

            _appointmentNameSuggestions.Clear();
            _appointmentNameSuggestions.AddRange(allNames);
            _appointmentPhoneSuggestions.Clear();
            _appointmentPhoneSuggestions.AddRange(allPhones);

            CmbAppointmentsSearch.ItemsSource = allSuggestions;
            RefreshAppointmentSuggestions();
        }

        private void CmbAppointmentPatientName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded || _isUpdatingAppointmentFields || _isUpdatingAppointmentSuggestions)
                return;

            _selectedAppointmentPatientId = null;
            RefreshAppointmentSuggestions();
        }

        private void CmbAppointmentPhoneNumber_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded || _isUpdatingAppointmentFields || _isUpdatingAppointmentSuggestions)
                return;

            _selectedAppointmentPatientId = null;
            RefreshAppointmentSuggestions();
        }

        private void RefreshAppointmentSuggestions()
        {
            if (_isUpdatingAppointmentSuggestions)
                return;

            string nameQuery = CmbAppointmentPatientName.Text?.Trim() ?? string.Empty;
            string phoneQuery = CmbAppointmentPhoneNumber.Text?.Trim() ?? string.Empty;

            List<string> matchingNames = FilterAppointmentSuggestions(_appointmentNameSuggestions, nameQuery);
            List<string> matchingPhones = FilterAppointmentSuggestions(_appointmentPhoneSuggestions, phoneQuery);

            _isUpdatingAppointmentSuggestions = true;
            try
            {
                CmbAppointmentPatientName.ItemsSource = matchingNames;
                CmbAppointmentPhoneNumber.ItemsSource = matchingPhones;

                bool nameHasFocus = CmbAppointmentPatientName.IsKeyboardFocusWithin;
                bool phoneHasFocus = CmbAppointmentPhoneNumber.IsKeyboardFocusWithin;

                CmbAppointmentPatientName.IsDropDownOpen =
                    nameHasFocus && nameQuery.Length > 0 && matchingNames.Count > 0;
                CmbAppointmentPhoneNumber.IsDropDownOpen =
                    phoneHasFocus && phoneQuery.Length > 0 && matchingPhones.Count > 0;

                CollapsePatientNameSelection();
            }
            finally
            {
                _isUpdatingAppointmentSuggestions = false;
            }
        }

        private void CollapsePatientNameSelection()
        {
            if (!CmbAppointmentPatientName.IsKeyboardFocusWithin)
                return;

            if (CmbAppointmentPatientName.Template.FindName("PART_EditableTextBox", CmbAppointmentPatientName) is not TextBox textBox)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (CmbAppointmentPatientName.IsKeyboardFocusWithin)
                {
                    textBox.CaretIndex = textBox.Text.Length;
                    textBox.SelectionLength = 0;
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private static List<string> FilterAppointmentSuggestions(IEnumerable<string> values, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<string>();

            return values
                .Where(value => value.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .Take(8)
                .ToList();
        }

        private void CmbAppointmentPatientName_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingAppointmentSuggestions)
                return;

            if (e.AddedItems.Count > 0 && e.AddedItems[0] is string selectedName)
            {
                var patientMatch = _allPatients.FirstOrDefault(p => p.FullName == selectedName);
                if (patientMatch != null && !string.IsNullOrWhiteSpace(patientMatch.PhoneNumber))
                {
                    _selectedAppointmentPatientId = patientMatch.Id;
                    _isUpdatingAppointmentFields = true;
                    try
                    {
                        CmbAppointmentPhoneNumber.Text = patientMatch.PhoneNumber;
                    }
                    finally
                    {
                        _isUpdatingAppointmentFields = false;
                    }
                    CmbAppointmentPatientName.IsDropDownOpen = false;
                    CmbAppointmentPhoneNumber.IsDropDownOpen = false;
                    return;
                }

                var appMatch = _allAppointments.FirstOrDefault(a => a.PatientName == selectedName);
                if (appMatch != null && !string.IsNullOrWhiteSpace(appMatch.PhoneNumber))
                {
                    _selectedAppointmentPatientId = null;
                    _isUpdatingAppointmentFields = true;
                    try
                    {
                        CmbAppointmentPhoneNumber.Text = appMatch.PhoneNumber;
                    }
                    finally
                    {
                        _isUpdatingAppointmentFields = false;
                    }
                    CmbAppointmentPatientName.IsDropDownOpen = false;
                    CmbAppointmentPhoneNumber.IsDropDownOpen = false;
                }
            }
        }

        private void CmbAppointmentPhoneNumber_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingAppointmentSuggestions)
                return;

            if (e.AddedItems.Count > 0 && e.AddedItems[0] is string selectedPhone)
            {
                var patientMatch = _allPatients.FirstOrDefault(p => p.PhoneNumber == selectedPhone);
                if (patientMatch != null && !string.IsNullOrWhiteSpace(patientMatch.FullName))
                {
                    _selectedAppointmentPatientId = patientMatch.Id;
                    _isUpdatingAppointmentFields = true;
                    try
                    {
                        CmbAppointmentPatientName.Text = patientMatch.FullName;
                    }
                    finally
                    {
                        _isUpdatingAppointmentFields = false;
                    }
                    CmbAppointmentPatientName.IsDropDownOpen = false;
                    CmbAppointmentPhoneNumber.IsDropDownOpen = false;
                    return;
                }

                var appMatch = _allAppointments.FirstOrDefault(a => a.PhoneNumber == selectedPhone);
                if (appMatch != null && !string.IsNullOrWhiteSpace(appMatch.PatientName))
                {
                    _selectedAppointmentPatientId = null;
                    _isUpdatingAppointmentFields = true;
                    try
                    {
                        CmbAppointmentPatientName.Text = appMatch.PatientName;
                    }
                    finally
                    {
                        _isUpdatingAppointmentFields = false;
                    }
                    CmbAppointmentPatientName.IsDropDownOpen = false;
                    CmbAppointmentPhoneNumber.IsDropDownOpen = false;
                }
            }
        }

        private void BtnAddPatient_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
            {
                mainWindow.ShowAddPatient();
            }
        }

        private void BtnAddAppointment_Click(object sender, RoutedEventArgs e)
        {
            DpAppointmentDate.SelectedDate = GetSyrianTime().Date;
            ShowAppointmentDialog();
        }

        private async void BtnSaveAppointment_Click(object sender, RoutedEventArgs e)
        {
            string patientName = CmbAppointmentPatientName.Text.Trim();
            string phoneNumber = CmbAppointmentPhoneNumber.Text.Trim();
            string reason = GetSelectedAppointmentReason();

            if (string.IsNullOrWhiteSpace(patientName))
            {
                MessageBox.Show("يرجى إدخال اسم المريض.", "المواعيد", MessageBoxButton.OK, MessageBoxImage.Warning);
                CmbAppointmentPatientName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                MessageBox.Show("يرجى إدخال رقم الهاتف.", "المواعيد", MessageBoxButton.OK, MessageBoxImage.Warning);
                CmbAppointmentPhoneNumber.Focus();
                return;
            }

            if (DpAppointmentDate.SelectedDate is null)
            {
                MessageBox.Show("يرجى اختيار تاريخ الموعد.", "المواعيد", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string hourStr = CmbHour.SelectedItem?.ToString() ?? "12";
            string minStr = CmbMinute.SelectedItem?.ToString() ?? "00";
            string amPmStr = CmbAmPm.SelectedItem?.ToString() ?? "AM";
            string timeText = $"{hourStr}:{minStr} {amPmStr}";

            if (!DateTime.TryParse(timeText, out DateTime parsedTime))
            {
                MessageBox.Show("يرجى التأكد من إدخال وقت صحيح.", "المواعيد", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TimeSpan selectedTime = parsedTime.TimeOfDay;
            DateTime appointmentDateTime = DpAppointmentDate.SelectedDate.Value.Date.Add(selectedTime);

            try
            {
                AppointmentEntry newAppointment = new()
                {
                    PatientName = patientName,
                    PhoneNumber = phoneNumber,
                    Reason = reason,
                    AppointmentDateTime = appointmentDateTime,
                    CreatedAt = AppClock.Now,
                    PatientId = _selectedAppointmentPatientId ?? FindPatientId(patientName, phoneNumber, _allPatients)
                };

                using var db = new AppDbContext();
                db.Appointments.Add(newAppointment);

                await db.SaveChangesAsync();

                UpdateAppointmentAttendance(newAppointment);
                _allAppointments.Add(newAppointment);

                HideAppointmentDialog();

                UpdateSearchSuggestions();
                ApplyAppointmentsFilter();
            }
            catch (Exception)
            {
                MessageBox.Show("تعذر حفظ الموعد حالياً.", "المواعيد", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCloseAppointmentDialog_Click(object sender, RoutedEventArgs e)
        {
            HideAppointmentDialog();
        }

        private void DpAppointmentsFilter_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            ApplyAppointmentsFilter();
        }

        private void DpAppointmentsFilter_CalendarOpened(object sender, RoutedEventArgs e)
        {
            if (_viewMode == ViewMode.Monthly)
            {
                if (sender is DatePicker datePicker &&
                    datePicker.Template.FindName("PART_Popup", datePicker) is Popup popup &&
                    popup.Child is System.Windows.Controls.Calendar calendar)
                {
                    calendar.DisplayMode = CalendarMode.Year;
                    calendar.DisplayModeChanged += Calendar_DisplayModeChanged;
                }
            }
        }

        private void Calendar_DisplayModeChanged(object? sender, CalendarModeChangedEventArgs e)
        {
            if (_viewMode == ViewMode.Monthly && sender is System.Windows.Controls.Calendar calendar && calendar.DisplayMode == CalendarMode.Month)
            {
                calendar.SelectedDate = calendar.DisplayDate;
                DpAppointmentsFilter.IsDropDownOpen = false;
                calendar.DisplayModeChanged -= Calendar_DisplayModeChanged;
            }
        }

        private void DpAppointmentsFilter_CalendarClosed(object sender, RoutedEventArgs e)
        {
            if (sender is DatePicker datePicker &&
                datePicker.Template.FindName("PART_Popup", datePicker) is Popup popup &&
                popup.Child is System.Windows.Controls.Calendar calendar)
            {
                calendar.DisplayModeChanged -= Calendar_DisplayModeChanged;
            }
        }

        private void BtnMonthlyView_Click(object sender, RoutedEventArgs e)
        {
            _viewMode = ViewMode.Monthly;
            ApplyAppointmentsFilter();
        }

        private void BtnDailyView_Click(object sender, RoutedEventArgs e)
        {
            _viewMode = ViewMode.Daily;
            ApplyAppointmentsFilter();
        }

        private void BtnFilterUpcoming_Click(object sender, RoutedEventArgs e)
        {
            _statusFilter = StatusFilter.Upcoming;
            ApplyAppointmentsFilter();
        }

        private void BtnFilterPast_Click(object sender, RoutedEventArgs e)
        {
            _statusFilter = StatusFilter.Past;
            ApplyAppointmentsFilter();
        }

        private void BtnFilterAll_Click(object sender, RoutedEventArgs e)
        {
            _statusFilter = StatusFilter.All;
            ApplyAppointmentsFilter();
        }

        private void BtnCurrentDate_Click(object sender, RoutedEventArgs e)
        {
            DpAppointmentsFilter.SelectedDate = GetSyrianTime().Date;
        }

        private void BtnCalendarPreviousMonth_Click(object sender, RoutedEventArgs e)
        {
            DateTime selectedDate = DpAppointmentsFilter.SelectedDate ?? GetSyrianTime().Date;
            DpAppointmentsFilter.SelectedDate = new DateTime(selectedDate.Year, selectedDate.Month, 1).AddMonths(-1);
        }

        private void BtnCalendarNextMonth_Click(object sender, RoutedEventArgs e)
        {
            DateTime selectedDate = DpAppointmentsFilter.SelectedDate ?? GetSyrianTime().Date;
            DpAppointmentsFilter.SelectedDate = new DateTime(selectedDate.Year, selectedDate.Month, 1).AddMonths(1);
        }

        private void CalendarDay_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not Border dayBorder || dayBorder.Tag is not DateTime selectedDate)
                return;

            DependencyObject? source = e.OriginalSource as DependencyObject;
            while (source is not null)
            {
                if (source is Button)
                    return;
                source = VisualTreeHelper.GetParent(source);
            }

            e.Handled = true;
            ShowDayDetails(selectedDate);
        }

        private void ShowDayDetails(DateTime selectedDate)
        {
            DpAppointmentDate.SelectedDate = selectedDate;
            TxtDayDetailsDate.Text = selectedDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

            var dayAppointments = _allAppointments
                .Where(appointment => appointment.AppointmentDateTime.Date == selectedDate.Date)
                .OrderBy(appointment => appointment.AppointmentDateTime)
                .ToList();

            DayDetailsItemsControl.ItemsSource = dayAppointments;
            TxtDayDetailsEmpty.Visibility = dayAppointments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            DayDetailsOverlay.Visibility = Visibility.Visible;
        }

        private void BtnAddAppointmentForDay_Click(object sender, RoutedEventArgs e)
        {
            DayDetailsOverlay.Visibility = Visibility.Collapsed;
            ShowAppointmentDialog();
        }

        private void BtnCloseDayDetails_Click(object sender, RoutedEventArgs e)
        {
            DayDetailsOverlay.Visibility = Visibility.Collapsed;
        }

        private async void BtnDeleteAppointmentFromDay_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || !int.TryParse(button.Tag?.ToString(), out int appointmentId) || appointmentId <= 0)
                return;

            MessageBoxResult result = MessageBox.Show(
                "هل أنت متأكد من حذف هذا الموعد؟",
                "تأكيد حذف الموعد",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No,
                MessageBoxOptions.RightAlign);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                using var db = new AppDbContext();
                AppointmentEntry? appointment = await db.Appointments.FindAsync(appointmentId);
                if (appointment is null)
                    return;

                db.Appointments.Remove(appointment);
                await db.SaveChangesAsync();

                _allAppointments.RemoveAll(a => a.Id == appointmentId);
                UpdateSearchSuggestions();
                ApplyAppointmentsFilter();

                DateTime selectedDate = DpAppointmentDate.SelectedDate ?? GetSyrianTime().Date;
                ShowDayDetails(selectedDate);
            }
            catch (Exception)
            {
                MessageBox.Show("تعذر حذف الموعد حالياً.", "المواعيد", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DayDetailsBackdrop_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DayDetailsOverlay.Visibility = Visibility.Collapsed;
        }

        private async void BtnDeleteAppointment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || !int.TryParse(button.Tag?.ToString(), out int appointmentId))
                return;

            MessageBoxResult result = MessageBox.Show(
                "هل أنت متأكد من حذف هذا الموعد؟",
                "تأكيد حذف الموعد",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No,
                MessageBoxOptions.RightAlign);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                using var db = new AppDbContext();
                AppointmentEntry? appointment = await db.Appointments.FindAsync(appointmentId);
                if (appointment is null)
                    return;

                db.Appointments.Remove(appointment);
                await db.SaveChangesAsync();

                _allAppointments.RemoveAll(a => a.Id == appointmentId);
                UpdateSearchSuggestions();
                ApplyAppointmentsFilter();
            }
            catch (Exception)
            {
                MessageBox.Show("تعذر حذف الموعد حالياً.", "المواعيد", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CmbAppointmentsSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded) return;
            ApplyAppointmentsFilter();
        }

        private void CmbAppointmentsSearch_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            ApplyAppointmentsFilter();
        }

        private void AppointmentDialogBackdrop_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            HideAppointmentDialog();
        }

        private void PopulateTimeOptions()
        {
            CmbHour.Items.Clear();
            for (int i = 1; i <= 12; i++)
                CmbHour.Items.Add(i.ToString("00"));

            CmbMinute.Items.Clear();
            for (int i = 0; i < 60; i += 15)
                CmbMinute.Items.Add(i.ToString("00"));

            CmbAmPm.Items.Clear();
            CmbAmPm.Items.Add("AM");
            CmbAmPm.Items.Add("PM");

            if (CmbHour.Items.Count > 0) CmbHour.SelectedIndex = 7;
            if (CmbMinute.Items.Count > 0) CmbMinute.SelectedIndex = 0;
            if (CmbAmPm.Items.Count > 0) CmbAmPm.SelectedIndex = 0;
        }

        private void ReplaceAppointments(IEnumerable<AppointmentCardModel> items)
        {
            _appointments.Clear();
            foreach (AppointmentCardModel item in items)
            {
                _appointments.Add(item);
            }
        }

        private void ApplyAppointmentsFilter()
        {
            DateTime syrianNow = GetSyrianTime();
            DateTime selectedDate = (DpAppointmentsFilter.SelectedDate ?? syrianNow.Date).Date;

            string searchText = CmbAppointmentsSearch.Text?.Trim().ToLowerInvariant() ?? "";
            bool isSearching = !string.IsNullOrWhiteSpace(searchText);

            var query = _allAppointments.AsEnumerable();

            if (_viewMode == ViewMode.Monthly)
            {
                RenderMonthlyCalendar(selectedDate, searchText, isSearching);
                UpdateButtonStyles();
                return;
            }

            DailyAppointmentsPanel.Visibility = Visibility.Visible;
            MonthlyCalendarPanel.Visibility = Visibility.Collapsed;

            if (!isSearching)
            {
                query = query.Where(a => a.AppointmentDateTime.Date == selectedDate.Date);
            }

            if (_statusFilter == StatusFilter.Upcoming)
            {
                query = query.Where(a => a.AppointmentDateTime >= syrianNow);
            }
            else if (_statusFilter == StatusFilter.Past)
            {
                query = query.Where(a => a.AppointmentDateTime < syrianNow);
            }

            if (isSearching)
            {
                query = query.Where(a => (a.PatientName?.ToLowerInvariant().Contains(searchText) == true) ||
                                         (a.PhoneNumber?.Contains(searchText) == true));
            }

            List<AppointmentCardModel> visibleAppointments = query
                .OrderBy(a => Math.Abs((a.AppointmentDateTime - syrianNow).TotalSeconds))
                .ThenBy(a => a.AppointmentDateTime)
                .Select(MapAppointment)
                .ToList();

            ReplaceAppointments(visibleAppointments);
            EmptyAppointmentsState.Visibility = _appointments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            UpdateButtonStyles();
        }

        private void RenderMonthlyCalendar(DateTime selectedDate, string searchText, bool isSearching)
        {
            DailyAppointmentsPanel.Visibility = Visibility.Collapsed;
            MonthlyCalendarPanel.Visibility = Visibility.Visible;
            EmptyAppointmentsState.Visibility = Visibility.Collapsed;
            CalendarMonthLabel.Text = $"{GetArabicMonthName(selectedDate.Month)} {selectedDate.Year}";

            CalendarDaysGrid.Children.Clear();
            CalendarDaysGrid.ColumnDefinitions.Clear();
            CalendarDaysGrid.RowDefinitions.Clear();

            for (int column = 0; column < 7; column++)
                CalendarDaysGrid.ColumnDefinitions.Add(new ColumnDefinition());

            IEnumerable<AppointmentEntry> monthAppointments = _allAppointments
                .Where(a => a.AppointmentDateTime.Year == selectedDate.Year
                         && a.AppointmentDateTime.Month == selectedDate.Month);

            if (isSearching)
            {
                monthAppointments = monthAppointments.Where(a =>
                    (a.PatientName?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true)
                    || (a.PhoneNumber?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true));
            }

            Dictionary<DateTime, List<AppointmentEntry>> appointmentsByDay = monthAppointments
                .GroupBy(a => a.AppointmentDateTime.Date)
                .ToDictionary(group => group.Key, group => group.OrderBy(a => a.AppointmentDateTime).ToList());

            DateTime firstDay = new DateTime(selectedDate.Year, selectedDate.Month, 1);
            int daysInMonth = DateTime.DaysInMonth(selectedDate.Year, selectedDate.Month);
            int firstColumn = (int)firstDay.DayOfWeek;
            int weekCount = (firstColumn + daysInMonth + 6) / 7;

            for (int row = 0; row < weekCount; row++)
                CalendarDaysGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(116) });

            for (int dayNumber = 1; dayNumber <= daysInMonth; dayNumber++)
            {
                int index = firstColumn + dayNumber - 1;
                int column = index % 7;
                int row = index / 7;
                DateTime day = new DateTime(selectedDate.Year, selectedDate.Month, dayNumber);
                AddCalendarDay(day, column, row, appointmentsByDay.TryGetValue(day, out var dayAppointments)
                    ? dayAppointments
                    : Array.Empty<AppointmentEntry>());
            }
        }

        private void AddCalendarDay(DateTime day, int column, int row, IReadOnlyList<AppointmentEntry> dayAppointments)
        {
            DateTime today = GetSyrianTime().Date;
            bool isToday = day == today;
            bool isDark = IsDarkThemeActive();

            var dayBorder = new Border
            {
                Tag = day,
                Margin = new Thickness(4),
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(12),
                Background = isToday
                    ? (Brush)FindResource("DashCalendarTodayBg")
                    : (Brush)FindResource("DashCalendarDayBg"),
                BorderBrush = isToday
                    ? (Brush)FindResource("DashCalendarTodayBorder")
                    : (Brush)FindResource("DashCalendarDayBorder"),
                BorderThickness = new Thickness(isToday ? 1.6 : 1.0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            dayBorder.MouseLeftButtonUp += CalendarDay_MouseLeftButtonUp;

            var dayGrid = new Grid { FlowDirection = FlowDirection.LeftToRight };
            dayGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            dayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ترويسة خلية اليوم: رقم اليوم في اليسار، وشارة (اليوم) في اليمين عند الحاجة
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dayNumberText = new TextBlock
            {
                Text = day.Day.ToString(CultureInfo.InvariantCulture),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = isToday
                    ? (Brush)FindResource("DashCalendarTodayNumFg")
                    : (Brush)FindResource("AppTextPrimary"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dayNumberText, 0);
            headerGrid.Children.Add(dayNumberText);

            if (isToday && !isDark)
            {
                var todayBadge = new Border
                {
                    Background = (Brush)FindResource("DashCalendarTodayBadgeBg"),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(8, 2, 8, 2),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "اليوم",
                        Foreground = Brushes.White,
                        FontSize = 10.5,
                        FontWeight = FontWeights.Bold
                    }
                };
                Grid.SetColumn(todayBadge, 1);
                headerGrid.Children.Add(todayBadge);
            }

            Grid.SetRow(headerGrid, 0);
            dayGrid.Children.Add(headerGrid);

            // قائمة المواعيد داخل اليوم
            var contentGrid = new Grid { Margin = new Thickness(0, 5, 0, 0), FlowDirection = FlowDirection.LeftToRight };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var appointmentsPanel = new StackPanel { FlowDirection = FlowDirection.RightToLeft };
            foreach (AppointmentEntry appointment in dayAppointments)
            {
                bool isAbsent = appointment.ShowAttendanceStatus
                    && string.Equals(appointment.AttendanceStatus, "Absent", StringComparison.Ordinal);
                bool isWaiting = appointment.ShowAttendanceStatus
                    && string.Equals(appointment.AttendanceStatus, "Waiting", StringComparison.Ordinal);
                bool isGreenVariant = !isAbsent && !isWaiting && (
                    string.Equals(appointment.Reason, "معاينة جديدة", StringComparison.Ordinal)
                    || (appointment.ShowAttendanceStatus && string.Equals(appointment.AttendanceStatus, "Attended", StringComparison.Ordinal))
                    || appointment.IsVisitRecord);

                Brush pillBg;
                Brush pillBorder;
                Brush pillFg;
                Brush deleteFg;

                if (isAbsent)
                {
                    pillBg = CreateBrush(isDark ? "#BA6571" : "#FB7A6D");
                    pillBorder = CreateBrush(isDark ? "#D47A87" : "#F06457");
                    pillFg = CreateBrush(isDark ? "#FFF1F2" : "#2B0908");
                    deleteFg = CreateBrush(isDark ? "#FFE4E6" : "#5C110E");
                }
                else if (isWaiting)
                {
                    pillBg = CreateBrush(isDark ? "#514511" : "#FEF9C3");
                    pillBorder = CreateBrush(isDark ? "#806C0E" : "#FDE68A");
                    pillFg = CreateBrush(isDark ? "#FEF3C7" : "#713F12");
                    deleteFg = CreateBrush(isDark ? "#FDE68A" : "#A16207");
                }
                else if (isGreenVariant)
                {
                    pillBg = CreateBrush(isDark ? "#0D514F" : "#B2DCCC");
                    pillBorder = CreateBrush(isDark ? "#14857E" : "#96CEB8");
                    pillFg = CreateBrush(isDark ? "#E6FFFA" : "#0F291E");
                    deleteFg = CreateBrush(isDark ? "#F87171" : "#B91C1C");
                }
                else
                {
                    pillBg = CreateBrush(isDark ? "#15203E" : "#CAD4F0");
                    pillBorder = CreateBrush(isDark ? "#25386B" : "#B2C0E8");
                    pillFg = CreateBrush(isDark ? "#E2E8F0" : "#1E293B");
                    deleteFg = CreateBrush(isDark ? "#F87171" : "#B91C1C");
                }

                string statusLine = isAbsent ? "\nلم يحضر" : string.Empty;

                var appointmentBorder = new Border
                {
                    Background = pillBg,
                    BorderBrush = pillBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(5, 3, 6, 3),
                    Margin = new Thickness(0, 0, 0, 4)
                };

                var appointmentRow = new Grid();
                appointmentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                appointmentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var appointmentText = new TextBlock
                {
                    Text = $"{appointment.AppointmentDateTime.ToString("hh:mm tt", CultureInfo.InvariantCulture)} - {appointment.Reason}\n{appointment.PatientName}{statusLine}",
                    FontSize = 9.8,
                    FontWeight = FontWeights.Bold,
                    Foreground = pillFg,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Right,
                    ToolTip = $"{appointment.PatientName} - {appointment.Reason}{statusLine}"
                };
                Grid.SetColumn(appointmentText, 0);
                appointmentRow.Children.Add(appointmentText);

                if (!appointment.IsVisitRecord)
                {
                    var deleteButton = new Button
                    {
                        Content = "×",
                        Tag = appointment.Id,
                        Width = 18,
                        Height = 18,
                        Padding = new Thickness(0),
                        Background = Brushes.Transparent,
                        Foreground = deleteFg,
                        BorderThickness = new Thickness(0),
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = "حذف الموعد"
                    };
                    deleteButton.Click += BtnDeleteAppointment_Click;
                    Grid.SetColumn(deleteButton, 1);
                    appointmentRow.Children.Add(deleteButton);
                }

                appointmentBorder.Child = appointmentRow;
                appointmentsPanel.Children.Add(appointmentBorder);
            }

            var appointmentsScroll = new ScrollViewer
            {
                Content = appointmentsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetColumn(appointmentsScroll, 1);
            contentGrid.Children.Add(appointmentsScroll);

            Grid.SetRow(contentGrid, 1);
            dayGrid.Children.Add(contentGrid);

            dayBorder.Child = dayGrid;
            Grid.SetColumn(dayBorder, column);
            Grid.SetRow(dayBorder, row);
            CalendarDaysGrid.Children.Add(dayBorder);
        }

        private void UpdateButtonStyles()
        {
            bool isDark = IsDarkThemeActive();

            Brush activeFilterBg = (Brush)FindResource("DashActiveFilterBg");
            Brush inactiveBg = Brushes.Transparent;
            Brush activeFg = Brushes.White;
            Brush primaryText = (Brush)FindResource("AppTextPrimary");
            Brush secondaryText = (Brush)FindResource("AppTextSecondary");

            // أزرار التصفية (القادمة | السابقة | الكل)
            BtnFilterUpcoming.Background = _statusFilter == StatusFilter.Upcoming ? activeFilterBg : inactiveBg;
            BtnFilterUpcoming.Foreground = _statusFilter == StatusFilter.Upcoming ? activeFg : primaryText;

            BtnFilterPast.Background = _statusFilter == StatusFilter.Past ? activeFilterBg : inactiveBg;
            BtnFilterPast.Foreground = _statusFilter == StatusFilter.Past ? activeFg : primaryText;

            BtnFilterAll.Background = _statusFilter == StatusFilter.All ? activeFilterBg : inactiveBg;
            BtnFilterAll.Foreground = _statusFilter == StatusFilter.All ? activeFg : primaryText;

            // أزرار العرض (شهري | يومي) كما في الصور المرفقة
            Brush activeViewBg = isDark ? Brushes.Transparent : Brushes.White;
            BtnMonthlyView.Background = _viewMode == ViewMode.Monthly ? activeViewBg : inactiveBg;
            BtnMonthlyView.Foreground = _viewMode == ViewMode.Monthly ? primaryText : secondaryText;

            BtnDailyView.Background = _viewMode == ViewMode.Daily ? activeViewBg : inactiveBg;
            BtnDailyView.Foreground = _viewMode == ViewMode.Daily ? primaryText : secondaryText;

            DateTime syrianToday = GetSyrianTime().Date;
            DateTime selectedDate = (DpAppointmentsFilter.SelectedDate ?? syrianToday).Date;

            bool isCurrent = _viewMode == ViewMode.Monthly
                ? (selectedDate.Month == syrianToday.Month && selectedDate.Year == syrianToday.Year)
                : (selectedDate == syrianToday);

            BtnCurrentDate.Visibility = isCurrent ? Visibility.Collapsed : Visibility.Visible;
            BtnCurrentDate.Content = _viewMode == ViewMode.Monthly ? "الشهر الحالي" : "اليوم الحالي";
        }

        private static AppointmentCardModel MapAppointment(AppointmentEntry appointment)
        {
            bool isNewConsultation = string.Equals(appointment.Reason, "معاينة جديدة", StringComparison.Ordinal);

            return new AppointmentCardModel
            {
                PatientName = appointment.PatientName,
                AppointmentId = appointment.Id,
                PhoneNumber = appointment.PhoneNumber,
                Reason = appointment.Reason,
                AppointmentDateText = appointment.AppointmentDateTime.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                AppointmentTimeText = appointment.AppointmentDateTime.ToString("hh:mm tt", CultureInfo.InvariantCulture),
                AttendanceStatus = appointment.AttendanceStatus,
                AttendanceStatusText = appointment.AttendanceStatusText,
                ShowAttendanceStatus = appointment.ShowAttendanceStatus,
                ReasonBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isNewConsultation ? "#EAF1FF" : "#F0FDF4")),
                ReasonForeground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isNewConsultation ? "#235DF6" : "#15803D"))
            };
        }

        private void UpdateAppointmentAttendance(AppointmentEntry appointment)
        {
            appointment.AttendanceStatus = string.Empty;
            appointment.AttendanceStatusText = string.Empty;

            if (appointment.IsVisitRecord || appointment.IsAutomaticallyGenerated)
                return;

            DateTime? attendedAt = appointment.CreatedAt is DateTime createdAt
                ? _visitAttendanceRecords
                    .Where(visit => IsSamePatient(appointment, visit)
                        && visit.VisitDate.Date == appointment.AppointmentDateTime.Date
                        && visit.VisitDate > createdAt)
                    .OrderBy(visit => visit.VisitDate)
                    .Select(visit => (DateTime?)visit.VisitDate)
                    .FirstOrDefault()
                : null;

            if (attendedAt.HasValue)
            {
                appointment.AttendanceStatus = "Attended";
                appointment.AttendanceStatusText =
                    $"✓ حضر الساعة {attendedAt.Value.ToString("hh:mm tt", CultureInfo.InvariantCulture)}";
                return;
            }

            if (appointment.AppointmentDateTime < GetSyrianTime())
            {
                appointment.AttendanceStatus = "Absent";
                appointment.AttendanceStatusText = "✕ لم يحضر";
                return;
            }

            appointment.AttendanceStatus = "Waiting";
            appointment.AttendanceStatusText = "◷ لم يأت بعد";
        }

        private static int? FindPatientId(string patientName, string phoneNumber, IEnumerable<Patient> patients)
        {
            List<Patient> matches = patients
                .Where(patient => string.Equals(patient.FullName?.Trim(), patientName.Trim(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(patient.PhoneNumber?.Trim(), phoneNumber.Trim(), StringComparison.Ordinal))
                .Take(2)
                .ToList();

            return matches.Count == 1 ? matches[0].Id : null;
        }

        private static bool IsSamePatient(AppointmentEntry first, AppointmentEntry second)
        {
            if (first.PatientId is int firstPatientId && second.PatientId is int secondPatientId)
            {
                if (firstPatientId == secondPatientId)
                    return true;
            }

            return string.Equals(first.PhoneNumber?.Trim(), second.PhoneNumber?.Trim(), StringComparison.Ordinal)
                && string.Equals(first.PatientName?.Trim(), second.PatientName?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSamePatient(AppointmentEntry appointment, Visit visit)
        {
            if (appointment.PatientId is int patientId)
            {
                if (patientId == visit.PatientId)
                    return true;
            }

            return string.Equals(appointment.PhoneNumber?.Trim(), visit.Patient?.PhoneNumber?.Trim(), StringComparison.Ordinal)
                && string.Equals(appointment.PatientName?.Trim(), visit.Patient?.FullName?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSamePatient(AppointmentEntry appointment, VisitAttendanceRecord visit)
        {
            if (appointment.PatientId is int patientId && patientId == visit.PatientId)
                return true;

            return string.Equals(appointment.PhoneNumber?.Trim(), visit.PhoneNumber.Trim(), StringComparison.Ordinal)
                && string.Equals(appointment.PatientName?.Trim(), visit.PatientName.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildCalendarKey(string? phoneNumber, DateTime dateTime) =>
            $"{phoneNumber ?? string.Empty}|{dateTime.Ticks}";

        private string GetSelectedAppointmentReason()
        {
            return (CmbAppointmentReason.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "معاينة جديدة";
        }

        private static string GetArabicMonthName(int month)
        {
            return month switch
            {
                1 => "يناير",
                2 => "فبراير",
                3 => "مارس",
                4 => "أبريل",
                5 => "مايو",
                6 => "يونيو",
                7 => "يوليو",
                8 => "أغسطس",
                9 => "سبتمبر",
                10 => "أكتوبر",
                11 => "نوفمبر",
                12 => "ديسمبر",
                _ => month.ToString(CultureInfo.InvariantCulture)
            };
        }

        private void ShowAppointmentDialog()
        {
            AppointmentDialogOverlay.Visibility = Visibility.Visible;
        }

        private void HideAppointmentDialog()
        {
            AppointmentDialogOverlay.Visibility = Visibility.Collapsed;
            ResetAppointmentForm();
        }

        private void ResetAppointmentForm()
        {
            _selectedAppointmentPatientId = null;
            CmbAppointmentPatientName.Text = string.Empty;
            CmbAppointmentPhoneNumber.Text = string.Empty;
            CmbAppointmentReason.SelectedIndex = 0;
            DpAppointmentDate.SelectedDate = GetSyrianTime().Date;

            if (CmbHour.Items.Count > 0) CmbHour.SelectedIndex = 7;
            if (CmbMinute.Items.Count > 0) CmbMinute.SelectedIndex = 0;
            if (CmbAmPm.Items.Count > 0) CmbAmPm.SelectedIndex = 0;
        }

        private void SetLoadingState(bool isLoading)
        {
            LoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public sealed class AppointmentCardModel
    {
        public int AppointmentId { get; init; }
        public string PatientName { get; init; } = string.Empty;
        public string PhoneNumber { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
        public string AppointmentDateText { get; init; } = string.Empty;
        public string AppointmentTimeText { get; init; } = string.Empty;
        public string AttendanceStatus { get; init; } = string.Empty;
        public string AttendanceStatusText { get; init; } = string.Empty;
        public bool ShowAttendanceStatus { get; init; }
        public Brush ReasonBackground { get; init; } = Brushes.Transparent;
        public Brush ReasonForeground { get; init; } = Brushes.Black;
    }

    internal sealed record VisitAttendanceRecord(
        int PatientId,
        string PhoneNumber,
        string PatientName,
        DateTime VisitDate);
}
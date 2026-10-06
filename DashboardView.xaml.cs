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
        private readonly List<string> _appointmentNameSuggestions = new();
        private readonly List<string> _appointmentPhoneSuggestions = new();
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

            string username = LoginSessionStore.CurrentUsername;
            TxtWelcome.Text = string.IsNullOrWhiteSpace(username)
                ? "أهلاً بك"
                : $"أهلاً د. {username}";

            LoadFileKitCanalCount();
            _isLoadingFileKitCanalCount = false;

            _viewMode = ViewMode.Monthly;
            _statusFilter = StatusFilter.Upcoming;

            AppointmentsItemsControl.ItemsSource = _appointments;
            
            DateTime syrianNow = GetSyrianTime();
            DpAppointmentsFilter.SelectedDate = syrianNow.Date;
            DpAppointmentDate.SelectedDate = syrianNow.Date;
            
            PopulateTimeOptions();
            UpdateButtonStyles();

            GlobalEvents.OnPatientRecordAdded += HandlePatientRecordAdded;
            GlobalEvents.OnFileKitCanalCountChanged += HandleFileKitCanalCountChanged;
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
            settings.UpdatedAt = DateTime.Now;
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
                settings.UpdatedAt = DateTime.Now;
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
            return DateTime.Now.AddHours(7);
        }

        public void RequestRefresh()
        {
            _refreshRequested = true;
        }

        public void AddSavedVisitImmediately(string patientName, string phoneNumber, string reason, DateTime visitDateTime, int visitId)
        {
            AppointmentEntry savedVisit = new()
            {
                Id = -Math.Abs(visitId),
                PatientName = string.IsNullOrWhiteSpace(patientName) ? "بدون اسم" : patientName,
                PhoneNumber = phoneNumber ?? string.Empty,
                Reason = string.IsNullOrWhiteSpace(reason) ? "مراجعة" : reason,
                AppointmentDateTime = visitDateTime,
                IsVisitRecord = true
            };

            string key = BuildCalendarKey(savedVisit.PhoneNumber, savedVisit.AppointmentDateTime);
            _allAppointments.RemoveAll(a => a.IsVisitRecord
                && BuildCalendarKey(a.PhoneNumber, a.AppointmentDateTime) == key);
            _allAppointments.Add(savedVisit);
            _hasLoadedData = true;
            _refreshRequested = false;
            ApplyAppointmentsFilter();
            UpdateSearchSuggestions();
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

                var appointmentKeys = appointments
                    .Select(appointment => BuildCalendarKey(appointment.PhoneNumber, appointment.AppointmentDateTime))
                    .ToHashSet(StringComparer.Ordinal);
                var visitNumberByPatient = new Dictionary<int, int>();

                foreach (Visit visit in visits)
                {
                    string phoneNumber = visit.Patient?.PhoneNumber ?? string.Empty;
                    string key = BuildCalendarKey(phoneNumber, visit.VisitDate);
                    visitNumberByPatient.TryGetValue(visit.PatientId, out int visitNumber);
                    visitNumber++;
                    visitNumberByPatient[visit.PatientId] = visitNumber;

                    if (appointmentKeys.Contains(key))
                        continue;

                    appointments.Add(new AppointmentEntry
                    {
                        Id = -visit.Id,
                        PatientName = visit.Patient?.FullName ?? "بدون اسم",
                        PhoneNumber = phoneNumber,
                        Reason = visitNumber == 1 ? "معاينة جديدة" : "مراجعة",
                        AppointmentDateTime = visit.VisitDate,
                        IsVisitRecord = true
                    });
                }

                List<Patient> patients = await db.Patients
                    .AsNoTracking()
                    .ToListAsync();

                _allAppointments.Clear();
                _allAppointments.AddRange(appointments);
                
                _allPatients.Clear();
                _allPatients.AddRange(patients);
                
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

            RefreshAppointmentSuggestions();
        }

        private void CmbAppointmentPhoneNumber_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded || _isUpdatingAppointmentFields || _isUpdatingAppointmentSuggestions)
                return;

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

                // Replacing ItemsSource on an editable ComboBox can cause WPF to
                // select all text in PART_EditableTextBox. Keep the user's caret
                // at the end so continued typing edits the existing name instead
                // of replacing it.
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

            // Defer until ComboBox finishes applying the new item source, which
            // is when its default select-all behavior can run.
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

            // تجميع الوقت من منتقي الوقت (Time Picker)
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
                    AppointmentDateTime = appointmentDateTime
                };

                using var db = new AppDbContext();
                db.Appointments.Add(newAppointment);

                await db.SaveChangesAsync();

                _allAppointments.Add(newAppointment);

                // إخفاء النافذة يقوم بالتصفير تلقائياً بفضل استدعاء ResetAppointmentForm بداخلها
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

            // Clicking an appointment's delete button must not also open the add dialog.
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
            for (int i = 0; i < 60; i += 15) // فواصل كل 15 دقيقة 
                CmbMinute.Items.Add(i.ToString("00"));

            CmbAmPm.Items.Clear();
            CmbAmPm.Items.Add("AM");
            CmbAmPm.Items.Add("PM");

            // القيم الافتراضية
            if (CmbHour.Items.Count > 0) CmbHour.SelectedIndex = 7; // الساعة 08 الافتراضية
            if (CmbMinute.Items.Count > 0) CmbMinute.SelectedIndex = 0; // الدقيقة 00
            if (CmbAmPm.Items.Count > 0) CmbAmPm.SelectedIndex = 0; // ص AM
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

            for (int row = 0; row < 6; row++)
                CalendarDaysGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(112) });

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

            for (int index = 0; index < 42; index++)
            {
                int column = index % 7;
                int row = index / 7;
                int dayNumber = index - firstColumn + 1;

                if (dayNumber < 1 || dayNumber > daysInMonth)
                {
                    AddCalendarPlaceholder(column, row);
                    continue;
                }

                DateTime day = new DateTime(selectedDate.Year, selectedDate.Month, dayNumber);
                AddCalendarDay(day, column, row, appointmentsByDay.TryGetValue(day, out var dayAppointments)
                    ? dayAppointments
                    : Array.Empty<AppointmentEntry>());
            }
        }

        private void AddCalendarPlaceholder(int column, int row)
        {
            var placeholder = new Border
            {
                Margin = new Thickness(4),
                Background = Brushes.Transparent
            };
            Grid.SetColumn(placeholder, column);
            Grid.SetRow(placeholder, row);
            CalendarDaysGrid.Children.Add(placeholder);
        }

        private void AddCalendarDay(DateTime day, int column, int row, IEnumerable<AppointmentEntry> dayAppointments)
        {
            DateTime today = GetSyrianTime().Date;
            bool isToday = day == today;

            var dayBorder = new Border
            {
                Tag = day,
                Margin = new Thickness(4),
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(12),
                Background = isToday
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A235DF6"))
                    : (Brush)FindResource("AppInputBg"),
                BorderBrush = isToday
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#235DF6"))
                    : (Brush)FindResource("AppBorder"),
                BorderThickness = new Thickness(isToday ? 2 : 1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            dayBorder.MouseLeftButtonUp += CalendarDay_MouseLeftButtonUp;

            var dayGrid = new Grid { FlowDirection = FlowDirection.RightToLeft };
            dayGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            dayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var dayNumberText = new TextBlock
            {
                Text = day.Day.ToString(CultureInfo.InvariantCulture),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = isToday
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#235DF6"))
                    : (Brush)FindResource("AppTextPrimary"),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(dayNumberText, 0);
            dayGrid.Children.Add(dayNumberText);

            var appointmentsPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (AppointmentEntry appointment in dayAppointments)
            {
                var appointmentRow = new Grid
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#14235DF6")),
                    Margin = new Thickness(0, 0, 0, 4)
                };
                appointmentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                appointmentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var appointmentText = new TextBlock
                {
                    Text = $"{appointment.AppointmentDateTime:hh\\:mm tt} - {appointment.Reason}\\n{appointment.PatientName}",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("AppTextPrimary"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 3, 0, 3),
                    ToolTip = $"{appointment.PatientName} - {appointment.Reason}"
                };
                Grid.SetColumn(appointmentText, 0);

                appointmentRow.Children.Add(appointmentText);
                if (!appointment.IsVisitRecord)
                {
                    var deleteButton = new Button
                    {
                        Content = "×",
                        Tag = appointment.Id,
                        Width = 20,
                        Height = 20,
                        Padding = new Thickness(0),
                        Background = Brushes.Transparent,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626")),
                        BorderThickness = new Thickness(0),
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        ToolTip = "حذف الموعد"
                    };
                    deleteButton.Click += BtnDeleteAppointment_Click;
                    Grid.SetColumn(deleteButton, 1);
                    appointmentRow.Children.Add(deleteButton);
                }
                appointmentsPanel.Children.Add(appointmentRow);
            }

            var appointmentsScroll = new ScrollViewer
            {
                Content = appointmentsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(appointmentsScroll, 1);
            dayGrid.Children.Add(appointmentsScroll);

            dayBorder.Child = dayGrid;
            Grid.SetColumn(dayBorder, column);
            Grid.SetRow(dayBorder, row);
            CalendarDaysGrid.Children.Add(dayBorder);
        }

        private void UpdateButtonStyles()
        {
            Brush activeBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#235DF6"));
            Brush inactiveBg = Brushes.Transparent;
            Brush activeFg = Brushes.White;
            Brush inactiveFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5C6C82"));

            BtnMonthlyView.Background = _viewMode == ViewMode.Monthly ? activeBg : inactiveBg;
            BtnMonthlyView.Foreground = _viewMode == ViewMode.Monthly ? activeFg : inactiveFg;
            
            BtnDailyView.Background = _viewMode == ViewMode.Daily ? activeBg : inactiveBg;
            BtnDailyView.Foreground = _viewMode == ViewMode.Daily ? activeFg : inactiveFg;

            BtnFilterUpcoming.Background = _statusFilter == StatusFilter.Upcoming ? activeBg : inactiveBg;
            BtnFilterUpcoming.Foreground = _statusFilter == StatusFilter.Upcoming ? activeFg : inactiveFg;

            BtnFilterPast.Background = _statusFilter == StatusFilter.Past ? activeBg : inactiveBg;
            BtnFilterPast.Foreground = _statusFilter == StatusFilter.Past ? activeFg : inactiveFg;

            BtnFilterAll.Background = _statusFilter == StatusFilter.All ? activeBg : inactiveBg;
            BtnFilterAll.Foreground = _statusFilter == StatusFilter.All ? activeFg : inactiveFg;

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
                ReasonBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isNewConsultation ? "#EAF1FF" : "#F0FDF4")),
                ReasonForeground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isNewConsultation ? "#235DF6" : "#15803D"))
            };
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
            ResetAppointmentForm(); // تم وضع التصفير هنا ليعمل في كافة طرق الإغلاق
        }

        private void ResetAppointmentForm()
        {
            CmbAppointmentPatientName.Text = string.Empty;
            CmbAppointmentPhoneNumber.Text = string.Empty;
            CmbAppointmentReason.SelectedIndex = 0;
            DpAppointmentDate.SelectedDate = GetSyrianTime().Date;
            
            // تصفير وقت الموعد
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
        public Brush ReasonBackground { get; init; } = Brushes.Transparent;
        public Brush ReasonForeground { get; init; } = Brushes.Black;
    }
}

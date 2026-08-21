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
        private bool _refreshRequested = true;
        private bool _hasLoadedData;
        private Task? _refreshTask;

        private ViewMode _viewMode;
        private StatusFilter _statusFilter;

        public DashboardView()
        {
            InitializeComponent();

            string username = LoginSessionStore.CurrentUsername;
            TxtWelcome.Text = string.IsNullOrWhiteSpace(username)
                ? "أهلاً بك"
                : $"أهلاً د. {username}";

            _viewMode = ViewMode.Monthly;
            _statusFilter = StatusFilter.Upcoming;

            AppointmentsItemsControl.ItemsSource = _appointments;
            
            DateTime syrianNow = GetSyrianTime();
            DpAppointmentsFilter.SelectedDate = syrianNow.Date;
            DpAppointmentDate.SelectedDate = syrianNow.Date;
            
            PopulateTimeOptions();
            UpdateButtonStyles();
        }

        private DateTime GetSyrianTime()
        {
            return DateTime.Now.AddHours(7);
        }

        public void RequestRefresh()
        {
            _refreshRequested = true;
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
            var appointmentNames = _allAppointments.Select(a => a.PatientName).Where(s => !string.IsNullOrWhiteSpace(s));
            var patientNames = _allPatients.Select(p => p.FullName).Where(s => !string.IsNullOrWhiteSpace(s));
            var allNames = appointmentNames.Concat(patientNames).Distinct().ToList();

            var appointmentPhones = _allAppointments.Select(a => a.PhoneNumber).Where(s => !string.IsNullOrWhiteSpace(s));
            var patientPhones = _allPatients.Select(p => p.PhoneNumber).Where(s => !string.IsNullOrWhiteSpace(s));
            var allPhones = appointmentPhones.Concat(patientPhones).Distinct().ToList();

            var allSuggestions = allNames.Concat(allPhones).Distinct().ToList();

            CmbAppointmentsSearch.ItemsSource = allSuggestions;
            CmbAppointmentPatientName.ItemsSource = allNames;
            CmbAppointmentPhoneNumber.ItemsSource = allPhones;
        }

        private void CmbAppointmentPatientName_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is string selectedName)
            {
                var patientMatch = _allPatients.FirstOrDefault(p => p.FullName == selectedName);
                if (patientMatch != null && !string.IsNullOrWhiteSpace(patientMatch.PhoneNumber))
                {
                    CmbAppointmentPhoneNumber.Text = patientMatch.PhoneNumber;
                    return;
                }

                var appMatch = _allAppointments.FirstOrDefault(a => a.PatientName == selectedName);
                if (appMatch != null && !string.IsNullOrWhiteSpace(appMatch.PhoneNumber))
                {
                    CmbAppointmentPhoneNumber.Text = appMatch.PhoneNumber;
                }
            }
        }

        private void CmbAppointmentPhoneNumber_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is string selectedPhone)
            {
                var patientMatch = _allPatients.FirstOrDefault(p => p.PhoneNumber == selectedPhone);
                if (patientMatch != null && !string.IsNullOrWhiteSpace(patientMatch.FullName))
                {
                    CmbAppointmentPatientName.Text = patientMatch.FullName;
                    return;
                }

                var appMatch = _allAppointments.FirstOrDefault(a => a.PhoneNumber == selectedPhone);
                if (appMatch != null && !string.IsNullOrWhiteSpace(appMatch.PatientName))
                {
                    CmbAppointmentPatientName.Text = appMatch.PatientName;
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

            if (!isSearching)
            {
                if (_viewMode == ViewMode.Monthly)
                {
                    query = query.Where(a => a.AppointmentDateTime.Month == selectedDate.Month && 
                                             a.AppointmentDateTime.Year == selectedDate.Year);
                }
                else
                {
                    query = query.Where(a => a.AppointmentDateTime.Date == selectedDate.Date);
                }
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
                PhoneNumber = appointment.PhoneNumber,
                Reason = appointment.Reason,
                AppointmentDateText = appointment.AppointmentDateTime.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                AppointmentTimeText = appointment.AppointmentDateTime.ToString("hh:mm tt", CultureInfo.InvariantCulture),
                ReasonBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isNewConsultation ? "#EAF1FF" : "#F0FDF4")),
                ReasonForeground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isNewConsultation ? "#235DF6" : "#15803D"))
            };
        }

        private string GetSelectedAppointmentReason()
        {
            return (CmbAppointmentReason.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "معاينة جديدة";
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
        public string PatientName { get; init; } = string.Empty;
        public string PhoneNumber { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
        public string AppointmentDateText { get; init; } = string.Empty;
        public string AppointmentTimeText { get; init; } = string.Empty;
        public Brush ReasonBackground { get; init; } = Brushes.Transparent;
        public Brush ReasonForeground { get; init; } = Brushes.Black;
    }
}

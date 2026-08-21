using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MyClinic.Services;

namespace MyClinic
{
    public partial class MainWindow : Window
    {
        private DashboardView? _dashboardView;
        private PatientRecordsView? _patientRecordsView;
        private FinancialRecordsView? _financialRecordsView;
        private ShortagesView? _shortagesView;
        private LabView? _labView;
        private SettingsView? _settingsView;
        private StatisticsView? _statisticsView;
        
        // متغيرات المزامنة التلقائية والساعة
        private DispatcherTimer _clockTimer;
        private bool _isAutoSyncEnabled = false;
        private int _lastSyncedHour = -1;
        private readonly string _syncSettingsPath;
        private readonly string _themeSettingsPath;
        private UpdateInfo? _availableUpdate;

        public MainWindow()
        {
            // 1. تحديد المسار الآمن الجديد (AppData)
            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string clinicFolder = Path.Combine(appDataFolder, "MyClinicApp");
            
            // التأكد من وجود المجلد
            if (!Directory.Exists(clinicFolder))
            {
                Directory.CreateDirectory(clinicFolder);
            }

            // 2. توجيه ملفات الإعدادات إلى المجلد الآمن
            _syncSettingsPath = Path.Combine(clinicFolder, "SyncConfig.txt");
            _themeSettingsPath = Path.Combine(clinicFolder, "ThemeConfig.txt");

            // 3. فحص وتطبيق الاستعادة قبل تشغيل أي شيء أو الاتصال بقاعدة البيانات
            ApplyPendingRestore();

            InitializeComponent();
            
            // تحميل الثيم المحفوظ
            LoadThemeSettings();
            
            // تهيئة إعدادات المزامنة التلقائية والساعة
            LoadSyncSettings();
            InitializeAppClock();

            ShowDashboard();
            TxtCurrentVersion.Text = $"الإصدار {UpdateService.CurrentVersion.ToString(3)}";
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_Loaded;
            try
            {
                UpdateInfo? update = await UpdateService.GetLatestUpdateAsync();
                if (update is null)
                    return;

                _availableUpdate = update;
                TxtUpdateMessage.Text = $"الإصدار {update.Version} متاح الآن — حجم الملف: {update.SizeText}";
                UpdateBanner.Visibility = Visibility.Visible;
            }
            catch
            {
                // An update check must never prevent the clinic app from opening.
            }
        }

        private void BtnDownloadUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_availableUpdate is null)
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = _availableUpdate.DownloadUrl,
                UseShellExecute = true
            });
        }

        private void BtnDismissUpdate_Click(object sender, RoutedEventArgs e)
        {
            UpdateBanner.Visibility = Visibility.Collapsed;
        }

        private void InitializeAppClock()
        {
            _clockTimer = new DispatcherTimer();
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += ClockTimer_Tick;
            _clockTimer.Start();
        }

        private async void ClockTimer_Tick(object? sender, EventArgs e)
        {
            DateTime appTime = DateTime.Now.AddHours(7);
            
            TxtAppTime.Text = appTime.ToString("hh:mm:ss tt", new System.Globalization.CultureInfo("ar-SY"));
            TxtAppDate.Text = appTime.ToString("yyyy/MM/dd");
            TxtAppDay.Text = appTime.ToString("dddd", new System.Globalization.CultureInfo("ar-SY"));

            if (_isAutoSyncEnabled)
            {
                if ((appTime.Hour == 10 || appTime.Hour == 13 || appTime.Hour == 19) && appTime.Minute == 0)
                {
                    if (_lastSyncedHour != appTime.Hour)
                    {
                        _lastSyncedHour = appTime.Hour;
                        await ExecuteAutoSyncAsync(appTime);
                    }
                }
                else if (appTime.Minute != 0)
                {
                    _lastSyncedHour = -1;
                }

                UpdateNextSyncUI();
            }
        }

        private async Task ExecuteAutoSyncAsync(DateTime appTime)
        {
            var result = await GoogleDriveSyncService.BackupDatabaseAsync();
            if (result.Success)
            {
                UpdateLastSyncUI(appTime);
            }
        }

        private void UpdateLastSyncUI(DateTime time)
        {
            string formattedTime = time.ToString("yyyy/MM/dd hh:mm tt", new System.Globalization.CultureInfo("ar-SY"));
            TxtLastSync.Text = $"آخر مزامنة: {formattedTime}";
            SaveSyncSettings(formattedTime);
        }

        private void UpdateNextSyncUI()
        {
            if (!_isAutoSyncEnabled)
            {
                TxtNextSync.Visibility = Visibility.Collapsed;
                return;
            }

            TxtNextSync.Visibility = Visibility.Visible;
            DateTime appTime = DateTime.Now.AddHours(7);
            DateTime nextSyncTime = appTime.Date;

            if (appTime.Hour < 10)
                nextSyncTime = nextSyncTime.AddHours(10);
            else if (appTime.Hour < 13)
                nextSyncTime = nextSyncTime.AddHours(13);
            else if (appTime.Hour < 19)
                nextSyncTime = nextSyncTime.AddHours(19);
            else
                nextSyncTime = nextSyncTime.AddDays(1).AddHours(10);

            TxtNextSync.Text = $"المزامنة القادمة: {nextSyncTime.ToString("hh:mm tt", new System.Globalization.CultureInfo("ar-SY"))}";
        }

        private void LoadSyncSettings()
        {
            if (File.Exists(_syncSettingsPath))
            {
                var lines = File.ReadAllLines(_syncSettingsPath);
                if (lines.Length >= 2)
                {
                    bool.TryParse(lines[0], out _isAutoSyncEnabled);
                    AutoSyncToggle.IsChecked = _isAutoSyncEnabled;
                    TxtLastSync.Text = $"آخر مزامنة: {lines[1]}";
                }
            }
            UpdateNextSyncUI();
        }

        private void SaveSyncSettings(string lastSyncDateStr)
        {
            try
            {
                File.WriteAllLines(_syncSettingsPath, new[] { _isAutoSyncEnabled.ToString(), lastSyncDateStr });
            }
            catch { }
        }

        private void AutoSyncToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            _isAutoSyncEnabled = true;
            UpdateNextSyncUI();
            SaveSyncSettings(TxtLastSync.Text.Replace("آخر مزامنة: ", ""));
        }

        private void AutoSyncToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            _isAutoSyncEnabled = false;
            UpdateNextSyncUI();
            SaveSyncSettings(TxtLastSync.Text.Replace("آخر مزامنة: ", ""));
        }

        private void ApplyPendingRestore()
        {
            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string clinicFolder = Path.Combine(appDataFolder, "MyClinicApp");

            string dbPath = Path.Combine(clinicFolder, "ClinicData.db");
            string tempDbPath = Path.Combine(clinicFolder, "ClinicData_Temp.db");

            if (File.Exists(tempDbPath))
            {
                try
                {
                    // Check file size to ensure it's not empty
                    long fileSize = new FileInfo(tempDbPath).Length;
                    if (fileSize < 1024) // Less than 1KB is suspicious
                    {
                        MessageBox.Show($"ملف الاستعادة صغير جداً ({fileSize} bytes). قد يكون تالفاً أو فارغاً.", "تحذير", MessageBoxButton.OK, MessageBoxImage.Warning);
                        File.Delete(tempDbPath);
                        return;
                    }

                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    
                    // Close any open connections
                    System.Threading.Thread.Sleep(500); // Give time for connections to close
                    
                    if (File.Exists(dbPath))
                    {
                        string backupPath = Path.Combine(clinicFolder, $"ClinicData_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.db");
                        File.Copy(dbPath, backupPath, true);
                        File.Delete(dbPath); 
                    }
                    
                    File.Move(tempDbPath, dbPath); 
                    
                    MessageBox.Show("تم تطبيق النسخة المستعادة بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("حدث خطأ أثناء تطبيق النسخة المستعادة: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadThemeSettings()
        {
            bool isDark = false;
            if (File.Exists(_themeSettingsPath))
            {
                var content = File.ReadAllText(_themeSettingsPath).Trim();
                bool.TryParse(content, out isDark);
            }
            
            ThemeToggle.IsChecked = isDark;
            SetTheme(isDark);
        }

        private void SaveThemeSettings(bool isDark)
        {
            try
            {
                File.WriteAllText(_themeSettingsPath, isDark.ToString());
            }
            catch { }
        }

        private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            SetTheme(true);
            SaveThemeSettings(true);
        }

        private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            SetTheme(false);
            SaveThemeSettings(false);
        }

        private void SetTheme(bool isDark)
        {
            if (isDark)
            {
                Application.Current.Resources["AppWindowBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B1120"));
                Application.Current.Resources["AppSidebarBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#151A25"));
                Application.Current.Resources["AppSidebarHoverBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2532"));
                Application.Current.Resources["AppCardBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                Application.Current.Resources["AppInputBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
                Application.Current.Resources["AppBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                Application.Current.Resources["AppTextPrimary"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
                Application.Current.Resources["AppTextSecondary"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
            }
            else
            {
                Application.Current.Resources["AppWindowBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F4F6F8"));
                Application.Current.Resources["AppSidebarBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#151A25"));
                Application.Current.Resources["AppSidebarHoverBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2532"));
                Application.Current.Resources["AppCardBg"] = new SolidColorBrush(Colors.White);
                Application.Current.Resources["AppInputBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFD"));
                Application.Current.Resources["AppBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E4EAF1"));
                Application.Current.Resources["AppTextPrimary"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#13284A"));
                Application.Current.Resources["AppTextSecondary"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C8A9A"));
            }
        }

        private void BtnDashboard_Click(object sender, RoutedEventArgs e) => ShowDashboard();
        private void BtnPatients_Click(object sender, RoutedEventArgs e) => ShowPatientRecords();
        private void BtnFinancials_Click(object sender, RoutedEventArgs e) => ShowFinancialRecords();
        
        // زر النواقص الجديد
        private void BtnShortages_Click(object sender, RoutedEventArgs e) => ShowShortages();
        
        // زر المخبر الجديد
        private void BtnLab_Click(object sender, RoutedEventArgs e) => ShowLab();

        // زر الإعدادات الجديد
        private void BtnSettings_Click(object sender, RoutedEventArgs e) => ShowSettings();

        // زر الإحصائيات الجديد
        private void BtnStatistics_Click(object sender, RoutedEventArgs e) => ShowStatistics();

        private async void BtnSyncDrive_Click(object sender, RoutedEventArgs e)
        {
            BtnSyncDrive.IsEnabled = false;
            BtnSyncDrive.Content = "جاري المزامنة...";

            var result = await GoogleDriveSyncService.BackupDatabaseAsync();

            if (result.Success)
            {
                MessageBox.Show("تم عمل نسخة احتياطية من البيانات على Google Drive بنجاح!", "نجاح المزامنة", MessageBoxButton.OK, MessageBoxImage.Information);
                DateTime appTime = DateTime.Now.AddHours(7);
                UpdateLastSyncUI(appTime);
            }
            else
            {
                MessageBox.Show($"فشلت عملية المزامنة. السبب:\n{result.ErrorMessage}", "خطأ في المزامنة", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            BtnSyncDrive.Content = "مزامنة مع Google Drive";
            BtnSyncDrive.IsEnabled = true;
        }

        private async void BtnRestoreDrive_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult confirmResult = MessageBox.Show(
                "تحذير: استعادة البيانات ستؤدي إلى حذف البيانات الحالية واستبدالها بالنسخة الموجودة على Google Drive. هل أنت متأكد أنك تريد المتابعة؟",
                "تأكيد الاستعادة",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No,
                MessageBoxOptions.RightAlign);

            if (confirmResult == MessageBoxResult.Yes)
            {
                BtnRestoreDrive.IsEnabled = false;
                BtnRestoreDrive.Content = "جاري الاستعادة...";

                var result = await GoogleDriveSyncService.RestoreDatabaseAsync();

                if (result.Success)
                {
                    MessageBox.Show("تم تحميل النسخة الاحتياطية بنجاح! سيتم الآن إعادة تشغيل البرنامج لتطبيق البيانات الجديدة.", "نجاح الاستعادة", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    var processPath = Environment.ProcessPath;
                    if (processPath != null)
                    {
                        System.Diagnostics.Process.Start(processPath);
                    }
                    Application.Current.Shutdown();
                }
                else
                {
                    MessageBox.Show($"فشلت عملية الاستعادة. السبب:\n{result.ErrorMessage}", "خطأ في الاستعادة", MessageBoxButton.OK, MessageBoxImage.Error);
                    BtnRestoreDrive.Content = "استعادة البيانات";
                    BtnRestoreDrive.IsEnabled = true;
                }
            }
        }

        public void ShowDashboard()
        {
            _dashboardView ??= new DashboardView();
            MainContent.Content = _dashboardView;
            _dashboardView.RequestRefresh();
            _ = _dashboardView.EnsureDataCurrentAsync();
            SetActiveNavigation(BtnDashboard);
        }

        public void ShowAddPatient()
        {
            MainContent.Content = new AddPatientView();
            SetActiveNavigation(BtnDashboard);
        }

        public void ShowPatientRecords()
        {
            _patientRecordsView ??= new PatientRecordsView();
            MainContent.Content = _patientRecordsView;
            _patientRecordsView.RequestRefresh();
            _ = _patientRecordsView.EnsureDataCurrentAsync();
            SetActiveNavigation(BtnPatients);
        }

        public void ShowFinancialRecords()
        {
            _financialRecordsView ??= new FinancialRecordsView();
            MainContent.Content = _financialRecordsView;
            _financialRecordsView.RequestRefresh();
            _ = _financialRecordsView.EnsureDataCurrentAsync();
            SetActiveNavigation(BtnFinancials);
        }

        // إظهار واجهة النواقص الجديدة
        public void ShowShortages()
        {
            _shortagesView ??= new ShortagesView();
            MainContent.Content = _shortagesView;
            SetActiveNavigation(BtnShortages);
        }

        // إظهار واجهة المخبر الجديدة
        public void ShowLab()
        {
            _labView ??= new LabView();
            MainContent.Content = _labView;
            SetActiveNavigation(BtnLab);
        }

        // إظهار واجهة الإعدادات الجديدة
        public void ShowSettings()
        {
            _settingsView ??= new SettingsView();
            MainContent.Content = _settingsView;
            SetActiveNavigation(BtnSettings);
        }

        // إظهار واجهة الإحصائيات الجديدة
        public void ShowStatistics()
        {
            _statisticsView ??= new StatisticsView();
            MainContent.Content = _statisticsView;
            SetActiveNavigation(BtnStatistics);
        }

        public void InvalidatePatientRecords() => _patientRecordsView?.RequestRefresh();
        public void InvalidateFinancialRecords() => _financialRecordsView?.RequestRefresh();

        private void SetActiveNavigation(Button activeButton)
        {
            BtnDashboard.Style = (Style)FindResource("SidebarButton");
            BtnPatients.Style = (Style)FindResource("SidebarButton");
            BtnFinancials.Style = (Style)FindResource("SidebarButton");
            BtnShortages.Style = (Style)FindResource("SidebarButton");
            BtnLab.Style = (Style)FindResource("SidebarButton");
            BtnSettings.Style = (Style)FindResource("SidebarButton");
            BtnStatistics.Style = (Style)FindResource("SidebarButton");

            activeButton.Style = (Style)FindResource("SidebarActiveButton");
        }
    }
}

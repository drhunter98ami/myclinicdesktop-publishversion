using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MyClinic
{
    public partial class SettingsView : UserControl
    {
        private readonly ObservableCollection<TreatmentCost> _treatmentCosts = new();
        private int? _editingTreatmentId;

        public SettingsView()
        {
            InitializeComponent();
            TreatmentItemsControl.ItemsSource = _treatmentCosts;
            Loaded += SettingsView_Loaded;
            
            // Set default currency selection
            CmbTreatmentCurrency.SelectedIndex = 0;
        }

        private async void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadExchangeRate();
            LoadFileKitCanalCount();
            if (Window.GetWindow(this) is MainWindow mainWindow)
                ThemeToggle.IsChecked = mainWindow.IsDarkTheme;
            await LoadTreatmentsAsync();
        }

        private void LoadFileKitCanalCount()
        {
            using var db = new AppDbContext();
            var count = db.AppSettings.Select(s => (int?)s.FileKitCanalCount).FirstOrDefault() ?? 0;
            TxtFileKitCanalCount.Text = $"{count:N0} قناة";
        }

        private void BtnResetFileKitCanals_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("هل تريد تصفير عدد الأقنية لهذا الكيت؟", "تأكيد التصفير", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            using var db = new AppDbContext();
            var settings = db.AppSettings.FirstOrDefault() ?? new AppSettings();
            if (settings.Id == 0) db.AppSettings.Add(settings);
            settings.FileKitCanalCount = 0;
            settings.UpdatedAt = DateTime.Now;
            db.SaveChanges();
            TxtFileKitCanalCount.Text = "0 قناة";
            GlobalEvents.NotifyFileKitCanalCountChanged();
        }

        private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
                mainWindow.SetDarkTheme(true);
        }

        private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
                mainWindow.SetDarkTheme(false);
        }

        private async void BtnRestoreDrive_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
                await mainWindow.RestoreDatabaseFromDriveAsync();
        }

        private void LoadExchangeRate()
        {
            try
            {
                using var db = new AppDbContext();
                var settings = db.AppSettings.FirstOrDefault();
                if (settings == null)
                {
                    settings = new AppSettings();
                    db.AppSettings.Add(settings);
                    db.SaveChanges();
                }

                TxtExchangeRate.Text = settings.UsdToSypRate.ToString("N0", CultureInfo.CurrentCulture);
                TxtCurrentExchangeRate.Text = $"{settings.UsdToSypRate:N0} ل.س";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في تحميل سعر الصرف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveExchangeRate_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(TxtExchangeRate.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var rate) || rate <= 0)
            {
                MessageBox.Show("يرجى إدخال سعر صرف صحيح.", "قيمة غير صحيحة", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtExchangeRate.Focus();
                TxtExchangeRate.SelectAll();
                return;
            }

            try
            {
                using var db = new AppDbContext();
                var settings = db.AppSettings.FirstOrDefault();
                if (settings == null)
                {
                    settings = new AppSettings { UsdToSypRate = rate };
                    db.AppSettings.Add(settings);
                }
                else
                {
                    settings.UsdToSypRate = rate;
                    settings.UpdatedAt = DateTime.Now;
                }

                db.SaveChanges();
                TxtExchangeRate.Text = rate.ToString("N0", CultureInfo.CurrentCulture);
                TxtCurrentExchangeRate.Text = $"{rate:N0} ل.س";
                GlobalEvents.NotifyExchangeRateChanged();
                MessageBox.Show("تم حفظ سعر الصرف وتحديث التبويبات المرتبطة.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في حفظ سعر الصرف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadTreatmentsAsync()
        {
            try
            {
                using var db = new AppDbContext();
                var treatments = await db.TreatmentCosts
                    .OrderBy(t => t.TreatmentName)
                    .ToListAsync();

                _treatmentCosts.Clear();
                foreach (var treatment in treatments)
                {
                    _treatmentCosts.Add(treatment);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في تحميل العلاجات: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnAddTreatment_Click(object sender, RoutedEventArgs e)
        {
            string treatmentName = TxtTreatmentName.Text.Trim();
            string costText = TxtTreatmentCost.Text.Trim();
            string currency = CmbTreatmentCurrency.SelectedItem is ComboBoxItem item ? item.Content?.ToString() ?? "SYP" : "SYP";

            if (string.IsNullOrWhiteSpace(treatmentName))
            {
                MessageBox.Show("يرجى إدخال اسم العلاج.", "بيانات ناقصة", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtTreatmentName.Focus();
                return;
            }

            if (!decimal.TryParse(costText, out decimal cost) || cost <= 0)
            {
                MessageBox.Show("يرجى إدخال تكلفة صحيحة.", "قيمة غير صحيحة", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtTreatmentCost.Focus();
                TxtTreatmentCost.SelectAll();
                return;
            }

            try
            {
                using var db = new AppDbContext();
                if (_editingTreatmentId is int editingId)
                {
                    var treatment = await db.TreatmentCosts.FindAsync(editingId);
                    if (treatment == null) return;

                    treatment.Cost = cost;
                    treatment.Currency = currency;
                    await db.SaveChangesAsync();
                    await LoadTreatmentsAsync();
                    ResetTreatmentEditor();
                    return;
                }

                var newTreatment = new TreatmentCost
                {
                    TreatmentName = treatmentName,
                    Cost = cost,
                    Currency = currency
                };

                db.TreatmentCosts.Add(newTreatment);
                await db.SaveChangesAsync();

                _treatmentCosts.Add(newTreatment);

                TxtTreatmentName.Clear();
                TxtTreatmentCost.Clear();
                TxtTreatmentName.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في إضافة العلاج: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnEditTreatment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not int treatmentId)
                return;

            var treatment = _treatmentCosts.FirstOrDefault(t => t.Id == treatmentId);
            if (treatment == null) return;

            _editingTreatmentId = treatmentId;
            TxtTreatmentName.Text = treatment.TreatmentName;
            TxtTreatmentName.IsReadOnly = true;
            TxtTreatmentCost.Text = treatment.Cost.ToString(CultureInfo.CurrentCulture);
            CmbTreatmentCurrency.SelectedIndex = treatment.Currency == "USD" ? 1 : 0;
            BtnAddTreatment.Content = "حفظ التعديل";
            BtnCancelTreatmentEdit.Visibility = Visibility.Visible;
            TxtTreatmentCost.Focus();
            TxtTreatmentCost.SelectAll();
        }

        private void BtnCancelTreatmentEdit_Click(object sender, RoutedEventArgs e)
        {
            ResetTreatmentEditor();
        }

        private void ResetTreatmentEditor()
        {
            _editingTreatmentId = null;
            TxtTreatmentName.Clear();
            TxtTreatmentName.IsReadOnly = false;
            TxtTreatmentCost.Clear();
            CmbTreatmentCurrency.SelectedIndex = 0;
            BtnAddTreatment.Content = "إضافة";
            BtnCancelTreatmentEdit.Visibility = Visibility.Collapsed;
        }

        private async void BtnDeleteTreatment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int treatmentId)
            {
                var result = MessageBox.Show(
                    "هل أنت متأكد من حذف هذا العلاج؟",
                    "تأكيد الحذف",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        using var db = new AppDbContext();
                        var treatment = await db.TreatmentCosts.FindAsync(treatmentId);
                        if (treatment != null)
                        {
                            db.TreatmentCosts.Remove(treatment);
                            await db.SaveChangesAsync();

                            var localTreatment = _treatmentCosts.FirstOrDefault(t => t.Id == treatmentId);
                            if (localTreatment != null)
                            {
                                _treatmentCosts.Remove(localTreatment);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"خطأ في حذف العلاج: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
    }
}

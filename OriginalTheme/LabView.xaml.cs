using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MyClinic.OriginalTheme
{
    public partial class LabView : UserControl
    {
        private List<LabWork> _allLabWorks = new List<LabWork>();
        private decimal _usdToSypRate = 15000;
        private string _currentDateFilter = "all"; // all, day, month, year

        public LabView()
        {
            InitializeComponent();
            LoadExchangeRate();
            GlobalEvents.OnExchangeRateChanged += OnExchangeRateChanged;
            LoadLabNames();
            LoadLabWorks();
            
            // Set default filter to monthly
            _currentDateFilter = "month";
            DpFilterDate.SelectedDate = AppClock.Today;
            BtnCurrentDate.Visibility = Visibility.Visible;
            UpdateButtonStyles();
            ApplyFilters();
        }

        public void RefreshDateForAppTimeChange(DateTime previousAppTime)
        {
            if (DpFilterDate.SelectedDate?.Date == previousAppTime.Date)
                DpFilterDate.SelectedDate = AppClock.Today;

            ApplyFilters();
        }

        internal void ReleaseResources()
        {
            GlobalEvents.OnExchangeRateChanged -= OnExchangeRateChanged;
        }

        private void OnExchangeRateChanged()
        {
            LoadExchangeRate();
            ApplyFilters();
        }

        private void LoadLabNames()
        {
            try
            {
                using var context = new AppDbContext();
                var labNames = context.LabNames.OrderBy(l => l.Name).ToList();
                CmbLabName.ItemsSource = labNames;
                CmbLabName.DisplayMemberPath = "Name";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في تحميل أسماء المخابر: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadLabWorks()
        {
            try
            {
                using var context = new AppDbContext();
                _allLabWorks = context.LabWorks.OrderByDescending(l => l.DateSent).ToList();
                DgLabWorks.ItemsSource = _allLabWorks;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في تحميل البيانات: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadExchangeRate()
        {
            try
            {
                using var context = new AppDbContext();
                var settings = context.AppSettings.FirstOrDefault();
                if (settings != null)
                {
                    _usdToSypRate = settings.UsdToSypRate;
                }
                else
                {
                    // Create default settings if not exists
                    var newSettings = new AppSettings { UsdToSypRate = 15000 };
                    context.AppSettings.Add(newSettings);
                    context.SaveChanges();
                    _usdToSypRate = 15000;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في تحميل سعر الصرف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateSummary()
        {
            try
            {
                var labWorks = _allLabWorks.Where(IsInSelectedPeriod);

                // Total paid includes both "تم الدفع" and "تم الدفع ولم يتم الاستلام"
                decimal totalPaid = labWorks.Where(l => l.Status == "تم الدفع" || l.Status == "تم الدفع ولم يتم الاستلام").Sum(l => l.Cost);
                // Total required only includes received works (تم الإستلام or تم الدفع), not paid but not received
                decimal totalRequired = labWorks.Where(l => l.Status == "تم الإستلام" || l.Status == "تم الدفع").Sum(l => l.Cost);
                decimal remaining = totalRequired - totalPaid;

                TxtTotalPaid.Text = $"${totalPaid:F2}";
                TxtTotalRequired.Text = $"${totalRequired:F2}";
                TxtRemaining.Text = $"${remaining:F2}";

                // Update SYP amounts
                decimal totalPaidSYP = totalPaid * _usdToSypRate;
                decimal totalRequiredSYP = totalRequired * _usdToSypRate;
                decimal remainingSYP = remaining * _usdToSypRate;

                TxtTotalPaidSYP.Text = $"{totalPaidSYP:N0} ل.س";
                TxtTotalRequiredSYP.Text = $"{totalRequiredSYP:N0} ل.س";
                TxtRemainingSYP.Text = $"{remainingSYP:N0} ل.س";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في تحديث الملخص: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAddLabName_Click(object sender, RoutedEventArgs e)
        {
            TxtNewLabName.Clear();
            AddLabNameDialogOverlay.Visibility = Visibility.Visible;
            TxtNewLabName.Focus();
        }

        private void AddLabNameDialogBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            AddLabNameDialogOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnCloseAddLabNameDialog_Click(object sender, RoutedEventArgs e)
        {
            AddLabNameDialogOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnCancelAddLabName_Click(object sender, RoutedEventArgs e)
        {
            AddLabNameDialogOverlay.Visibility = Visibility.Collapsed;
        }

        private async void BtnConfirmAddLabName_Click(object sender, RoutedEventArgs e)
        {
            string newLabName = TxtNewLabName.Text?.Trim();
            if (string.IsNullOrWhiteSpace(newLabName))
            {
                ValidationAssist.Show(TxtNewLabName, "يرجى إدخال اسم المخبر");
                return;
            }
            using var saveFeedback = SaveFeedback.Begin(this, sender);
            if (!saveFeedback.CanStart) return;
            await saveFeedback.ShowBusyAsync();


            try
            {
                using var context = new AppDbContext();
                // Check if name already exists
                if (context.LabNames.Any(l => l.Name == newLabName))
                {
                    ValidationAssist.Show(TxtNewLabName, "هذا الاسم موجود بالفعل");
                    return;
                }
                var labName = new LabName { Name = newLabName };
                context.LabNames.Add(labName);
                context.SaveChanges();
                LoadLabNames();
                CmbLabName.SelectedItem = labName;
                AddLabNameDialogOverlay.Visibility = Visibility.Collapsed;
                saveFeedback.Success("تم إضافة المخبر بنجاح");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في إضافة المخبر: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnAddWork_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtPatientName.Text))
            {
                ValidationAssist.Show(TxtPatientName, "يرجى إدخال اسم المريض");
                return;
            }

            if (!decimal.TryParse(TxtCost.Text, out decimal cost) || cost <= 0)
            {
                ValidationAssist.Show(TxtCost, "يرجى إدخال تكلفة صحيحة");
                return;
            }
            using var saveFeedback = SaveFeedback.Begin(this, sender);
            if (!saveFeedback.CanStart) return;
            await saveFeedback.ShowBusyAsync();


            try
            {
                using var context = new AppDbContext();
                var labWork = new LabWork
                {
                    PatientName = TxtPatientName.Text.Trim(),
                    LabName = CmbLabName.SelectedItem is LabName selectedLab ? selectedLab.Name : null,
                    Cost = cost,
                    Teeth = TxtTeeth.Text?.Trim(),
                    Status = "تم الإرسال",
                    DateSent = AppClock.Now,
                    Notes = TxtNotes.Text?.Trim()
                };

                context.LabWorks.Add(labWork);
                context.SaveChanges();

                // Clear inputs
                TxtPatientName.Clear();
                TxtCost.Clear();
                TxtTeeth.Clear();
                TxtNotes.Clear();
                CmbLabName.SelectedIndex = -1;

                LoadLabWorks();
                ApplyFilters();

                saveFeedback.Success("تم إضافة العمل بنجاح");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في إضافة العمل: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnMarkReceived_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int id)
            {
                try
                {
                    using var context = new AppDbContext();
                    var labWork = context.LabWorks.FirstOrDefault(l => l.Id == id);
                    if (labWork != null)
                    {
                        // If already paid but not received, change to final paid status
                        if (labWork.Status == "تم الدفع ولم يتم الاستلام")
                        {
                            labWork.Status = "تم الدفع";
                        }
                        else
                        {
                            labWork.Status = "تم الإستلام";
                        }
                        
                        labWork.DateReceived = AppClock.Now;
                        context.SaveChanges();
                        LoadLabWorks();
                        ApplyFilters();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"خطأ في تحديث الحالة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnMarkPaid_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int id)
            {
                try
                {
                    using var context = new AppDbContext();
                    var labWork = context.LabWorks.FirstOrDefault(l => l.Id == id);
                    if (labWork != null)
                    {
                        // Set status based on whether work has been received
                        if (labWork.Status == "تم الإستلام")
                        {
                            labWork.Status = "تم الدفع";
                        }
                        else
                        {
                            labWork.Status = "تم الدفع ولم يتم الاستلام";
                        }
                        
                        labWork.DatePaid = AppClock.Now;
                        labWork.AmountPaid = labWork.Cost;
                        
                        // add new expense
                        decimal costInSyp = labWork.Cost * _usdToSypRate;
                        var newExpense = new ExpenseEntry
                        {
                            ExpenseDate = AppClock.Now,
                            Description = $"تكلفة عمل مخبر - {labWork.LabName}",
                            Amount = (double)costInSyp
                        };
                        context.Expenses.Add(newExpense);
                        
                        context.SaveChanges();

                        // Notify financial tab
                        GlobalEvents.NotifyFinancialRecordAdded();
                        
                        LoadLabWorks();
                        ApplyFilters();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"خطأ في تحديث الحالة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void CmbStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            string searchTerm = TxtSearch.Text?.Trim().ToLower() ?? "";
            string? statusFilter = null;

            if (CmbStatusFilter.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                string content = item.Content.ToString() ?? "";
                if (content != "الكل")
                {
                    statusFilter = content;
                }
            }

            var filtered = _allLabWorks.Where(IsInSelectedPeriod);

            if (!string.IsNullOrEmpty(searchTerm))
            {
                filtered = filtered.Where(l => l.PatientName.ToLower().Contains(searchTerm));
            }

            if (!string.IsNullOrEmpty(statusFilter))
            {
                filtered = filtered.Where(l => l.Status == statusFilter);
            }

            DgLabWorks.ItemsSource = filtered.ToList();
            UpdateSummary();
        }

        private bool IsInSelectedPeriod(LabWork labWork)
        {
            if (_currentDateFilter == "all" || !DpFilterDate.SelectedDate.HasValue)
                return true;

            DateTime selectedDate = DpFilterDate.SelectedDate.Value;
            return _currentDateFilter switch
            {
                "day" => labWork.DateSent.Date == selectedDate.Date,
                "month" => labWork.DateSent.Year == selectedDate.Year && labWork.DateSent.Month == selectedDate.Month,
                "year" => labWork.DateSent.Year == selectedDate.Year,
                _ => true
            };
        }

        private void BtnDayRange_Click(object sender, RoutedEventArgs e)
        {
            _currentDateFilter = "day";
            DpFilterDate.SelectedDate = AppClock.Today;
            BtnCurrentDate.Visibility = Visibility.Visible;
            UpdateButtonStyles();
            ApplyFilters();
        }

        private void BtnMonthRange_Click(object sender, RoutedEventArgs e)
        {
            _currentDateFilter = "month";
            DpFilterDate.SelectedDate = AppClock.Today;
            BtnCurrentDate.Visibility = Visibility.Visible;
            UpdateButtonStyles();
            ApplyFilters();
            
            // Open calendar and set to month view
            DpFilterDate.IsDropDownOpen = true;
        }

        private void BtnYearRange_Click(object sender, RoutedEventArgs e)
        {
            _currentDateFilter = "year";
            DpFilterDate.SelectedDate = AppClock.Today;
            BtnCurrentDate.Visibility = Visibility.Visible;
            UpdateButtonStyles();
            ApplyFilters();
            
            // Open calendar and set to year view
            DpFilterDate.IsDropDownOpen = true;
        }

        private void BtnCurrentDate_Click(object sender, RoutedEventArgs e)
        {
            DpFilterDate.SelectedDate = AppClock.Today;
            ApplyFilters();
        }

        private void DpFilterDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DpFilterDate.SelectedDate.HasValue)
            {
                ApplyFilters();
            }
        }

        private void DpFilterDate_CalendarOpened(object sender, RoutedEventArgs e)
        {
            // Set calendar display mode based on current filter
            var datePicker = (DatePicker)sender;
            if (datePicker != null)
            {
                var popup = datePicker.Template.FindName("PART_Popup", datePicker) as System.Windows.Controls.Primitives.Popup;
                if (popup != null && popup.Child is System.Windows.Controls.Calendar calendar)
                {
                    calendar.DisplayModeChanged -= Calendar_DisplayModeChanged;
                    calendar.DisplayModeChanged += Calendar_DisplayModeChanged;

                    if (_currentDateFilter == "month")
                    {
                        calendar.DisplayMode = CalendarMode.Year;
                    }
                    else if (_currentDateFilter == "year")
                    {
                        calendar.DisplayMode = CalendarMode.Decade;
                    }
                    else
                    {
                        calendar.DisplayMode = CalendarMode.Month;
                    }
                }
            }
        }

        private void Calendar_DisplayModeChanged(object sender, CalendarModeChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.Calendar calendar)
            {
                if (_currentDateFilter == "month" && calendar.DisplayMode == CalendarMode.Month)
                {
                    DpFilterDate.SelectedDate = calendar.DisplayDate;
                    DpFilterDate.IsDropDownOpen = false;
                }
                else if (_currentDateFilter == "year" && calendar.DisplayMode == CalendarMode.Year)
                {
                    DpFilterDate.SelectedDate = calendar.DisplayDate;
                    DpFilterDate.IsDropDownOpen = false;
                }
            }
        }

        private void UpdateButtonStyles()
        {
            // Reset all buttons to default style
            BtnDayRange.Style = (Style)FindResource("RangeButtonStyle");
            BtnMonthRange.Style = (Style)FindResource("RangeButtonStyle");
            BtnYearRange.Style = (Style)FindResource("RangeButtonStyle");

            // Apply selected style to current filter button
            switch (_currentDateFilter)
            {
                case "day":
                    BtnDayRange.Style = (Style)FindResource("RangeButtonSelectedStyle");
                    break;
                case "month":
                    BtnMonthRange.Style = (Style)FindResource("RangeButtonSelectedStyle");
                    break;
                case "year":
                    BtnYearRange.Style = (Style)FindResource("RangeButtonSelectedStyle");
                    break;
            }
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;

                var childResult = FindVisualChild<T>(child);
                if (childResult != null)
                    return childResult;
            }
            return null;
        }
    }
}

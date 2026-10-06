using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MyClinic
{
    public partial class ShortagesView : UserControl
    {
        public ObservableCollection<ShortageRowModel> UrgentItems { get; set; }
        public ObservableCollection<ShortageRowModel> NonUrgentItems { get; set; }

        private decimal _usdToSypRate = 15000;
        private ShortageRowModel? _editingItem;

        public ShortagesView()
        {
            InitializeComponent();

            UrgentItems = new ObservableCollection<ShortageRowModel>();
            NonUrgentItems = new ObservableCollection<ShortageRowModel>();

            LoadExchangeRate();
            LoadData();

            ListUrgent.ItemsSource = UrgentItems;
            ListNonUrgent.ItemsSource = NonUrgentItems;

            UrgentItems.CollectionChanged += (_, _) => UpdateTotals();
            NonUrgentItems.CollectionChanged += (_, _) => UpdateTotals();

            GlobalEvents.OnExchangeRateChanged += OnExchangeRateChanged;
        }

        private void OnExchangeRateChanged()
        {
            LoadExchangeRate();
            RefreshItemsDisplay();
            UpdateTotals();
        }

        private void RefreshItemsDisplay()
        {
            foreach (var item in UrgentItems)
            {
                item.PriceText = item.Price == 0
                    ? "بدون سعر"
                    : $"{item.Price.ToString("0.##", CultureInfo.CurrentCulture)} {item.Currency}";
            }

            foreach (var item in NonUrgentItems)
            {
                item.PriceText = item.Price == 0
                    ? "بدون سعر"
                    : $"{item.Price.ToString("0.##", CultureInfo.CurrentCulture)} {item.Currency}";
            }
        }

        private void LoadExchangeRate()
        {
            try
            {
                using var context = new AppDbContext();
                var settings = context.AppSettings.AsNoTracking().FirstOrDefault();
                if (settings != null)
                {
                    _usdToSypRate = settings.UsdToSypRate;
                }
            }
            catch
            {
            }
        }

        private decimal ConvertToSyp(decimal price, string currency)
        {
            return string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)
                ? price * _usdToSypRate
                : price;
        }

        private void UpdateTotals()
        {
            decimal urgentTotal = UrgentItems.Sum(i => ConvertToSyp(i.Price, i.Currency) * i.Quantity);
            decimal nonUrgentTotal = NonUrgentItems.Sum(i => ConvertToSyp(i.Price, i.Currency) * i.Quantity);

            TxtUrgentTotal.Text = $"{urgentTotal.ToString("N0", CultureInfo.CurrentCulture)} ل.س";
            TxtNonUrgentTotal.Text = $"{nonUrgentTotal.ToString("N0", CultureInfo.CurrentCulture)} ل.س";
        }

        private void BtnAddUrgent_Click(object sender, RoutedEventArgs e)
        {
            if (TryAddShortage(TxtUrgentInput, TxtUrgentPrice, TxtUrgentQuantity, CmbUrgentCurrency, isUrgent: true, out ShortageRowModel? row))
            {
                UrgentItems.Add(row);
            }
        }

        private void TxtUrgentInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                BtnAddUrgent_Click(sender, e);
        }

        private void BtnAddNonUrgent_Click(object sender, RoutedEventArgs e)
        {
            if (TryAddShortage(TxtNonUrgentInput, TxtNonUrgentPrice, TxtNonUrgentQuantity, CmbNonUrgentCurrency, isUrgent: false, out ShortageRowModel? row))
            {
                NonUrgentItems.Add(row);
            }
        }

        private void TxtNonUrgentInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                BtnAddNonUrgent_Click(sender, e);
        }

        private bool TryAddShortage(TextBox input, TextBox priceInput, TextBox quantityInput, ComboBox currencyCombo, bool isUrgent, out ShortageRowModel? row)
        {
            row = null;

            string text = input.Text.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            if (!decimal.TryParse(priceInput.Text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out decimal price) || price < 0)
            {
                MessageBox.Show("يرجى إدخال سعر صحيح.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                priceInput.Focus();
                return false;
            }

            int quantity = ParseQuantity(quantityInput.Text);
            if (quantity < 1)
            {
                MessageBox.Show("يرجى إدخال عدد صحيح أكبر من صفر.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                quantityInput.Focus();
                return false;
            }

            string currency = (currencyCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "SYP";

            try
            {
                using var context = new AppDbContext();
                var shortage = new Shortage
                {
                    Item = text,
                    IsUrgent = isUrgent,
                    Price = price,
                    Quantity = quantity,
                    Currency = currency,
                    CreatedAt = DateTime.Now
                };
                context.Shortages.Add(shortage);
                context.SaveChanges();

                row = MapRow(shortage);

                input.Clear();
                priceInput.Clear();
                quantityInput.Text = "1";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ أثناء إضافة النقص: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            return true;
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is ShortageRowModel itemToRemove)
            {
                try
                {
                    using var context = new AppDbContext();
                    var shortage = context.Shortages.FirstOrDefault(s => s.Id == itemToRemove.Id);
                    if (shortage != null)
                    {
                        context.Shortages.Remove(shortage);
                        context.SaveChanges();
                    }

                    if (UrgentItems.Contains(itemToRemove))
                    {
                        UrgentItems.Remove(itemToRemove);
                    }
                    else if (NonUrgentItems.Contains(itemToRemove))
                    {
                        NonUrgentItems.Remove(itemToRemove);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"خطأ أثناء حذف النقص: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is ShortageRowModel item)
            {
                _editingItem = item;
                TxtEditItemName.Text = item.Item;
                TxtEditPrice.Text = item.Price.ToString("0.##", CultureInfo.CurrentCulture);
                TxtEditQuantity.Text = item.Quantity.ToString(CultureInfo.CurrentCulture);
                CmbEditCurrency.SelectedIndex = item.Currency == "USD" ? 0 : 1;
                EditPriceDialogOverlay.Visibility = Visibility.Visible;
                TxtEditPrice.Focus();
            }
        }

        private void EditPriceDialogBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            EditPriceDialogOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnCloseEditPriceDialog_Click(object sender, RoutedEventArgs e)
        {
            EditPriceDialogOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnCancelEditPrice_Click(object sender, RoutedEventArgs e)
        {
            EditPriceDialogOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnConfirmEditPrice_Click(object sender, RoutedEventArgs e)
        {
            if (_editingItem == null) return;

            if (!decimal.TryParse(TxtEditPrice.Text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out decimal newPrice) || newPrice < 0)
            {
                MessageBox.Show("يرجى إدخال سعر صحيح.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int newQuantity = ParseQuantity(TxtEditQuantity.Text);
            if (newQuantity < 1)
            {
                MessageBox.Show("يرجى إدخال عدد صحيح أكبر من صفر.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtEditQuantity.Focus();
                return;
            }

            string newCurrency = (CmbEditCurrency.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "SYP";

            try
            {
                using var context = new AppDbContext();
                var shortage = context.Shortages.FirstOrDefault(s => s.Id == _editingItem.Id);
                if (shortage != null)
                {
                    shortage.Price = newPrice;
                    shortage.Quantity = newQuantity;
                    shortage.Currency = newCurrency;
                    context.SaveChanges();

                    // Update the item in the collection
                    var updatedRow = MapRow(shortage);

                    if (UrgentItems.Contains(_editingItem))
                    {
                        var index = UrgentItems.IndexOf(_editingItem);
                        UrgentItems[index] = updatedRow;
                    }
                    else if (NonUrgentItems.Contains(_editingItem))
                    {
                        var index = NonUrgentItems.IndexOf(_editingItem);
                        NonUrgentItems[index] = updatedRow;
                    }

                    UpdateTotals();
                    EditPriceDialogOverlay.Visibility = Visibility.Collapsed;
                    MessageBox.Show("تم تحديث السعر بنجاح", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ أثناء تحديث السعر: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnPurchased_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is ShortageRowModel item)
            {
                decimal amountInSyp = ConvertToSyp(item.Price, item.Currency) * item.Quantity;

                try
                {
                    using var context = new AppDbContext();

                    var newExpense = new ExpenseEntry
                    {
                        ExpenseDate = DateTime.Now,
                        Description = $"نواقص - {item.Item}",
                        Amount = (double)amountInSyp
                    };
                    context.Expenses.Add(newExpense);

                    var shortage = context.Shortages.FirstOrDefault(s => s.Id == item.Id);
                    if (shortage != null)
                    {
                        context.Shortages.Remove(shortage);
                    }

                    await context.SaveChangesAsync();

                    if (UrgentItems.Contains(item))
                        UrgentItems.Remove(item);
                    else if (NonUrgentItems.Contains(item))
                        NonUrgentItems.Remove(item);

                    GlobalEvents.NotifyFinancialRecordAdded();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"خطأ أثناء تسجيل الشراء: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadData()
        {
            try
            {
                using var context = new AppDbContext();
                var shortages = context.Shortages.AsNoTracking().OrderBy(s => s.CreatedAt).ToList();

                UrgentItems.Clear();
                NonUrgentItems.Clear();

                foreach (var shortage in shortages)
                {
                    var row = MapRow(shortage);
                    if (shortage.IsUrgent)
                    {
                        UrgentItems.Add(row);
                    }
                    else
                    {
                        NonUrgentItems.Add(row);
                    }
                }

                UpdateTotals();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ أثناء تحميل النواقص: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static ShortageRowModel MapRow(Shortage s)
        {
            int quantity = Math.Max(1, s.Quantity);
            string priceText = s.Price == 0
                ? $"العدد: {quantity} • بدون سعر"
                : $"{s.Price.ToString("0.##", CultureInfo.CurrentCulture)} {s.Currency} × {quantity} = {(s.Price * quantity).ToString("0.##", CultureInfo.CurrentCulture)} {s.Currency}";

            return new ShortageRowModel
            {
                Id = s.Id,
                Item = s.Item,
                Price = s.Price,
                Quantity = quantity,
                Currency = s.Currency,
                PriceText = priceText,
                CurrencyBadge = s.Currency
            };
        }

        private static int ParseQuantity(string? text)
        {
            string value = text?.Trim() ?? string.Empty;
            if (value.Length == 0)
            {
                return 1;
            }

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out int quantity)
                ? quantity
                : 0;
        }
    }

    public sealed class ShortageRowModel : System.ComponentModel.INotifyPropertyChanged
    {
        private string _priceText = string.Empty;

        public int Id { get; init; }
        public string Item { get; init; } = string.Empty;
        public decimal Price { get; init; }
        public int Quantity { get; init; } = 1;
        public string Currency { get; init; } = "SYP";
        public string PriceText
        {
            get => _priceText;
            set
            {
                _priceText = value;
                OnPropertyChanged(nameof(PriceText));
            }
        }
        public string CurrencyBadge { get; init; } = "SYP";

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}

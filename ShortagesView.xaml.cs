using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System.Windows;
using System.Windows.Controls;

namespace MyClinic;

public sealed class ShortageRowModel
{
    public Shortage ItemData { get; init; } = new();
    public int Id => ItemData.Id;
    public string Item => ItemData.Item;
    public string PriceText => ItemData.PriceKnown ? $"{ItemData.Quantity} × {ItemData.Price:N2} {ItemData.Currency} = {ItemData.Quantity * ItemData.Price:N2} {ItemData.Currency}" : $"الكمية: {ItemData.Quantity} · السعر غير معروف";
}

public partial class ShortagesView : UserControl
{
    List<Shortage> _items = new(); List<ClinicOperation> _history = new();
    int? _editingId, _undoId; int _editingQuantity; decimal _rate; bool _ready, _historyMode, _busy;
    readonly Func<AppDbContext> _createContext;
    public ShortagesView() : this(() => new AppDbContext()) { }
    public ShortagesView(Func<AppDbContext> createContext)
    {
        _createContext = createContext;
        InitializeComponent();
        Currency.ItemsSource = new[] { "SYP", "USD" }; Currency.SelectedIndex = 0;
        Priority.ItemsSource = new[] { "غير مستعجل", "مستعجل" }; Priority.SelectedIndex = 0;
        Sort.ItemsSource = new[] { "الأقدم أولاً", "الأعلى تكلفة", "الاسم" }; Sort.SelectedIndex = 0;
        _ready = true;
        Loaded += LoadedView; Unloaded += UnloadedView;
    }
    void LoadedView(object sender, RoutedEventArgs e)
    {
        WorkspaceUi.Configure(this); Refresh();
        GlobalEvents.OnExchangeRateChanged += RateChanged; GlobalEvents.OnThemeChanged += ThemeChanged;
        MotionPreferences.Changed += MotionChanged;
    }
    void UnloadedView(object sender, RoutedEventArgs e)
    { GlobalEvents.OnExchangeRateChanged -= RateChanged; GlobalEvents.OnThemeChanged -= ThemeChanged; MotionPreferences.Changed -= MotionChanged; }
    void ThemeChanged(bool dark) => WorkspaceUi.Configure(this);
    void MotionChanged(object? sender, EventArgs e) { if (!MotionPreferences.Enabled) FinanceMotion.StopTree(this); }
    void RateChanged() => Dispatcher.Invoke(Refresh);
    void Refresh()
    {
        try
        {
            using var db = _createContext(); _rate = WorkspaceUi.Rate(db);
            _items = db.Shortages.AsNoTracking().Where(s => !s.IsDeleted && s.Quantity > 0).ToList();
            _history = db.ClinicOperations.AsNoTracking().Where(o => o.Kind == "Purchase").OrderByDescending(o => o.OccurredAt).ToList();
            Filter();
        }
        catch (Exception ex) { Status.Text = "تعذر تحميل النواقص: " + ex.Message; }
    }
    void Filter()
    {
        if (!_ready) return;
        string search = Search.Text.Trim(); IEnumerable<Shortage> query = _items.Where(s => s.Item.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        query = Sort.SelectedIndex switch { 1 => query.OrderByDescending(s => s.PriceKnown ? s.Price * s.Quantity * (s.Currency == "USD" ? _rate : 1) : -1), 2 => query.OrderBy(s => s.Item), _ => query.OrderBy(s => s.CreatedAt) };
        var urgent = query.Where(s => s.IsUrgent).ToList(); var normal = query.Where(s => !s.IsUrgent).ToList();
        UrgentList.ItemsSource = urgent.Select(s => new ShortageRowModel { ItemData = s }).ToList();
        NormalList.ItemsSource = normal.Select(s => new ShortageRowModel { ItemData = s }).ToList();
        UrgentTitle.Text = $"نواقص مستعجلة · {urgent.Count}"; NormalTitle.Text = $"نواقص غير مستعجلة · {normal.Count}";
        Counts.Text = $"مستعجل: {_items.Count(s => s.IsUrgent)} · غير مستعجل: {_items.Count(s => !s.IsUrgent)}";
        UrgentEmpty.Visibility = urgent.Count == 0 ? Visibility.Visible : Visibility.Collapsed; NormalEmpty.Visibility = normal.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UrgentTotal.Text = Total(urgent); NormalTotal.Text = Total(normal);
        var history = _history.Where(o => o.Description.Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToList(); HistoryGrid.ItemsSource = history;
        HistoryEmpty.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Groups.Visibility = _historyMode ? Visibility.Collapsed : Visibility.Visible; HistoryCard.Visibility = _historyMode ? Visibility.Visible : Visibility.Collapsed;
        PendingButton.FontWeight = !_historyMode ? FontWeights.Bold : FontWeights.Normal; HistoryButton.FontWeight = _historyMode ? FontWeights.Bold : FontWeights.Normal;
        Sort.IsEnabled = !_historyMode;
    }
    string Total(List<Shortage> items)
    {
        decimal usd = items.Where(s => s.PriceKnown && s.Currency == "USD").Sum(s => s.Price * s.Quantity), syp = items.Where(s => s.PriceKnown && s.Currency == "SYP").Sum(s => s.Price * s.Quantity);
        int unknown = items.Count(s => !s.PriceKnown);
        return WorkspaceUi.Money(usd, "USD") + " · " + WorkspaceUi.Money(syp, "SYP") + "\nالتقدير بسعر الصرف الحالي: " + WorkspaceUi.Money(syp + usd * _rate, "SYP") + (unknown > 0 ? $"\n{unknown} مواد بلا سعر · غير مشمولة بالتقدير" : "");
    }
    void Filter_Changed(object sender, RoutedEventArgs e) => Filter();
    void Form_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        UnitPrice.IsEnabled = UnknownPrice.IsChecked != true;
        PreviewTotal.Text = UnknownPrice.IsChecked == true ? "الإجمالي غير محدد" : WorkspaceUi.Number(UnitPrice.Text, out var price) && WorkspaceUi.Number(Quantity.Text, out var count) ? $"الإجمالي: {price * count:N2} {Currency.SelectedItem}" : "أدخل السعر والكمية لحساب الإجمالي";
    }
    async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (string.IsNullOrWhiteSpace(ItemName.Text)) { ValidationAssist.Show(ItemName, "أدخل اسم الصنف."); return; }
        if (!int.TryParse(WorkspaceUi.Normalize(Quantity.Text), out int count) || count < 1 || count > 1_000_000) { ValidationAssist.Show(Quantity, "أدخل كمية صحيحة بين 1 و1000000."); return; }
        decimal price = 0; bool known = UnknownPrice.IsChecked != true;
        if (known && (!WorkspaceUi.Number(UnitPrice.Text, out price) || price <= 0 || price > 1_000_000_000m)) { ValidationAssist.Show(UnitPrice, "أدخل سعراً موجباً أو اختر السعر غير معروف."); return; }
        _busy = true; SaveItem.IsEnabled = false;
        try
        {
            using var db = _createContext();
            Shortage item;
            if (_editingId.HasValue)
            {
                int editingItemId = _editingId.Value; string name = ItemName.Text.Trim(), currency = Currency.SelectedItem?.ToString() ?? "SYP"; bool urgent = Priority.SelectedIndex == 1;
                int changed = await db.Shortages.Where(s => s.Id == editingItemId && !s.IsDeleted && s.Quantity == _editingQuantity)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Item, name).SetProperty(x => x.Quantity, count).SetProperty(x => x.Price, price)
                        .SetProperty(x => x.PriceKnown, known).SetProperty(x => x.Currency, currency).SetProperty(x => x.IsUrgent, urgent));
                if (changed != 1) throw new InvalidOperationException("تغيرت كمية المادة أو حُذفت؛ ألغِ التعديل وحدّث القائمة.");
                Reset(); Refresh(); Status.Text = "✓ تم حفظ المادة والأولوية."; Highlight(editingItemId); return;
            }
            else
            {
                var duplicate = (await db.Shortages.Where(s => !s.IsDeleted && s.Quantity > 0).ToListAsync()).FirstOrDefault(s => s.Item.Equals(ItemName.Text.Trim(), StringComparison.CurrentCultureIgnoreCase));
                if (duplicate != null)
                {
                    var dialog = new ClinicDialog(this, "هذه المادة موجودة", "زيادة الكمية");
                    dialog.Note($"{duplicate.Item} · الكمية الحالية {duplicate.Quantity}\nستُضاف {count} مع الاحتفاظ بسعر المادة وأولويتها الحاليين.");
                    bool merge = false, separate = false;
                    dialog.OnSubmit(() => { merge = true; dialog.Complete(); return Task.CompletedTask; });
                    var independent = new Button { Content = "إضافة مادة مستقلة", Style = (Style)FindResource("WorkspaceButton"), Margin = new(0, 8, 0, 8) };
                    independent.Click += (_, _) => { separate = true; dialog.Complete(); }; dialog.Fields.Children.Add(independent); dialog.ShowDialog();
                    if (!merge && !separate) return;
                    if (merge)
                    {
                        int originalQuantity = duplicate.Quantity, newQuantity = checked(originalQuantity + count);
                        int changed = await db.Shortages.Where(s => s.Id == duplicate.Id && !s.IsDeleted && s.Quantity == originalQuantity).ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, newQuantity));
                        if (changed != 1) throw new InvalidOperationException("تغيرت كمية المادة؛ حدّث القائمة وأعد المحاولة.");
                        Reset(); Refresh(); Status.Text = "تمت زيادة الكمية."; Highlight(duplicate.Id); return;
                    }
                }
                item = new(); db.Shortages.Add(item);
            }
            item.Item = ItemName.Text.Trim(); item.Quantity = count; item.Price = price; item.PriceKnown = known;
            item.Currency = Currency.SelectedItem?.ToString() ?? "SYP"; item.IsUrgent = Priority.SelectedIndex == 1;
            await db.SaveChangesAsync(); int id = item.Id; Reset(); Refresh(); Status.Text = "✓ تم حفظ المادة."; Highlight(id);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { _busy = false; SaveItem.IsEnabled = true; }
    }
    void Reset()
    { _editingId = null; ItemName.Clear(); UnitPrice.Clear(); Quantity.Text = "1"; UnknownPrice.IsChecked = false; SaveItem.Content = "إضافة مادة"; FormTitle.Text = "إضافة مادة"; CancelEdit.Visibility = Visibility.Collapsed; ItemName.Focus(); }
    void Edit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ShortageRowModel row) return;
        var item = row.ItemData; _editingId = item.Id; _editingQuantity = item.Quantity; ItemName.Text = item.Item; UnitPrice.Text = item.Price.ToString("0.##"); Quantity.Text = item.Quantity.ToString();
        Currency.SelectedItem = item.Currency; Priority.SelectedIndex = item.IsUrgent ? 1 : 0; UnknownPrice.IsChecked = !item.PriceKnown;
        SaveItem.Content = "حفظ التعديل"; FormTitle.Text = "تعديل المادة والأولوية"; CancelEdit.Visibility = Visibility.Visible; FormTitle.BringIntoView(); ItemName.Focus();
    }
    void CancelEdit_Click(object sender, RoutedEventArgs e) => Reset();
    async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || (sender as FrameworkElement)?.DataContext is not ShortageRowModel row) return;
        try
        {
            using var db = _createContext(); int changed = await db.Shortages.Where(s => s.Id == row.Id && s.Quantity > 0 && !s.IsDeleted).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
            if (changed == 1) { _undoId = row.Id; UndoButton.Visibility = Visibility.Visible; if (_editingId == row.Id) Reset(); Refresh(); Status.Text = $"حُذفت {row.Item}. يمكنك التراجع."; }
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (!_undoId.HasValue) return;
        try { using var db = _createContext(); await db.Shortages.Where(s => s.Id == _undoId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, false)); _undoId = null; UndoButton.Visibility = Visibility.Collapsed; Refresh(); Status.Text = "تم التراجع عن الحذف."; }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    void Purchase_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ShortageRowModel row) return;
        var item = row.ItemData; var dialog = new ClinicDialog(this, "تسجيل شراء · " + item.Item, "تأكيد الشراء والمصروف");
        var price = dialog.Input("سعر الوحدة الفعلي", item.PriceKnown ? item.Price.ToString("0.##") : "", true);
        var quantity = dialog.Input($"الكمية المشتراة · المطلوبة {item.Quantity}", item.Quantity.ToString(), true);
        var currency = dialog.Choice("عملة الشراء", new[] { "SYP", "USD" }, item.Currency);
        var date = dialog.Date("تاريخ الشراء", AppClock.Today); var rate = dialog.Input("سعر الصرف · SYP لكل USD", _rate.ToString("0.##"), true);
        var preview = new TextBlock { Style = (Style)FindResource("WorkspaceHint"), Margin = new(0, 6, 0, 6) }; dialog.Fields.Children.Add(preview);
        void Preview() { if (WorkspaceUi.Number(price.Text, out var p) && int.TryParse(WorkspaceUi.Normalize(quantity.Text), out int q) && WorkspaceUi.Number(rate.Text, out var r)) preview.Text = $"سيُسجّل كمصروف: {p * q * (currency.SelectedItem?.ToString() == "USD" ? r : 1):N2} SYP\nستبقى {Math.Max(0, item.Quantity - q)} وحدات في النواقص."; }
        price.TextChanged += (_, _) => Preview(); quantity.TextChanged += (_, _) => Preview(); rate.TextChanged += (_, _) => Preview(); currency.SelectionChanged += (_, _) => Preview(); Preview();
        string key = Guid.NewGuid().ToString();
        dialog.OnSubmit(async () =>
        {
            if (!WorkspaceUi.Number(price.Text, out var p) || !int.TryParse(WorkspaceUi.Normalize(quantity.Text), out int q) || !WorkspaceUi.Number(rate.Text, out var r) || date.SelectedDate == null) throw new InvalidOperationException("أكمل السعر والكمية والتاريخ وسعر الصرف.");
            using var db = _createContext(); await new ClinicTransactions(db).PurchaseAsync(item.Id, item.Quantity, q, p, currency.SelectedItem!.ToString()!, r, date.SelectedDate.Value, key);
            dialog.Complete();
            if (sender is Button purchased) { purchased.Content = "✓ تم الشراء"; purchased.IsEnabled = false; }
            await Task.Delay(MotionPreferences.Duration(180));
            Refresh(); Status.Text = "✓ تم تسجيل الشراء والمصروف. الإيصال متاح في سجل المشتريات."; GlobalEvents.NotifyFinancialRecordAdded();
            if (_editingId == item.Id) Reset();
        }); dialog.ShowDialog();
    }
    void Expense_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is ClinicOperation { ExpenseId: int id }) WorkspaceUi.OpenExpense(this, id); }
    void Pending_Click(object sender, RoutedEventArgs e) { _historyMode = false; Filter(); }
    void History_Click(object sender, RoutedEventArgs e) { _historyMode = true; Filter(); WorkspaceUi.Reveal(HistoryCard); }
    void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
    void Groups_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = Groups.ActualWidth < 850; Grid.SetColumn(NormalCard, narrow ? 0 : 2); Grid.SetRow(NormalCard, narrow ? 1 : 0);
        Grid.SetColumnSpan(UrgentCard, narrow ? 3 : 1); Grid.SetColumnSpan(NormalCard, narrow ? 3 : 1); NormalCard.Margin = new(0, narrow ? 14 : 0, 0, 0);
    }
    void Highlight(int id) => Dispatcher.BeginInvoke(new Action(() =>
    {
        foreach (var list in new[] { UrgentList, NormalList }) foreach (ShortageRowModel row in list.Items)
            if (row.Id == id && list.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem container) { list.ScrollIntoView(row); WorkspaceUi.Reveal(container); }
    }), System.Windows.Threading.DispatcherPriority.Loaded);
}

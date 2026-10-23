using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace MyClinic;

public partial class PatientImageViewer : Window
{
    private readonly ObservableCollection<ExportableVisitImage> _images;
    private int _loadVersion;
    private BitmapSource? _bitmap;
    private int _rotation;
    private double _zoom = 1;
    private bool _fit = true;
    private bool _exporting;
    private bool _closed;
    private ExportableVisitImage? Current => ImageStrip.SelectedItem as ExportableVisitImage;

    public PatientImageViewer(IEnumerable<VisitAttachmentItem> images, string? selectedPath, string patientName)
    {
        InitializeComponent();
        _images = new(images.Select(i => new ExportableVisitImage(i)));
        ViewerTitle.Text = $"صور الزيارة · {patientName}";
        ImageStrip.ItemsSource = _images;
        Loaded += (_, _) => ImageStrip.SelectedIndex = Math.Max(0, _images.ToList().FindIndex(i => i.FilePath == selectedPath));
        Closed += (_, _) => { _closed = true; ++_loadVersion; FullImage.Source = null; _bitmap = null; };
        Closing += (_, e) => { if (_exporting) { e.Cancel = true; ViewerStatus.Text = "يرجى انتظار اكتمال التصدير."; } };
    }

    private async Task LoadCurrentAsync()
    {
        int version = ++_loadVersion;
        _bitmap = null; FullImage.Source = null; _rotation = 0; _fit = true;
        ImageState.Visibility = Visibility.Visible;
        RetryImageButton.Visibility = Visibility.Collapsed;
        ImageStateText.Text = "جارٍ تحميل الصورة…";
        ImagePosition.Text = $"{ImageStrip.SelectedIndex + 1} / {_images.Count}";
        PreviousButton.IsEnabled = ImageStrip.SelectedIndex > 0;
        NextButton.IsEnabled = ImageStrip.SelectedIndex < _images.Count - 1;
        ExportCurrentButton.IsEnabled = ExternalButton.IsEnabled = Current is not null && !_exporting;
        if (Current is not { } current) { ImageStateText.Text = "لا توجد صور لهذه الزيارة."; return; }
        try
        {
            BitmapSource source = await Task.Run(() =>
            {
                // Decode a bounded preview; export always copies the untouched original.
                using var stream = File.OpenRead(current.FilePath);
                var metadata = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                int previewWidth = Math.Min(4096, metadata.Frames[0].PixelWidth);
                stream.Position = 0;
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = previewWidth; image.StreamSource = stream; image.EndInit(); image.Freeze();
                return image;
            });
            if (_closed || version != _loadVersion) return;
            _bitmap = source;
            ApplyImageTransform();
            ImageState.Visibility = Visibility.Collapsed;
            FullImage.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, MotionPreferences.Duration(120)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
        {
            if (_closed || version != _loadVersion) return;
            ImageStateText.Text = File.Exists(current.FilePath) ? "تعذر عرض هذه الصورة. يمكنك تصدير الملف الأصلي أو فتحه خارج التطبيق." : "ملف الصورة غير موجود. أعد توصيل موقع التخزين ثم حاول مرة أخرى.";
            RetryImageButton.Visibility = Visibility.Visible;
        }
    }

    private void ApplyImageTransform()
    {
        if (_bitmap is null) return;
        BitmapSource displayed = _bitmap;
        if (_rotation != 0)
        {
            var rotated = new TransformedBitmap(_bitmap, new System.Windows.Media.RotateTransform(_rotation));
            rotated.Freeze(); displayed = rotated;
        }
        FullImage.Source = displayed;
        double w = displayed.PixelWidth * 96.0 / displayed.DpiX, h = displayed.PixelHeight * 96.0 / displayed.DpiY;
        if (_fit) _zoom = Math.Min(Math.Max(80, ImageScroll.ViewportWidth - 16) / w, Math.Max(80, ImageScroll.ViewportHeight - 16) / h);
        FullImage.Width = w * _zoom; FullImage.Height = h * _zoom;
        ZoomLabel.Text = $"{_zoom:P0}";
    }
    private void Zoom(double factor) { _fit = false; _zoom = Math.Clamp(_zoom * factor, .05, 8); ApplyImageTransform(); }
    private void Previous_Click(object sender, RoutedEventArgs e) { if (ImageStrip.SelectedIndex > 0) ImageStrip.SelectedIndex--; }
    private void Next_Click(object sender, RoutedEventArgs e) { if (ImageStrip.SelectedIndex < _images.Count - 1) ImageStrip.SelectedIndex++; }
    private async void ImageStrip_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) await LoadCurrentAsync(); }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Zoom(1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Zoom(.8);
    private void Fit_Click(object sender, RoutedEventArgs e) { _fit = true; ApplyImageTransform(); }
    private void Rotate_Click(object sender, RoutedEventArgs e) { _rotation = (_rotation + 90) % 360; ApplyImageTransform(); }
    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) { if (_fit) ApplyImageTransform(); }
    private void Image_MouseWheel(object sender, MouseWheelEventArgs e) { if (Keyboard.Modifiers == ModifierKeys.Control) { Zoom(e.Delta > 0 ? 1.15 : 1 / 1.15); e.Handled = true; } }
    private async void Retry_Click(object sender, RoutedEventArgs e) => await LoadCurrentAsync();
    private void Viewer_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key is Key.Left or Key.Right && e.OriginalSource is not CheckBox) { if (e.Key == Key.Left) Next_Click(sender, e); else Previous_Click(sender, e); e.Handled = true; }
    }
    private void ExportSelection_Changed(object sender, RoutedEventArgs e) => UpdateExportSelection();
    private void UpdateExportSelection()
    {
        if (_images is null) return;
        int count = _images.Count(i => i.IsSelected);
        ExportSelectedButton.Content = $"تصدير المحدد ({count})";
        ExportSelectedButton.IsEnabled = count > 0 && !_exporting;
    }
    private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (var image in _images) image.IsSelected = true; UpdateExportSelection(); }
    private void ClearSelection_Click(object sender, RoutedEventArgs e) { foreach (var image in _images) image.IsSelected = false; UpdateExportSelection(); }
    private void External_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } image) return;
        try { Process.Start(new ProcessStartInfo(image.FilePath) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException) { ViewerStatus.Text = "تعذر فتح الملف خارج التطبيق."; }
    }
    private async void ExportCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_exporting || Current is not { } image) return;
        var dialog = new SaveFileDialog { Title = "تصدير الصورة الأصلية", FileName = image.FileName, Filter = "الملف الأصلي|*" + Path.GetExtension(image.FilePath), AddExtension = true, DefaultExt = Path.GetExtension(image.FilePath), OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        await ExportAsync(new[] { (image.FilePath, dialog.FileName) }, overwrite: true);
    }
    private async void ExportSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_exporting) return;
        var selected = _images.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;
        var dialog = new OpenFolderDialog { Title = $"اختر مجلد تصدير {selected.Count} صور", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        // Allocate names together so two originals with the same filename remain distinct.
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var copies = selected.Select(i => (i.FilePath, ImageExportFiles.UniqueDestination(dialog.FolderName, i.FileName, reserved))).ToArray();
        await ExportAsync(copies, overwrite: false);
    }
    private async Task ExportAsync(IEnumerable<(string Source, string Destination)> copies, bool overwrite)
    {
        _exporting = true; ExportCurrentButton.IsEnabled = ExportSelectedButton.IsEnabled = SelectAllButton.IsEnabled = false;
        ViewerStatus.Text = "جارٍ تصدير الصور…";
        try
        {
            var report = await Task.Run(() => ImageExportFiles.Copy(copies, overwrite));
            ViewerStatus.Text = $"تم تصدير {report.Copied} ملف." + (report.Failures.Count > 0 ? $" تعذر تصدير {report.Failures.Count}: {string.Join("، ", report.Failures)}" : "");
        }
        finally { _exporting = false; ExportCurrentButton.IsEnabled = Current is not null; SelectAllButton.IsEnabled = true; UpdateExportSelection(); }
    }
}

public sealed class ExportableVisitImage : INotifyPropertyChanged
{
    public string FilePath { get; }
    public string FileName { get; }
    public BitmapImage? Thumbnail { get; }
    private bool _selected;
    public bool IsSelected { get => _selected; set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ExportableVisitImage(VisitAttachmentItem item) { FilePath = item.FilePath; FileName = item.FileName; Thumbnail = item.Thumbnail; }
}

public static class ImageExportFiles
{
    public static string UniqueDestination(string folder, string fileName, ISet<string> reserved)
    {
        string name = Path.GetFileName(fileName), stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
        string destination = Path.Combine(folder, name);
        for (int suffix = 2; File.Exists(destination) || Directory.Exists(destination) || reserved.Contains(destination); suffix++)
            destination = Path.Combine(folder, $"{stem} ({suffix}){extension}");
        reserved.Add(destination); return destination;
    }
    public static (int Copied, List<string> Failures) Copy(IEnumerable<(string Source, string Destination)> files, bool overwrite)
    {
        int copied = 0; var failures = new List<string>();
        foreach (var (source, destination) in files)
        {
            try
            {
                if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) throw new IOException("Source and destination are the same.");
                File.Copy(source, destination, overwrite); copied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { failures.Add(Path.GetFileName(source)); }
        }
        return (copied, failures);
    }
}

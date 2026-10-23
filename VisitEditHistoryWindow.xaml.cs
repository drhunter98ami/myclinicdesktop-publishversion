using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MyClinic
{
    public partial class VisitEditHistoryWindow : Window
    {
        private readonly int _visitId;

        public VisitEditHistoryWindow(int visitId)
        {
            InitializeComponent();
            _visitId = visitId;
            ApplyThemeAndLoad(IsDarkThemeActive());
            GlobalEvents.OnThemeChanged += ApplyThemeAndLoad;
            Closed += (_, _) => GlobalEvents.OnThemeChanged -= ApplyThemeAndLoad;
        }

        private bool IsDarkThemeActive()
        {
            if (Owner is MainWindow ownerMain)
                return ownerMain.IsDarkTheme;
            if (Application.Current?.MainWindow is MainWindow appMain)
                return appMain.IsDarkTheme;
            return false;
        }

        private static SolidColorBrush CreateBrush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));

        private void ApplyThemeAndLoad(bool isDark)
        {
            if (isDark)
            {
                var bannerGradient = new LinearGradientBrush { StartPoint = new Point(1, 0), EndPoint = new Point(0, 1) };
                bannerGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#0D6C6B"), 0.0));
                bannerGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#0B8674"), 0.48));
                bannerGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#216382"), 1.0));

                HistoryHeaderBanner.Background = bannerGradient;
                HistoryHeaderBanner.BorderBrush = CreateBrush("#662DD4BF");
                TxtHistoryHeaderTitle.Foreground = CreateBrush("#FFFFFF");
                TxtHistoryHeaderSub.Foreground = CreateBrush("#E2F2F0");

                BtnCloseHistory.Background = CreateBrush("#47535E");
                BtnCloseHistory.BorderBrush = CreateBrush("#64748B");
                BtnCloseHistory.BorderThickness = new Thickness(1);
                BtnCloseHistory.Foreground = CreateBrush("#FFFFFF");
            }
            else
            {
                HistoryHeaderBanner.Background = CreateBrush("#E3EFED");
                HistoryHeaderBanner.BorderBrush = CreateBrush("#D5E6E3");
                TxtHistoryHeaderTitle.Foreground = CreateBrush("#13284A");
                TxtHistoryHeaderSub.Foreground = CreateBrush("#2C3E50");

                BtnCloseHistory.Background = CreateBrush("#C3DCD8");
                BtnCloseHistory.BorderBrush = CreateBrush("#8EB4AE");
                BtnCloseHistory.BorderThickness = new Thickness(1);
                BtnCloseHistory.Foreground = CreateBrush("#264642");
            }

            if (HistorySpotlightCanvas is not null)
                HistorySpotlightCanvas.Visibility = isDark ? Visibility.Visible : Visibility.Collapsed;

            LoadHistory(_visitId, isDark);
        }

        private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!MotionPreferences.Enabled)
            {
                Window_MouseLeave(sender, e);
                return;
            }

            if (!IsDarkThemeActive() || HistorySpotlightCanvas is null || HistorySpotlightTransform is null)
                return;

            if (HistorySpotlightCanvas.Visibility != Visibility.Visible)
                HistorySpotlightCanvas.Visibility = Visibility.Visible;

            Point pos = e.GetPosition(RootHistoryGrid);
            HistorySpotlightTransform.X = pos.X - (HistorySpotlightEllipse.Width / 2.0);
            HistorySpotlightTransform.Y = pos.Y - (HistorySpotlightEllipse.Height / 2.0);
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            if (HistorySpotlightTransform is not null)
            {
                HistorySpotlightTransform.X = -1200;
                HistorySpotlightTransform.Y = -1200;
            }
        }

        private void LoadHistory(int visitId, bool isDark)
        {
            HistoryItems.Children.Clear();

            using AppDbContext db = new();
            List<VisitEditHistory> entries = db.VisitEditHistories
                .AsNoTracking()
                .Where(item => item.VisitId == visitId)
                .OrderByDescending(item => item.EditedAt)
                .ToList();

            if (entries.Count == 0)
            {
                HistoryItems.Children.Add(new TextBlock
                {
                    Text = "لا يوجد سجل تعديل لهذه الزيارة حتى الآن.",
                    Foreground = (Brush)FindResource("AppTextSecondary"),
                    FontSize = 16,
                    Margin = new Thickness(0, 12, 0, 0)
                });
                return;
            }

            // ألوان بطاقات قبل/بعد التعديل مطابقة لألوان بطاقات المواعيد في الوضعين الفاتح والداكن
            string beforeBg = isDark ? "#BA6571" : "#FB7A6D";
            string beforeBorder = isDark ? "#D47A87" : "#F06457";
            string beforeFg = isDark ? "#FFF1F2" : "#2B0908";

            string afterBg = isDark ? "#0D514F" : "#B2DCCC";
            string afterBorder = isDark ? "#14857E" : "#96CEB8";
            string afterFg = isDark ? "#E6FFFA" : "#0F291E";

            foreach (VisitEditHistory entry in entries)
            {
                List<VisitEditChange> changes;
                try
                {
                    changes = JsonSerializer.Deserialize<List<VisitEditChange>>(
                        entry.ChangesJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                }
                catch (JsonException)
                {
                    changes = new();
                }

                StackPanel changeList = new();
                foreach (VisitEditChange change in changes)
                {
                    Grid values = new();
                    values.ColumnDefinitions.Add(new ColumnDefinition());
                    values.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                    values.ColumnDefinitions.Add(new ColumnDefinition());
                    values.Children.Add(CreateValueCard("قبل التعديل", change.Before, beforeBg, beforeBorder, beforeFg));
                    Border after = CreateValueCard("بعد التعديل", change.After, afterBg, afterBorder, afterFg);
                    Grid.SetColumn(after, 2);
                    values.Children.Add(after);

                    StackPanel field = new() { Margin = new Thickness(0, 0, 0, 12) };
                    field.Children.Add(new TextBlock
                    {
                        Text = change.Field,
                        FontWeight = FontWeights.Bold,
                        Foreground = (Brush)FindResource("AppTextPrimary"),
                        Margin = new Thickness(0, 0, 0, 6)
                    });
                    field.Children.Add(values);
                    changeList.Children.Add(field);
                }

                if (changes.Count == 0)
                {
                    changeList.Children.Add(new TextBlock
                    {
                        Text = "لا توجد تفاصيل محفوظة لهذا التعديل.",
                        Foreground = (Brush)FindResource("AppTextSecondary"),
                        Margin = new Thickness(0, 8, 0, 0)
                    });
                }

                StackPanel card = new() { Margin = new Thickness(0, 0, 0, 14) };
                Border shell = new()
                {
                    Padding = new Thickness(18),
                    CornerRadius = new CornerRadius(16),
                    Background = CreateBrush(isDark ? "#121621" : "#FFFFFF"),
                    BorderBrush = CreateBrush(isDark ? "#232E42" : "#E4ECEA"),
                    BorderThickness = new Thickness(1)
                };
                StackPanel content = new();
                content.Children.Add(new TextBlock
                {
                    Text = entry.EditedAt.ToString("dd/MM/yyyy  hh:mm:ss.fff tt", CultureInfo.InvariantCulture),
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = CreateBrush(isDark ? "#5EEAD4" : "#3348BD")
                });
                content.Children.Add(new TextBlock
                {
                    Text = $"تم التعديل بواسطة: {(!string.IsNullOrWhiteSpace(entry.EditedBy) ? entry.EditedBy : "غير معروف")}",
                    Foreground = (Brush)FindResource("AppTextSecondary"),
                    Margin = new Thickness(0, 3, 0, 12)
                });
                content.Children.Add(new Expander
                {
                    Header = $"عرض التغييرات ({changes.Count})",
                    IsExpanded = ReferenceEquals(entry, entries[0]),
                    Foreground = (Brush)FindResource("AppTextPrimary"),
                    Content = changeList
                });
                shell.Child = content;
                card.Children.Add(shell);
                HistoryItems.Children.Add(card);
            }
        }

        private static Border CreateValueCard(string label, string value, string background, string borderHex, string textHex)
        {
            Border card = new()
            {
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(10),
                Background = CreateBrush(background),
                BorderBrush = CreateBrush(borderHex),
                BorderThickness = new Thickness(1)
            };
            StackPanel content = new();
            content.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = CreateBrush(textHex),
                Opacity = 0.85
            });
            var valueText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(value) ? "غير محدد" : value,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 280,
                FontWeight = FontWeights.SemiBold,
                Foreground = CreateBrush(textHex),
                Margin = new Thickness(0, 4, 0, 0)
            };
            if (value?.Length > 240)
                content.Children.Add(new Expander { Header = "عرض النص الكامل", Foreground = CreateBrush(textHex), Content = valueText });
            else content.Children.Add(valueText);
            card.Child = content;
            return card;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}

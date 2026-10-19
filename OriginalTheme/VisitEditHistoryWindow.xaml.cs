using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MyClinic.OriginalTheme
{
    public partial class VisitEditHistoryWindow : Window
    {
        public VisitEditHistoryWindow(int visitId)
        {
            InitializeComponent();
            LoadHistory(visitId);
        }

        private void LoadHistory(int visitId)
        {
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
                    values.Children.Add(CreateValueCard("قبل التعديل", change.Before, "#FEF2F2", "#B91C1C"));
                    Border after = CreateValueCard("بعد التعديل", change.After, "#ECFDF5", "#047857");
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
                    Padding = new Thickness(16),
                    CornerRadius = new CornerRadius(12),
                    Background = (Brush)FindResource("AppCardBg"),
                    BorderBrush = (Brush)FindResource("AppBorder"),
                    BorderThickness = new Thickness(1)
                };
                StackPanel content = new();
                content.Children.Add(new TextBlock
                {
                    Text = entry.EditedAt.ToString("dd/MM/yyyy  hh:mm:ss.fff tt", CultureInfo.InvariantCulture),
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("AppTextPrimary")
                });
                content.Children.Add(new TextBlock
                {
                    Text = $"تم التعديل بواسطة: {(!string.IsNullOrWhiteSpace(entry.EditedBy) ? entry.EditedBy : "غير معروف")}",
                    Foreground = (Brush)FindResource("AppTextSecondary"),
                    Margin = new Thickness(0, 3, 0, 12)
                });
                content.Children.Add(changeList);
                shell.Child = content;
                card.Children.Add(shell);
                HistoryItems.Children.Add(card);
            }
        }

        private Border CreateValueCard(string label, string value, string background, string accent)
        {
            Border card = new()
            {
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background)),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent)),
                BorderThickness = new Thickness(1)
            };
            StackPanel content = new();
            content.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent)) });
            content.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(value) ? "غير محدد" : value,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 280,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111827")),
                Margin = new Thickness(0, 4, 0, 0)
            });
            card.Child = content;
            return card;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}

using System.IO;
using System.Windows;

namespace MyClinic;

public static class InterfaceThemeSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MyClinicApp",
        "InterfaceThemeConfig.txt");

    public static bool IsOriginalTheme
    {
        get
        {
            if (!File.Exists(SettingsPath))
                return false;

            return string.Equals(File.ReadAllText(SettingsPath).Trim(), "Original", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static Window CreateMainWindow() =>
        IsOriginalTheme ? new OriginalTheme.MainWindow() : new MainWindow();

    public static void SwitchTo(Window currentWindow, bool useOriginalTheme)
    {
        if (useOriginalTheme == IsOriginalTheme)
            return;

        Window nextWindow = useOriginalTheme ? new OriginalTheme.MainWindow() : new MainWindow();

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, useOriginalTheme ? "Original" : "Modern");
        Application.Current.MainWindow = nextWindow;
        nextWindow.Show();
        currentWindow.Close();
    }
}

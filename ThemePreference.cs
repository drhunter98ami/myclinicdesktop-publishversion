using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace MyClinic;

public static class ThemePreference
{
    static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyClinicApp", "FollowSystemTheme.txt");
    public static bool FollowSystem { get { try { return File.Exists(PathName) && File.ReadAllText(PathName) == "true"; } catch { return false; } } }
    public static bool SystemDark
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); return key?.GetValue("AppsUseLightTheme") is int light && light == 0; }
    }
    public static void Set(bool follow) { Directory.CreateDirectory(Path.GetDirectoryName(PathName)!); File.WriteAllText(PathName, follow ? "true" : "false"); }
    public static void Attach(Window window, Action<bool> apply)
    {
        if (FollowSystem) apply(SystemDark);
        UserPreferenceChangedEventHandler changed = (_, _) => { if (FollowSystem) window.Dispatcher.BeginInvoke(new Action(() => apply(SystemDark))); };
        SystemEvents.UserPreferenceChanged += changed; window.Closed += (_, _) => SystemEvents.UserPreferenceChanged -= changed;
    }
}

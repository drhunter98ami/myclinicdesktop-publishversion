using System.IO;
using System.Windows;

namespace MyClinic;

public enum MotionLevel { Off, Subtle, Full }

public static class MotionPreferences
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyClinicApp", "MotionPreference.txt");
    private static MotionLevel _current = Load();
    public static MotionLevel Current => _current;
    public static bool Enabled => _current != MotionLevel.Off && SystemParameters.ClientAreaAnimation;
    public static event EventHandler? Changed;
    public static TimeSpan Duration(double milliseconds) => TimeSpan.FromMilliseconds(
        Enabled ? milliseconds * (_current == MotionLevel.Subtle ? 0.75 : 1) : 0);
    public static double Distance(double pixels) => Enabled ? pixels * (_current == MotionLevel.Subtle ? 0.6 : 1) : 0;

    public static void Set(MotionLevel value, bool persist = true)
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (persist)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, value.ToString());
        }
        if (_current == value) return;
        _current = value;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static MotionLevel Load()
    {
        try
        {
            if (File.Exists(SettingsPath) && Enum.TryParse<MotionLevel>(File.ReadAllText(SettingsPath).Trim(), out var level)
                && Enum.IsDefined(level)) return level;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return MotionLevel.Subtle;
    }
}

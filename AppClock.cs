using System;
using System.Globalization;
using System.IO;

namespace MyClinic
{
    public static class AppClock
    {
        private static readonly object SyncRoot = new();
        private static readonly string OffsetFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyClinicApp",
            "AppTimeOffset.txt");
        private static TimeSpan? _savedOffset;

        public static DateTime Now => GetComputerTime().Add(GetOffset());

        public static DateTime Today => Now.Date;

        public static void SetCurrentTime(DateTime appTime)
        {
            lock (SyncRoot)
            {
                TimeSpan offset = appTime - GetComputerTime();
                Directory.CreateDirectory(Path.GetDirectoryName(OffsetFilePath)!);
                File.WriteAllText(OffsetFilePath, offset.Ticks.ToString(CultureInfo.InvariantCulture));
                _savedOffset = offset;
            }
        }

        public static void ResetToComputerTime()
        {
            lock (SyncRoot)
            {
                if (File.Exists(OffsetFilePath))
                    File.Delete(OffsetFilePath);

                _savedOffset = TimeSpan.Zero;
            }
        }

        private static DateTime GetComputerTime() => DateTime.Now.AddHours(7);

        private static TimeSpan GetOffset()
        {
            lock (SyncRoot)
            {
                if (_savedOffset is TimeSpan savedOffset)
                    return savedOffset;

                if (File.Exists(OffsetFilePath) &&
                    long.TryParse(File.ReadAllText(OffsetFilePath), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
                {
                    _savedOffset = TimeSpan.FromTicks(ticks);
                }
                else
                {
                    _savedOffset = TimeSpan.Zero;
                }

                return _savedOffset.Value;
            }
        }
    }
}

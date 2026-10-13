using System;
using System.Globalization;
using System.IO;

namespace MyClinic
{
    internal static class LoginSessionStore
    {
        private static readonly string SessionDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyClinicApp");

        private static readonly string SessionFilePath = Path.Combine(SessionDirectory, "session.txt");

        public static string CurrentUsername
        {
            get
            {
                try
                {
                    if (!File.Exists(SessionFilePath)) return string.Empty;
                    string[] lines = File.ReadAllLines(SessionFilePath);
                    return lines.Length > 1 ? lines[1].Trim() : string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        public static bool HasValidLoginSession(TimeSpan maxAge)
        {
            try
            {
                if (!File.Exists(SessionFilePath))
                {
                    return false;
                }

                string[] lines = File.ReadAllLines(SessionFilePath);
                if (lines.Length < 2 ||
                    string.IsNullOrWhiteSpace(lines[1]) ||
                    !DateTime.TryParse(
                        lines[0].Trim(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTime lastLoginUtc))
                {
                    return false;
                }

                TimeSpan sessionAge = DateTime.UtcNow - lastLoginUtc.ToUniversalTime();
                return sessionAge >= TimeSpan.Zero && sessionAge < maxAge;
            }
            catch
            {
                return false;
            }
        }

        public static void MarkSuccessfulLogin(string username)
        {
            Directory.CreateDirectory(SessionDirectory);
            File.WriteAllLines(SessionFilePath, new[]
            {
                DateTime.UtcNow.ToString("O"),
                username.Trim()
            });
        }

        public static void UpdateUsername(string username)
        {
            string lastLoginUtc = File.Exists(SessionFilePath)
                ? File.ReadLines(SessionFilePath).FirstOrDefault() ?? DateTime.UtcNow.ToString("O")
                : DateTime.UtcNow.ToString("O");

            Directory.CreateDirectory(SessionDirectory);
            File.WriteAllLines(SessionFilePath, new[] { lastLoginUtc, username.Trim() });
        }
    }
}

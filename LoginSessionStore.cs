using System;
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

                string savedText = File.ReadAllText(SessionFilePath).Trim();
                if (!DateTime.TryParse(savedText, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime lastLoginUtc))
                {
                    return false;
                }

                return DateTime.UtcNow - lastLoginUtc < maxAge;
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
    }
}

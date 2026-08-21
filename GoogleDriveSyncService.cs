using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MyClinic.Services
{
    public class GoogleDriveSyncService
    {
        static string[] Scopes = { DriveService.Scope.DriveFile };
        static string ApplicationName = "My Clinic Backup Sync";

        public static async Task<(bool Success, string ErrorMessage)> BackupDatabaseAsync()
        {
            try
            {
                // توجيه المسارات إلى المجلد الآمن
                string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string clinicFolder = Path.Combine(appDataFolder, "MyClinicApp");
                
                if (!Directory.Exists(clinicFolder))
                {
                    Directory.CreateDirectory(clinicFolder);
                }

                UserCredential credential;
                // توجيه مسار حفظ رمز المصادقة (Token) الخاص بجوجل إلى المسار الآمن
                string credPath = GetUserTokenPath(clinicFolder);

                // ملف credentials.json لا يزال يُقرأ من مجلد البرنامج الأساسي (read-only)
                string credentialsPath = Path.Combine(AppContext.BaseDirectory, "credentials.json");
                if (!File.Exists(credentialsPath))
                    return (false, "ملف credentials.json غير موجود. تأكد من إعدادات Copy to Output Directory.");

                using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
                {
                    credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.FromStream(stream).Secrets,
                        Scopes,
                        "user",
                        CancellationToken.None,
                        new FileDataStore(credPath, true));
                }

                var service = new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = ApplicationName,
                });

                // مسار ملف قاعدة البيانات الصحيح
                string dbPath = Path.Combine(clinicFolder, "ClinicData.db");
                string fileName = "ClinicData_Backup.db";

                if (!File.Exists(dbPath)) 
                    return (false, $"ملف قاعدة البيانات غير موجود في المسار: {dbPath}");

                var listRequest = service.Files.List();
                listRequest.Q = $"name = '{fileName}' and trashed = false";
                var files = await listRequest.ExecuteAsync();
                var existingFile = files.Files.FirstOrDefault();

                var fileMetadata = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = fileName
                };

                using (var stream = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (existingFile != null)
                    {
                        var updateRequest = service.Files.Update(fileMetadata, existingFile.Id, stream, "application/octet-stream");
                        await updateRequest.UploadAsync();
                    }
                    else
                    {
                        var insertRequest = service.Files.Create(fileMetadata, stream, "application/octet-stream");
                        await insertRequest.UploadAsync();
                    }
                }

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public static async Task<(bool Success, string ErrorMessage)> RestoreDatabaseAsync()
        {
            try
            {
                // توجيه المسارات إلى المجلد الآمن
                string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string clinicFolder = Path.Combine(appDataFolder, "MyClinicApp");
                
                if (!Directory.Exists(clinicFolder))
                {
                    Directory.CreateDirectory(clinicFolder);
                }

                UserCredential credential;
                // توجيه مسار التوكن الآمن
                string credPath = GetUserTokenPath(clinicFolder);

                string credentialsPath = Path.Combine(AppContext.BaseDirectory, "credentials.json");
                if (!File.Exists(credentialsPath))
                    return (false, "ملف credentials.json غير موجود.");

                using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
                {
                    credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.FromStream(stream).Secrets,
                        Scopes,
                        "user",
                        CancellationToken.None,
                        new FileDataStore(credPath, true));
                }

                var service = new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = ApplicationName,
                });

                string fileName = "ClinicData_Backup.db";

                // البحث عن ملف النسخة الاحتياطية في جوجل درايف
                var listRequest = service.Files.List();
                listRequest.Q = $"name = '{fileName}' and trashed = false";
                var files = await listRequest.ExecuteAsync();
                var existingFile = files.Files.FirstOrDefault();

                if (existingFile == null)
                {
                    return (false, "لم يتم العثور على أي نسخة احتياطية في Google Drive.");
                }

                // نحفظ الملف في المجلد الآمن كملف مؤقت
                string tempDbPath = Path.Combine(clinicFolder, "ClinicData_Temp.db");

                var request = service.Files.Get(existingFile.Id);
                using (var stream = new FileStream(tempDbPath, FileMode.Create, FileAccess.Write))
                {
                    await request.DownloadAsync(stream);
                }

                // Check if downloaded file has content
                long fileSize = new FileInfo(tempDbPath).Length;
                if (fileSize < 1024)
                {
                    File.Delete(tempDbPath);
                    return (false, $"الملف المسترد صغير جداً ({fileSize} bytes). قد يكون النسخة الاحتياطية فارغة أو تالفة.");
                }

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private static string GetUserTokenPath(string clinicFolder)
        {
            string username = LoginSessionStore.CurrentUsername;
            if (string.IsNullOrWhiteSpace(username))
                username = "unidentified-user";

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(username.Trim().ToUpperInvariant()));
            string userKey = Convert.ToHexString(hash).ToLowerInvariant();
            string tokenPath = Path.Combine(clinicFolder, "GoogleDriveToken", userKey);
            Directory.CreateDirectory(tokenPath);
            return tokenPath;
        }
    }
}

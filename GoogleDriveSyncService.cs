using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyClinic.Services;

public sealed record DriveBackup(string Id, string Name, string Modified, long Size, string Account);
public sealed class BackupState
{
    public string Account { get; set; } = "";
    public DateTimeOffset? LastSuccess { get; set; }
    public string DriveModified { get; set; } = "";
    public string LastError { get; set; } = "";
    public string RecoveryPath { get; set; } = "";
}

public static class GoogleDriveSyncService
{
    static readonly SemaphoreSlim Gate = new(1, 1);
    static string ClinicFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyClinicApp");
    static string UserKey => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((LoginSessionStore.CurrentUsername ?? "unidentified-user").Trim().ToUpperInvariant()))).ToLowerInvariant();
    static string StatePath => Path.Combine(ClinicFolder, "BackupStatus_" + UserKey + ".json");
    public static string ActiveDatabasePath { get { using var db = new AppDbContext(); return Path.GetFullPath(db.Database.GetDbConnection().DataSource); } }
    public static BackupState ReadState()
    { try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<BackupState>(File.ReadAllText(StatePath)) ?? new() : new(); } catch { return new(); } }
    static void SaveState(BackupState state) { Directory.CreateDirectory(ClinicFolder); File.WriteAllText(StatePath, JsonSerializer.Serialize(state)); }
    static async Task<DriveService> Connect(CancellationToken cancellationToken)
    {
        string credentials = Path.Combine(AppContext.BaseDirectory, "credentials.json");
        if (!File.Exists(credentials)) throw new InvalidOperationException("ملف إعداد اتصال Google Drive غير موجود بجانب التطبيق.");
        using var stream = File.OpenRead(credentials);
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(GoogleClientSecrets.FromStream(stream).Secrets,
            new[] { DriveService.Scope.DriveFile }, "user", cancellationToken,
            new FileDataStore(Path.Combine(ClinicFolder, "GoogleDriveToken", UserKey), true));
        return new DriveService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "My Clinic Backup Sync" });
    }
    static async Task<string> Account(DriveService service, CancellationToken cancellationToken)
    {
        var request = service.About.Get(); request.Fields = "user(emailAddress,displayName)";
        var about = await request.ExecuteAsync(cancellationToken); return about.User?.EmailAddress ?? about.User?.DisplayName ?? "حساب Google متصل";
    }
    static async Task<DriveBackup?> Latest(DriveService service, CancellationToken cancellationToken)
    {
        var request = service.Files.List(); request.Q = "name = 'ClinicData_Backup.db' and trashed = false";
        request.Fields = "files(id,name,modifiedTime,size)"; request.OrderBy = "modifiedTime desc"; request.PageSize = 10;
        var result = await request.ExecuteAsync(cancellationToken); var file = result.Files.FirstOrDefault();
        return file == null ? null : new(file.Id, file.Name, file.ModifiedTimeRaw ?? "", file.Size ?? 0, await Account(service, cancellationToken));
    }
    public static async Task<DriveBackup?> InspectBackupAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            using var service = await Connect(cancellationToken); var backup = await Latest(service, cancellationToken);
            var state = ReadState(); state.Account = backup?.Account ?? await Account(service, cancellationToken); state.DriveModified = backup?.Modified ?? ""; state.LastError = ""; SaveState(state); return backup;
        }
        finally { Gate.Release(); }
    }
    public static void CreateSnapshot(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("قاعدة البيانات غير موجودة.");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destinationPath, Pooling = false }.ToString());
        source.Open(); target.Open(); source.BackupDatabase(target);
    }
    public static void ValidateBackup(string path)
    {
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); source.Open();
        using var check = source.CreateCommand(); check.CommandText = "PRAGMA quick_check";
        if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("نسخة قاعدة البيانات غير سليمة.");
        check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Users','Patients','Visits')";
        if (Convert.ToInt32(check.ExecuteScalar()) != 3) throw new InvalidDataException("الملف ليس نسخة بيانات عيادة مدعومة.");
    }
    public static async Task<(bool Success, string ErrorMessage)> BackupDatabaseAsync()
    {
        await Gate.WaitAsync(); string snapshot = Path.Combine(ClinicFolder, "Upload_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await Task.Run(() => CreateSnapshot(ActiveDatabasePath, snapshot));
            using var service = await Connect(CancellationToken.None); var existing = await Latest(service, CancellationToken.None);
            var metadata = new Google.Apis.Drive.v3.Data.File { Name = "ClinicData_Backup.db" };
            using var stream = File.OpenRead(snapshot);
            Google.Apis.Upload.IUploadProgress result;
            if (existing != null) result = await service.Files.Update(metadata, existing.Id, stream, "application/octet-stream").UploadAsync();
            else result = await service.Files.Create(metadata, stream, "application/octet-stream").UploadAsync();
            if (result.Status != Google.Apis.Upload.UploadStatus.Completed) throw result.Exception ?? new IOException("لم يكتمل رفع النسخة.");
            var state = ReadState(); state.Account = existing?.Account ?? await Account(service, CancellationToken.None); state.LastSuccess = DateTimeOffset.Now; state.LastError = ""; SaveState(state);
            return (true, "");
        }
        catch (Exception ex) { var state = ReadState(); state.LastError = ex.Message; try { SaveState(state); } catch { } return (false, ex.Message); }
        finally { try { if (File.Exists(snapshot)) File.Delete(snapshot); } catch { } Gate.Release(); }
    }
    public static async Task<(bool Success, string ErrorMessage)> RestoreDatabaseAsync(DriveBackup? selected = null)
    {
        await Gate.WaitAsync(); string staging = Path.Combine(ClinicFolder, "Download_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var service = await Connect(CancellationToken.None); var backup = selected ?? await Latest(service, CancellationToken.None) ?? throw new InvalidOperationException("لا توجد نسخة على Google Drive.");
            // Re-check the selected version so confirmation cannot silently restore a changed remote file.
            var get = service.Files.Get(backup.Id); get.Fields = "id,modifiedTime,size"; var remote = await get.ExecuteAsync();
            if (remote.ModifiedTimeRaw != backup.Modified) throw new InvalidOperationException("تغيرت نسخة Drive؛ اعرضها وراجع تاريخها مجدداً.");
            Directory.CreateDirectory(ClinicFolder);
            using (var stream = File.Create(staging))
            { var result = await service.Files.Get(backup.Id).DownloadAsync(stream); if (result.Status != Google.Apis.Download.DownloadStatus.Completed) throw result.Exception ?? new IOException("لم يكتمل تنزيل النسخة."); }
            ValidateBackup(staging);
            string active = ActiveDatabasePath; string recovery = Path.Combine(Path.GetDirectoryName(active)!, "Recovery", $"ClinicData_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.db");
            await Task.Run(() => CreateSnapshot(active, recovery));
            File.Move(staging, active + ".restore-pending.db", true);
            var state = ReadState(); state.Account = backup.Account; state.DriveModified = backup.Modified; state.RecoveryPath = recovery; state.LastError = ""; SaveState(state);
            return (true, "");
        }
        catch (Exception ex) { return (false, ex.Message); }
        finally { try { if (File.Exists(staging)) File.Delete(staging); } catch { } Gate.Release(); }
    }
    public static void ApplyPendingRestore()
    {
        string active = ActiveDatabasePath, pending = active + ".restore-pending.db";
        if (!File.Exists(pending)) return;
        ValidateBackup(pending);
        if (File.Exists(active)) CreateSnapshot(active, Path.Combine(Path.GetDirectoryName(active)!, "Recovery", $"ClinicData_before_apply_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.db"));
        // SQLite's backup API also handles a WAL database without discarding committed pages.
        using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = pending, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = active, Pooling = false }.ToString()))
        { source.Open(); target.Open(); source.BackupDatabase(target); }
        File.Delete(pending);
    }
}

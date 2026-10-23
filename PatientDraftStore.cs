using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyClinic.Models;

namespace MyClinic;

public sealed class PatientFormDraft
{
    public int Version { get; set; } = 1;
    public DateTime SavedAt { get; set; }
    public int? PatientId { get; set; }
    public Dictionary<string, string> Text { get; set; } = new();
    public Dictionary<string, int> Choices { get; set; } = new();
    public Dictionary<string, bool> Checks { get; set; } = new();
    public DateTime? DateOfBirth { get; set; }
    public int? BirthYear { get; set; }
    public List<SelectedTreatment> Treatments { get; set; } = new();
    public List<PrescriptionItem> Prescription { get; set; } = new();
    public List<string> Attachments { get; set; } = new();
    public List<string> AdultTeeth { get; set; } = new();
    public List<string> ChildTeeth { get; set; } = new();
    public bool ChildChart { get; set; }
}

/// <summary>One recoverable form per signed-in account, encrypted with Windows user protection.</summary>
public static class PatientDraftStore
{
    public static string DraftPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MyClinicApp", "Drafts", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(LoginSessionStore.CurrentUsername.ToUpperInvariant())))[..24] + ".draft");
    public static void Save(PatientFormDraft draft) => SaveAt(DraftPath, draft);
    public static PatientFormDraft? Load() => LoadAt(DraftPath);
    public static void Delete() { if (File.Exists(DraftPath)) File.Delete(DraftPath); }
    public static void SaveAt(string path, PatientFormDraft draft)
    {
        byte[] protectedBytes = Protect(JsonSerializer.SerializeToUtf8Bytes(draft), false);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string pending = path + ".tmp";
        File.WriteAllBytes(pending, protectedBytes);
        File.Move(pending, path, true);
    }
    public static PatientFormDraft? LoadAt(string path)
    {
        if (!File.Exists(path)) return null;
        var draft = JsonSerializer.Deserialize<PatientFormDraft>(Protect(File.ReadAllBytes(path), true));
        if (draft?.Version != 1) throw new InvalidDataException("Unsupported patient draft version.");
        return draft;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Protect(byte[] bytes, bool decrypt)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool success = decrypt ? CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, output.Length); return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
    }
}

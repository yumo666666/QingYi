using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using QingYi.Models;

namespace QingYi.Services;

public static class SettingsStore
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QingYi");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "QingYiSelectionTranslator";

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
        ApplyStartup(settings.StartWithWindows);
    }

    public static string ReadApiKey(AppSettings settings)
    {
        if (string.IsNullOrEmpty(settings.ProtectedApiKey)) return string.Empty;
        try
        {
            var protectedBytes = Convert.FromBase64String(settings.ProtectedApiKey);
            return System.Text.Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
        }
        catch { return string.Empty; }
    }

    public static void SetApiKey(AppSettings settings, string value)
    {
        settings.ProtectedApiKey = value.Length == 0 ? string.Empty : Convert.ToBase64String(ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    public static void ApplyStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (key is null) return;
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(exe)) key.SetValue(RunValue, $"\"{exe}\" --background");
        }
        else key.DeleteValue(RunValue, false);
    }
}

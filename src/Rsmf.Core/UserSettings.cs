using System.Text.Json;
using NodaTime;

namespace Rsmf.Core;

/// <summary>
/// Settings the user can choose to keep between runs. Stored as a small JSON file. Never holds file
/// content, and never the "include sensitive values" option: that always starts switched off.
/// </summary>
public sealed class UserSettings
{
    public const string FileName = "RsmfInspector.settings.json";

    /// <summary>
    /// Where the settings file is kept: the user's local application data folder. The program's own folder
    /// is not used because it is read-only when the app is installed from the Microsoft Store.
    /// </summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RsmfInspector", FileName);

    public string? TimeZoneId { get; set; }
    public string TimeWindowMode { get; set; } = "both";
    public long SizeThresholdBytes { get; set; } = RuleCatalog.DefaultSizeThresholdBytes;
    public int? ParticipantWarningThreshold { get; set; }
    public string? OutputFolder { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Loads settings, or returns null when the file is absent, unreadable or not valid. Bad values fall back to defaults.</summary>
    public static UserSettings? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var info = new FileInfo(path);
            if (info.Length > 64 * 1024) return null;
            var loaded = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path), Json);
            if (loaded is null) return null;

            if (string.IsNullOrWhiteSpace(loaded.TimeZoneId) || DateTimeZoneProviders.Tzdb.GetZoneOrNull(loaded.TimeZoneId) is null)
                loaded.TimeZoneId = null;
            if (loaded.TimeWindowMode is not ("both" or "elapsed-only")) loaded.TimeWindowMode = "both";
            if (loaded.SizeThresholdBytes <= 0) loaded.SizeThresholdBytes = RuleCatalog.DefaultSizeThresholdBytes;
            if (loaded.ParticipantWarningThreshold < 0) loaded.ParticipantWarningThreshold = null;
            if (string.IsNullOrWhiteSpace(loaded.OutputFolder)) loaded.OutputFolder = null;
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>Saves the settings. Returns false (and changes nothing else) when the location is not writable.</summary>
    public bool Save(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>Removes a saved settings file. Returns false when it could not be removed.</summary>
    public static bool Forget(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}

public enum DroppedKind { Nothing, Folder, File }

/// <summary>What to scan when items are dropped onto the window.</summary>
public sealed record DroppedInput(DroppedKind Kind, string Path, string Note)
{
    /// <summary>
    /// A folder is scanned; a single file is checked and opened. When several items are dropped, a folder
    /// wins over files and only the first is used, with a note saying so.
    /// </summary>
    public static DroppedInput From(IEnumerable<string>? paths)
    {
        var list = (paths ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        var folders = list.Where(Directory.Exists).ToList();
        var files = list.Where(File.Exists).ToList();
        if (folders.Count == 0 && files.Count == 0)
            return new DroppedInput(DroppedKind.Nothing, "", "Nothing that was dropped is a file or folder that can be read.");

        var ignored = list.Count - 1;
        var note = ignored > 0 ? $"{list.Count} items were dropped; only the first {(folders.Count > 0 ? "folder" : "file")} is used. To check several files, put them in one folder and drop the folder." : "";
        return folders.Count > 0
            ? new DroppedInput(DroppedKind.Folder, folders[0], note)
            : new DroppedInput(DroppedKind.File, files[0], note);
    }
}

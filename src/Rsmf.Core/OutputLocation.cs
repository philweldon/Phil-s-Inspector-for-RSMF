namespace Rsmf.Core;

/// <summary>
/// Where reports go when the user has not chosen a folder: beside the scanned folder (in its parent),
/// never inside it, so the source folder is left exactly as it was.
/// </summary>
public static class OutputLocation
{
    /// <summary>
    /// The folder that holds the scanned folder. For a single file, the folder that holds the file's folder.
    /// Null when there is no such folder (the input is a drive root, or does not exist).
    /// </summary>
    public static string? Beside(string input)
    {
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
            var folder = Directory.Exists(full) ? full : File.Exists(full) ? Path.GetDirectoryName(full) : null;
            if (string.IsNullOrEmpty(folder)) return null;
            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder));
            return string.IsNullOrEmpty(parent) ? null : parent;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>True when a file can be created in the folder. The probe file removes itself; nothing is left behind.</summary>
    public static bool CanWrite(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return false;
            var probe = Path.Combine(directory, ".rsmf-inspector-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// The start of the report folder's name when it sits beside the source: the scanned folder's name (or the
    /// file's name without ".rsmf") followed by <paramref name="kind"/>, for example "Chats-rsmf-report-".
    /// </summary>
    public static string Prefix(string input, string kind)
    {
        string name;
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
            name = Directory.Exists(full) ? Path.GetFileName(full) : Path.GetFileNameWithoutExtension(full);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            name = "";
        }
        name = name.Trim().TrimEnd('.');
        if (name.Length > 60) name = name[..60].TrimEnd(' ', '.');
        return name.Length == 0 ? kind + "-" : name + "-" + kind + "-";
    }
}

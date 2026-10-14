using System.Text;

namespace OpenParallel.Core.Linux;

/// <summary>The freedesktop.org Trash specification (home trash): the item moves to
/// <c>Trash/files</c> and a <c>.trashinfo</c> records where it came from, so the file manager's
/// Trash can restore it.</summary>
public static class LinuxTrash
{
    public static string HomeTrash(string dataHome) => Path.Combine(dataHome, "Trash");

    /// <summary>Moves one file or folder to the trash and returns its new path. Refuses items on
    /// another file system rather than copying them.</summary>
    public static string Move(string path, string trash, DateTime? now = null)
    {
        var full = Path.GetFullPath(path).TrimEnd('/');
        if (!File.Exists(full) && !Directory.Exists(full)) throw new FileNotFoundException("Nothing to move to the Trash.", full);
        var files = Path.Combine(trash, "files");
        var info = Path.Combine(trash, "info");
        Directory.CreateDirectory(files);
        Directory.CreateDirectory(info);

        var baseName = Path.GetFileName(full);
        for (var n = 1; ; n++)
        {
            var name = n == 1 ? baseName : $"{baseName}.{n}";
            var infoPath = Path.Combine(info, name + ".trashinfo");
            var target = Path.Combine(files, name);
            if (File.Exists(target) || Directory.Exists(target)) continue;
            FileStream stream;
            try
            {
                // CreateNew claims the name atomically, as the spec asks.
                stream = new FileStream(infoPath, FileMode.CreateNew, FileAccess.Write);
            }
            catch (IOException) when (File.Exists(infoPath))
            {
                continue;
            }
            using (stream)
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write($"[Trash Info]\nPath={EncodePath(full)}\nDeletionDate={(now ?? DateTime.Now):yyyy-MM-ddTHH:mm:ss}\n");
            }
            try
            {
                if (Directory.Exists(full)) Directory.Move(full, target);
                else File.Move(full, target);
            }
            catch
            {
                File.Delete(infoPath);
                throw;
            }
            return target;
        }
    }

    /// <summary>Percent-encodes each path segment, keeping the slashes (RFC 2396, as the spec says).</summary>
    public static string EncodePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}

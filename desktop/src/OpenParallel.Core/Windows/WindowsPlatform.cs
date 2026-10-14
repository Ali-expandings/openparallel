using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;

namespace OpenParallel.Core.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsPlatform : IPlatform
{
    public string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenParallel");

    public string Name => "windows";
    public IAppKnowledge Apps { get; } = new WindowsAppKnowledge();
    public string StartMenuPrograms { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
    public string ShortcutsFolder => Path.Combine(StartMenuPrograms, "OpenParallel");
    public string DesktopDirectory { get; } = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public string LauncherPath { get; } = Path.Combine(AppContext.BaseDirectory, "OpenParallel.exe");
    public ProcessMatching Matching => ProcessMatching.ExecutableFolder;
    public string ShortcutFileName(string instanceName, string slug) => Naming.SafeFileName(instanceName) + ".lnk";
    public string ShortcutPattern => "*.lnk";
    public string JoinArguments(IEnumerable<string> arguments) => WindowsCommandLine.Join(arguments);
    public IReadOnlyList<string> SplitArguments(string text) => WindowsCommandLine.Split(text);

    /// <summary>Opens File Explorer with the item selected (or the folder itself).</summary>
    public void Reveal(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
            else if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = false });
        }
        catch (Exception)
        {
        }
    }

    public void OpenWithDefaultApp(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }

    // MARK: Shortcuts

    public void WriteShortcut(ShortcutDefinition shortcut)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(shortcut.Target);
            link.SetArguments(shortcut.Arguments);
            if (shortcut.WorkingDirectory is not null) link.SetWorkingDirectory(shortcut.WorkingDirectory);
            if (shortcut.Description is not null) link.SetDescription(shortcut.Description[..Math.Min(shortcut.Description.Length, 250)]);
            if (shortcut.IconPath is not null) link.SetIconLocation(shortcut.IconPath, shortcut.IconIndex);
            if (shortcut.AppUserModelId is not null) TrySetAppUserModelId(link, shortcut.AppUserModelId);
            Directory.CreateDirectory(Path.GetDirectoryName(shortcut.Path)!);
            ((IPersistFile)link).Save(shortcut.Path, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    public ShortcutDefinition? ReadShortcut(string path)
    {
        if (!File.Exists(path)) return null;
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(path, 0);
            var buffer = new StringBuilder(1024);
            link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
            var target = buffer.ToString();
            buffer.Clear();
            link.GetArguments(buffer, buffer.Capacity);
            var arguments = buffer.ToString();
            buffer.Clear();
            link.GetWorkingDirectory(buffer, buffer.Capacity);
            var workDir = buffer.ToString();
            buffer.Clear();
            link.GetIconLocation(buffer, buffer.Capacity, out var iconIndex);
            var icon = buffer.ToString();
            buffer.Clear();
            link.GetDescription(buffer, buffer.Capacity);
            return new ShortcutDefinition
            {
                Path = path, Target = target, Arguments = arguments,
                WorkingDirectory = workDir.Length == 0 ? null : workDir,
                IconPath = icon.Length == 0 ? null : icon, IconIndex = iconIndex, Description = buffer.ToString(),
            };
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    private static void TrySetAppUserModelId(IShellLinkW link, string id)
    {
        // Lets Windows group the copy on its own in the taskbar where the app allows it.
        // Optional: an old shell without the property store still gets a working shortcut.
        try
        {
            var store = (IPropertyStore)link;
            var key = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
            var value = PropVariant.FromString(id);
            try
            {
                store.SetValue(ref key, ref value);
                store.Commit();
            }
            finally
            {
                value.Free();
            }
        }
        catch (Exception error) when (error is InvalidCastException or COMException)
        {
        }
    }

    // MARK: Recycle Bin

    public void MoveToRecycleBin(IReadOnlyList<string> paths)
    {
        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).Select(Path.GetFullPath).ToList();
        if (existing.Count == 0) return;
        var operation = new SHFILEOPSTRUCTW
        {
            wFunc = FO_DELETE,
            pFrom = string.Join('\0', existing) + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };
        var result = SHFileOperationW(ref operation);
        if (result != 0 || operation.fAnyOperationsAborted)
            throw new IOException($"Windows could not move {existing.Count} item(s) to the Recycle Bin (error {result}).");
    }

    // MARK: Processes

    public int Start(LaunchPlan plan)
    {
        if (plan.Target.StartsWith(PackagedApps.ActivationPrefix, StringComparison.OrdinalIgnoreCase))
            return PackagedApps.Activate(plan.Target[PackagedApps.ActivationPrefix.Length..], WindowsCommandLine.Join(plan.Arguments));
        var info = new ProcessStartInfo(plan.Target) { UseShellExecute = false };
        foreach (var argument in plan.Arguments) info.ArgumentList.Add(argument);
        if (plan.WorkingDirectory is not null && Directory.Exists(plan.WorkingDirectory)) info.WorkingDirectory = plan.WorkingDirectory;
        foreach (var (key, value) in plan.Environment) info.Environment[key] = value;
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Windows did not start {plan.Target}.");
        return process.Id;
    }

    public bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public IReadOnlyList<ProcessSnapshot> Processes()
    {
        var result = new List<ProcessSnapshot>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id <= 4) continue;
                var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, process.Id);
                if (handle == IntPtr.Zero) handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, process.Id);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    var image = ImagePath(handle);
                    if (image is null) continue;
                    var commandLine = CommandLineOf(handle) ?? "";
                    result.Add(new ProcessSnapshot(process.Id, image, commandLine));
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
        }
        return result;
    }

    private static string? ImagePath(IntPtr handle)
    {
        var buffer = new StringBuilder(1024);
        var size = buffer.Capacity;
        return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString() : null;
    }

    private static string? CommandLineOf(IntPtr handle)
    {
        // Windows 8.1+ (and Wine) answer ProcessCommandLineInformation directly.
        var length = 0;
        NtQueryInformationProcess(handle, ProcessCommandLineInformation, IntPtr.Zero, 0, ref length);
        if (length > 0 && length < 1 << 20)
        {
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, length, ref length) == 0)
                {
                    var stringLength = Marshal.ReadInt16(buffer);
                    var pointer = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                    if (pointer != IntPtr.Zero && stringLength > 0) return Marshal.PtrToStringUni(pointer, stringLength / 2);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return ReadProcessParameters(handle, environment: false);
    }

    public string? EnvironmentOf(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            return ReadProcessParameters(handle, environment: true);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Reads the command line or the environment block from a 64-bit process's
    /// RTL_USER_PROCESS_PARAMETERS. Returns null when the layout is unavailable.</summary>
    private static string? ReadProcessParameters(IntPtr handle, bool environment)
    {
        if (IntPtr.Size != 8) return null;
        var basic = new PROCESS_BASIC_INFORMATION();
        var returned = 0;
        if (NtQueryInformationProcess(handle, ProcessBasicInformation, ref basic, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), ref returned) != 0
            || basic.PebBaseAddress == IntPtr.Zero)
            return null;
        var parameters = ReadPointer(handle, basic.PebBaseAddress + 0x20);
        if (parameters == IntPtr.Zero) return null;

        if (!environment)
        {
            var header = ReadBytes(handle, parameters + 0x70, 16);
            if (header is null) return null;
            var length = BitConverter.ToUInt16(header, 0);
            var buffer = (IntPtr)BitConverter.ToInt64(header, 8);
            var text = ReadBytes(handle, buffer, length);
            return text is null ? null : Encoding.Unicode.GetString(text);
        }

        var block = ReadPointer(handle, parameters + 0x80);
        if (block == IntPtr.Zero) return null;
        var sizeBytes = ReadBytes(handle, parameters + 0x3F0, 8);
        var size = sizeBytes is null ? 0L : BitConverter.ToInt64(sizeBytes, 0);
        if (size <= 0 || size > 1 << 20) size = 64 * 1024;
        var bytes = ReadBytes(handle, block, (int)size) ?? ReadBytes(handle, block, 16 * 1024);
        if (bytes is null) return null;
        var all = Encoding.Unicode.GetString(bytes);
        var end = all.IndexOf("\0\0", StringComparison.Ordinal);
        return (end >= 0 ? all[..end] : all).Replace('\0', '\n');
    }

    private static IntPtr ReadPointer(IntPtr handle, IntPtr address)
    {
        var bytes = ReadBytes(handle, address, 8);
        return bytes is null ? IntPtr.Zero : (IntPtr)BitConverter.ToInt64(bytes, 0);
    }

    private static byte[]? ReadBytes(IntPtr handle, IntPtr address, int count)
    {
        if (count <= 0) return [];
        var buffer = new byte[count];
        return ReadProcessMemory(handle, address, buffer, count, out var read) && read > 0
            ? (read == count ? buffer : buffer[..(int)read])
            : null;
    }

    // MARK: Windows

    public bool BringToFront(int pid)
    {
        var window = MainWindowOf(pid);
        if (window == IntPtr.Zero) return false;
        if (IsIconic(window)) ShowWindow(window, SW_RESTORE);
        return SetForegroundWindow(window);
    }

    public bool RequestClose(int pid)
    {
        var windows = TopLevelWindowsOf(pid);
        foreach (var window in windows) PostMessageW(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        return windows.Count > 0;
    }

    public void ForceQuit(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
        }
    }

    private static IntPtr MainWindowOf(int pid) => TopLevelWindowsOf(pid).FirstOrDefault();

    private static List<IntPtr> TopLevelWindowsOf(int pid)
    {
        var windows = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner == pid && IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == IntPtr.Zero) windows.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    // MARK: Native

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400;
    private const int PROCESS_QUERY_INFORMATION = 0x400, PROCESS_VM_READ = 0x10, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int ProcessBasicInformation = 0, ProcessCommandLineInformation = 60;
    private const int SW_RESTORE = 9;
    private const uint GW_OWNER = 4, WM_CLOSE = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW operation);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out nint read);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, IntPtr info, int length, ref int returned);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref PROCESS_BASIC_INFORMATION info, int length, ref int returned);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int max, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relative, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly struct PropertyKey(Guid formatId, uint propertyId)
    {
        public readonly Guid FormatId = formatId;
        public readonly uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] private ushort valueType;
        [FieldOffset(8)] private IntPtr pointer;

        public static PropVariant FromString(string value) =>
            new() { valueType = 31 /* VT_LPWSTR */, pointer = Marshal.StringToCoTaskMemUni(value) };

        public void Free()
        {
            if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
            pointer = IntPtr.Zero;
        }
    }
}

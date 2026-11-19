using System.Runtime.InteropServices;

namespace FRExplorer;

internal sealed record ShellEntry(ShellItemInfo Item, Bitmap? SmallIcon) : IDisposable
{
    public void Dispose() => SmallIcon?.Dispose();
}

internal static class WindowsShellNamespace
{
    internal static Task<List<ShellEntry>> EnumerateAsync(
        string parsingName,
        bool foldersOnly,
        CancellationToken cancellationToken) =>
        StaTask.Run(() => Enumerate(parsingName, foldersOnly), cancellationToken);

    private static List<ShellEntry> Enumerate(string parsingName, bool foldersOnly)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        object? shell = null;
        object? folder = null;
        object? items = null;
        var result = new List<ShellEntry>();
        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            shell = shellType is null ? null : Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return [];
            }

            dynamic shellDispatch = shell;
            folder = parsingName == ShellItemInfo.DesktopParsingName
                ? shellDispatch.NameSpace(0)
                : shellDispatch.NameSpace(parsingName);
            if (folder is null)
            {
                return [];
            }

            dynamic folderDispatch = folder;
            items = folderDispatch.Items();
            if (items is null)
            {
                return [];
            }

            dynamic itemsDispatch = items;
            int count = Convert.ToInt32(itemsDispatch.Count, System.Globalization.CultureInfo.InvariantCulture);
            for (int index = 0; index < count; index++)
            {
                object? item = null;
                try
                {
                    item = itemsDispatch.Item(index);
                    if (item is null)
                    {
                        continue;
                    }

                    dynamic itemDispatch = item;
                    bool isFolder = Convert.ToBoolean(itemDispatch.IsFolder, System.Globalization.CultureInfo.InvariantCulture);
                    if (foldersOnly && !isFolder)
                    {
                        continue;
                    }

                    string displayName = Convert.ToString(itemDispatch.Name, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
                    string itemPath = Convert.ToString(itemDispatch.Path, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
                    if (displayName.Length == 0 || itemPath.Length == 0)
                    {
                        continue;
                    }

                    var shellItem = new ShellItemInfo(displayName, itemPath, isFolder);
                    result.Add(new ShellEntry(shellItem, WindowsShellIconProvider.GetSmallIcon(itemPath)));
                }
                finally
                {
                    ReleaseComObject(item);
                }
            }
        }
        catch (Exception exception) when (
            exception is COMException or InvalidCastException or MissingMemberException or NotSupportedException)
        {
            DisposeEntries(result);
            return [];
        }
        finally
        {
            ReleaseComObject(items);
            ReleaseComObject(folder);
            ReleaseComObject(shell);
        }

        // The Shell owns ordering for virtual folders and drives.
        return result;
    }

    internal static void DisposeEntries(IEnumerable<ShellEntry> entries)
    {
        foreach (ShellEntry entry in entries)
        {
            entry.Dispose();
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}

internal static class WindowsShellIconProvider
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiPidl = 0x000000008;

    internal static Bitmap? GetSmallIcon(string parsingName)
    {
        if (!OperatingSystem.IsWindows()
            || SHParseDisplayName(parsingName, IntPtr.Zero, out IntPtr pidl, 0, out _) != 0
            || pidl == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var info = new ShellFileInfo { DisplayName = string.Empty, TypeName = string.Empty };
            IntPtr result = SHGetFileInfo(
                pidl,
                0,
                ref info,
                (uint)Marshal.SizeOf<ShellFileInfo>(),
                ShgfiIcon | ShgfiSmallIcon | ShgfiPidl);
            if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                using Icon icon = Icon.FromHandle(info.IconHandle);
                return icon.ToBitmap();
            }
            finally
            {
                DestroyIcon(info.IconHandle);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        internal IntPtr IconHandle;
        internal int IconIndex;
        internal uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        internal string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(
        string name,
        IntPtr bindingContext,
        out IntPtr itemIdList,
        uint attributesIn,
        out uint attributesOut);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        IntPtr pathOrItemIdList,
        uint fileAttributes,
        ref ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}


using System.Runtime.InteropServices;

namespace FRExplorer;

/// <summary>Displays the Windows Shell menu for one physical or virtual item.</summary>
internal static class WindowsShellContextMenu
{
    private const uint CmfNormal = 0x00000000;
    private const uint CmicMaskUnicode = 0x00004000;
    private const uint CmicMaskPtInvoke = 0x20000000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const int SwShowNormal = 1;
    private const uint FirstCommandId = 1;
    private const uint LastCommandId = 0x7fff;

    internal static bool Show(Control owner, string parsingName, Point screenLocation)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);
        if (!OperatingSystem.IsWindows() || owner.IsDisposed || !owner.IsHandleCreated)
        {
            return false;
        }

        IntPtr absolutePidl = IntPtr.Zero;
        IShellFolder? parent = null;
        object? contextMenuObject = null;
        IntPtr menu = IntPtr.Zero;
        try
        {
            if (SHParseDisplayName(parsingName, IntPtr.Zero, out absolutePidl, 0, out _) != 0
                || absolutePidl == IntPtr.Zero)
            {
                return false;
            }

            Guid shellFolderId = typeof(IShellFolder).GUID;
            int result = SHBindToParent(absolutePidl, ref shellFolderId, out parent, out IntPtr childPidl);
            if (result != 0 || parent is null || childPidl == IntPtr.Zero)
            {
                return false;
            }

            Guid contextMenuId = typeof(IContextMenu).GUID;
            IntPtr[] children = [childPidl];
            result = parent.GetUIObjectOf(owner.Handle, 1, children, ref contextMenuId, IntPtr.Zero, out IntPtr rawMenu);
            if (result != 0 || rawMenu == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                contextMenuObject = Marshal.GetObjectForIUnknown(rawMenu);
            }
            finally
            {
                Marshal.Release(rawMenu);
            }

            var contextMenu = (IContextMenu)contextMenuObject;
            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                return false;
            }

            result = contextMenu.QueryContextMenu(menu, 0, FirstCommandId, LastCommandId, CmfNormal);
            if (result < 0)
            {
                Marshal.ThrowExceptionForHR(result);
            }

            using var messageWindow = new ContextMenuMessageWindow(owner, contextMenuObject);
            uint selected = TrackPopupMenuEx(
                menu,
                TpmRightButton | TpmReturnCommand,
                screenLocation.X,
                screenLocation.Y,
                owner.Handle,
                IntPtr.Zero);
            if (selected == 0)
            {
                return false;
            }

            var invocation = new CommandInvocation
            {
                Size = Marshal.SizeOf<CommandInvocation>(),
                Mask = CmicMaskUnicode | CmicMaskPtInvoke,
                Owner = owner.Handle,
                Verb = (IntPtr)(selected - FirstCommandId),
                VerbUnicode = (IntPtr)(selected - FirstCommandId),
                Show = SwShowNormal,
                InvokePoint = new NativePoint(screenLocation.X, screenLocation.Y),
            };
            result = contextMenu.InvokeCommand(ref invocation);
            if (result < 0)
            {
                Marshal.ThrowExceptionForHR(result);
            }

            return true;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or ExternalException)
        {
            return false;
        }
        finally
        {
            if (menu != IntPtr.Zero)
            {
                DestroyMenu(menu);
            }

            if (contextMenuObject is not null && Marshal.IsComObject(contextMenuObject))
            {
                Marshal.FinalReleaseComObject(contextMenuObject);
            }

            if (parent is not null && Marshal.IsComObject(parent))
            {
                Marshal.FinalReleaseComObject(parent);
            }

            if (absolutePidl != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(absolutePidl);
            }
        }
    }

    /// <summary>
    /// Builds and queries the native menu without displaying or invoking it.
    /// This keeps automated validation non-destructive.
    /// </summary>
    internal static bool CanBuild(string parsingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        IntPtr absolutePidl = IntPtr.Zero;
        IShellFolder? parent = null;
        object? contextMenuObject = null;
        IntPtr menu = IntPtr.Zero;
        try
        {
            if (SHParseDisplayName(parsingName, IntPtr.Zero, out absolutePidl, 0, out _) != 0
                || absolutePidl == IntPtr.Zero)
            {
                return false;
            }

            Guid shellFolderId = typeof(IShellFolder).GUID;
            if (SHBindToParent(absolutePidl, ref shellFolderId, out parent, out IntPtr childPidl) != 0
                || parent is null
                || childPidl == IntPtr.Zero)
            {
                return false;
            }

            Guid contextMenuId = typeof(IContextMenu).GUID;
            IntPtr[] children = [childPidl];
            if (parent.GetUIObjectOf(IntPtr.Zero, 1, children, ref contextMenuId, IntPtr.Zero, out IntPtr rawMenu) != 0
                || rawMenu == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                contextMenuObject = Marshal.GetObjectForIUnknown(rawMenu);
            }
            finally
            {
                Marshal.Release(rawMenu);
            }

            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                return false;
            }

            int result = ((IContextMenu)contextMenuObject).QueryContextMenu(
                menu,
                0,
                FirstCommandId,
                LastCommandId,
                CmfNormal);
            return result >= 0 && GetMenuItemCount(menu) > 0;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or ExternalException)
        {
            return false;
        }
        finally
        {
            if (menu != IntPtr.Zero)
            {
                DestroyMenu(menu);
            }

            if (contextMenuObject is not null && Marshal.IsComObject(contextMenuObject))
            {
                Marshal.FinalReleaseComObject(contextMenuObject);
            }

            if (parent is not null && Marshal.IsComObject(parent))
            {
                Marshal.FinalReleaseComObject(parent);
            }

            if (absolutePidl != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(absolutePidl);
            }
        }
    }

    private sealed class ContextMenuMessageWindow : NativeWindow, IDisposable
    {
        private const int WmDrawItem = 0x002b;
        private const int WmMeasureItem = 0x002c;
        private const int WmInitMenuPopup = 0x0117;
        private const int WmMenuChar = 0x0120;

        private readonly IContextMenu2? contextMenu2;
        private readonly IContextMenu3? contextMenu3;

        internal ContextMenuMessageWindow(Control owner, object contextMenu)
        {
            contextMenu2 = contextMenu as IContextMenu2;
            contextMenu3 = contextMenu as IContextMenu3;
            AssignHandle(owner.Handle);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg is WmDrawItem or WmMeasureItem or WmInitMenuPopup or WmMenuChar)
            {
                if (contextMenu3 is not null)
                {
                    int result = contextMenu3.HandleMenuMsg2(
                        (uint)message.Msg,
                        message.WParam,
                        message.LParam,
                        out IntPtr menuResult);
                    if (result >= 0 && message.Msg == WmMenuChar)
                    {
                        message.Result = menuResult;
                        return;
                    }
                }
                else
                {
                    contextMenu2?.HandleMenuMsg((uint)message.Msg, message.WParam, message.LParam);
                }
            }

            base.WndProc(ref message);
        }

        public void Dispose() => ReleaseHandle();
    }

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr bindingContext, IntPtr displayName, ref uint eaten, out IntPtr itemIdList, ref uint attributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, uint flags, out IntPtr enumIdList);
        [PreserveSig] int BindToObject(IntPtr itemIdList, IntPtr bindingContext, ref Guid interfaceId, out IntPtr result);
        [PreserveSig] int BindToStorage(IntPtr itemIdList, IntPtr bindingContext, ref Guid interfaceId, out IntPtr result);
        [PreserveSig] int CompareIDs(IntPtr parameter, IntPtr first, IntPtr second);
        [PreserveSig] int CreateViewObject(IntPtr hwnd, ref Guid interfaceId, out IntPtr result);
        [PreserveSig] int GetAttributesOf(uint count, IntPtr itemIdLists, ref uint attributes);
        [PreserveSig] int GetUIObjectOf(IntPtr hwnd, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] itemIdLists, ref Guid interfaceId, IntPtr reserved, out IntPtr result);
        [PreserveSig] int GetDisplayNameOf(IntPtr itemIdList, uint flags, IntPtr name);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr itemIdList, IntPtr name, uint flags, out IntPtr renamedItemIdList);
    }

    [ComImport]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint firstCommandId, uint lastCommandId, uint flags);
        [PreserveSig] int InvokeCommand(ref CommandInvocation invocation);
        [PreserveSig] int GetCommandString(nuint commandId, uint flags, IntPtr reserved, IntPtr name, uint nameLength);
    }

    [ComImport]
    [Guid("000214F4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint firstCommandId, uint lastCommandId, uint flags);
        [PreserveSig] int InvokeCommand(ref CommandInvocation invocation);
        [PreserveSig] int GetCommandString(nuint commandId, uint flags, IntPtr reserved, IntPtr name, uint nameLength);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
    }

    [ComImport]
    [Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint firstCommandId, uint lastCommandId, uint flags);
        [PreserveSig] int InvokeCommand(ref CommandInvocation invocation);
        [PreserveSig] int GetCommandString(nuint commandId, uint flags, IntPtr reserved, IntPtr name, uint nameLength);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CommandInvocation
    {
        internal int Size;
        internal uint Mask;
        internal IntPtr Owner;
        internal IntPtr Verb;
        internal IntPtr Parameters;
        internal IntPtr Directory;
        internal int Show;
        internal uint HotKey;
        internal IntPtr Icon;
        internal IntPtr Title;
        internal IntPtr VerbUnicode;
        internal IntPtr ParametersUnicode;
        internal IntPtr DirectoryUnicode;
        internal IntPtr TitleUnicode;
        internal NativePoint InvokePoint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr itemIdList, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHBindToParent(IntPtr itemIdList, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellFolder parent, out IntPtr childItemIdList);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr menu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
}

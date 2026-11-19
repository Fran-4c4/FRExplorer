# Architecture

FRExplorer has three layers:

1. `ShellExplorerControl` owns WinForms state, selection, responsive layout and stale-result rejection.
2. `WindowsShellNamespace` enumerates the Windows Shell through `Shell.Application` and retrieves native system icons.
3. `StaTask` serializes Shell requests on one dedicated, long-lived background STA thread.

The UI thread never performs folder enumeration. Selecting another folder cancels the relevance of the previous result and increments a generation number. If an earlier request later completes, its icons are disposed and its entries are not applied.

The dedicated apartment prevents `Shell.Application` RCWs from being shared between short-lived COM apartments. Windows does not provide safe forced cancellation for every Shell namespace extension. A slow provider can therefore occupy the worker until it returns, but it cannot freeze the window or overwrite a newer selection. Requests cancelled while still queued are skipped before calling the Shell.

The control uses `DockStyle.Fill` internally and for its own default. Both the tree and contents list are docked to the resizable split container, so the component follows its parent in both dimensions and at different DPI scales.


# Architecture

FRExplorer has three layers:

1. `ShellExplorerControl` owns WinForms state, selection, responsive layout and stale-result rejection.
2. `WindowsShellNamespace` enumerates the Windows Shell through `Shell.Application` and retrieves native system icons.
3. `StaTask` runs each Shell request on a background STA thread.

The UI thread never performs folder enumeration. Selecting another folder cancels the relevance of the previous result and increments a generation number. If an earlier request later completes, its icons are disposed and its entries are not applied.

Windows does not provide safe forced cancellation for every Shell namespace extension. A slow provider can therefore keep its background thread alive until it returns, but it cannot freeze the window or overwrite a newer selection.

The control uses `DockStyle.Fill` internally and for its own default. Both the tree and contents list are docked to the resizable split container, so the component follows its parent in both dimensions and at different DPI scales.


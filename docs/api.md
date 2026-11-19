# Public API

## `ShellExplorerControl`

`ShellExplorerControl` is a WinForms `UserControl`. Its authoritative layout default is `DockStyle.Fill`; a host may replace `Dock` after construction.

### Properties

| Property | Contract |
|---|---|
| `ContentsPaneVisible` | `true` by default. Hides or shows the right contents pane without changing selection or cancelling work. |
| `ContentsContextMenu` | `null` by default. Assigns a caller-owned menu to the contents list; FRExplorer never disposes it. |
| `SelectedFolder` | Current tree folder, or `null` before selection. |
| `SelectedItems` | Snapshot of selected list items. Parsing names may represent virtual Shell items, not only file-system paths. |
| `IsBusy` | `true` while one or more Shell operations are still executing. A discarded Network request can remain busy until Windows returns. |

### Events

| Event | Timing |
|---|---|
| `SelectionChanged` | Raised immediately when the selected tree node changes, before its contents necessarily finish loading. |
| `ItemActivated` | Raised for an activated non-folder item. The library does not decide how the host opens it. |
| `BusyStateChanged` | Raised when `IsBusy` changes between idle and busy. |

### Methods

`ResetToDesktopAsync(CancellationToken)` initializes the control if necessary, selects Desktop and restores it to the top of the tree. An already-cancelled token prevents Shell work. Once Windows has entered a Shell call, cancellation discards the eventual result but cannot forcibly abort that operating-system call.

## `ShellItemInfo`

`DisplayName` is localized text supplied by Windows. `ParsingName` is the value accepted by Shell APIs; it is an absolute path only for physical items. `IsFolder` follows the Shell classification. Consumers must not use `Directory.Exists` as the only way to classify virtual folders.


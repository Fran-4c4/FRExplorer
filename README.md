# FRExplorer

FRExplorer is a reusable WinForms control that displays the real Windows Shell namespace. It includes Desktop, This PC, Network, Recycle Bin, virtual folders, drives and physical files with the icons and ordering supplied by Windows.

Shell enumeration and icon retrieval run asynchronously on a dedicated background STA worker, so slow locations such as Network do not block the UI. The control fills its parent by default and can display a compact tree or a tree with a resizable contents pane. Right-clicking an item opens the native Windows Shell context menu, including verbs installed by other applications. Physical items can be dragged from the contents pane using the standard Windows `FileDrop` format.

## Requirements

- Windows 10 or later.
- .NET 10 SDK.
- A WinForms application targeting `net10.0-windows`.

## Try the component

Clone the repository and run the included demo; no host application is required:

```powershell
dotnet run --project .\samples\FRExplorer.Demo -c Release
```

The demo lets you navigate the Shell, hide or show the contents pane, return to Desktop, open files with their associated Windows application, drag physical items and exercise their native Shell context menus.

## Use from source

Add FRExplorer as a Git submodule so the consuming repository records the exact source revision:

```powershell
git submodule add https://github.com/OWNER/FRExplorer.git external/FRExplorer
git submodule update --init --recursive
```

Reference the project from the consuming `.csproj`:

```xml
<ProjectReference Include="..\..\external\FRExplorer\src\FRExplorer\FRExplorer.csproj" />
```

Then place the control in a WinForms container:

```csharp
using FRExplorer;

var explorer = new ShellExplorerControl(); // DockStyle.Fill by default
explorer.ItemActivated += (_, e) => OpenWithWindows(e.Item.ParsingName);
Controls.Add(explorer);
```

See [docs/api.md](docs/api.md) for the public contract and [docs/architecture.md](docs/architecture.md) for threading and cancellation behavior.

## Build and test

```powershell
dotnet restore FRExplorer.slnx
dotnet build FRExplorer.slnx -c Release --no-restore
dotnet test FRExplorer.slnx -c Release --no-build
```

## License

FRExplorer is available under the [MIT License](LICENSE).


using System.ComponentModel;

namespace FRExplorer;

/// <summary>
/// A resizable WinForms browser for the Windows Shell namespace. Shell calls
/// run on a background STA worker; only completed, current results are applied
/// to the UI.
/// </summary>
[DefaultEvent(nameof(SelectionChanged))]
public sealed class ShellExplorerControl : UserControl
{
    private const string PlaceholderNodeName = "__frexplorer_placeholder__";
    private const int DefaultTreePaneWidth = 240;
    private const int MinimumTreePaneWidth = 110;
    private const int MinimumContentsPaneWidth = 150;

    private readonly SplitContainer browser = new();
    private readonly TreeView folders = new();
    private readonly ListView contents = new();
    private readonly ImageList shellIcons = new()
    {
        ColorDepth = ColorDepth.Depth32Bit,
        ImageSize = new Size(16, 16),
    };
    private readonly Dictionary<string, int> iconIndexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource contentsLoad = new();
    private Task? initialization;
    private int activeOperations;
    private long contentsGeneration;

    /// <summary>Creates an explorer that fills its parent and shows both panes by default.</summary>
    public ShellExplorerControl()
    {
        Dock = DockStyle.Fill;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = SystemColors.Window;
        BuildLayout();
        LoadDesktopRoot();
    }

    /// <summary>Raised immediately after the selected tree folder changes.</summary>
    public event EventHandler<ShellItemEventArgs>? SelectionChanged;

    /// <summary>Raised when a physical or virtual non-folder item is activated.</summary>
    public event EventHandler<ShellItemEventArgs>? ItemActivated;

    /// <summary>Raised whenever <see cref="IsBusy"/> changes.</summary>
    public event EventHandler? BusyStateChanged;

    /// <summary>
    /// Controls whether the contents list is visible. The authoritative default
    /// is <see langword="true"/>. Hiding it does not cancel enumeration or alter
    /// the selected folder; it only collapses the right pane.
    /// </summary>
    [DefaultValue(true)]
    public bool ContentsPaneVisible
    {
        get => !browser.Panel2Collapsed;
        set
        {
            browser.Panel2Collapsed = !value;
            SynchronizeCollapsedPanelBounds();
        }
    }

    /// <summary>
    /// Gets or sets the menu displayed by the contents list. The default is
    /// <see langword="null"/>. Ownership remains with the caller.
    /// </summary>
    [DefaultValue(null)]
    public ContextMenuStrip? ContentsContextMenu
    {
        get => contents.ContextMenuStrip;
        set => contents.ContextMenuStrip = value;
    }

    /// <summary>The folder currently selected in the tree, or <see langword="null"/>.</summary>
    [Browsable(false)]
    public ShellItemInfo? SelectedFolder => (folders.SelectedNode?.Tag as TreeNodeState)?.Item;

    /// <summary>
    /// Returns a snapshot of selected contents. Items may be physical paths or
    /// virtual Shell parsing names; callers must inspect <see cref="ShellItemInfo.ParsingName"/>.
    /// </summary>
    [Browsable(false)]
    public IReadOnlyList<ShellItemInfo> SelectedItems => contents.SelectedItems
        .Cast<ListViewItem>()
        .Select(item => item.Tag as ShellItemInfo)
        .Where(item => item is not null)
        .Cast<ShellItemInfo>()
        .ToArray();

    /// <summary>
    /// Indicates that one or more Shell calls are running. Slow virtual folders
    /// can remain busy after the user has selected another location because
    /// Windows does not provide a safe way to abort an in-progress Shell call.
    /// </summary>
    [Browsable(false)]
    public bool IsBusy => activeOperations > 0;

    /// <summary>
    /// Selects Desktop, scrolls it into view and asynchronously refreshes its
    /// children and contents. Cancellation discards the result and never applies
    /// a partial tree.
    /// </summary>
    public async Task ResetToDesktopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(true);
        if (IsDisposed || folders.Nodes.Count == 0)
        {
            return;
        }

        TreeNode desktop = folders.Nodes[0];
        folders.SelectedNode = desktop;
        folders.TopNode = desktop;
        desktop.Expand();
        desktop.EnsureVisible();
    }

    private void BuildLayout()
    {
        browser.Dock = DockStyle.Fill;
        browser.Orientation = Orientation.Vertical;
        browser.Size = new Size(640, 420);
        browser.SplitterDistance = DefaultTreePaneWidth;
        browser.SplitterWidth = 5;
        browser.Panel1MinSize = MinimumTreePaneWidth;
        browser.Panel2MinSize = MinimumContentsPaneWidth;
        browser.BackColor = SystemColors.ControlDark;

        folders.Dock = DockStyle.Fill;
        folders.HideSelection = false;
        folders.ShowRootLines = false;
        folders.ImageList = shellIcons;
        folders.BeforeExpand += HandleBeforeExpand;
        folders.AfterSelect += HandleAfterSelect;
        folders.NodeMouseClick += HandleFolderMouseClick;
        browser.Panel1.Controls.Add(folders);

        contents.Dock = DockStyle.Fill;
        contents.View = View.Details;
        contents.FullRowSelect = true;
        contents.MultiSelect = true;
        contents.HideSelection = false;
        contents.SmallImageList = shellIcons;
        contents.Columns.Add("Name", 220);
        contents.Columns.Add("Type", 90);
        contents.Columns.Add("Size", 90);
        contents.Columns.Add("Modified", 140);
        contents.DoubleClick += HandleContentsDoubleClick;
        contents.KeyDown += HandleContentsKeyDown;
        contents.ItemDrag += (_, _) => BeginFileDrag();
        contents.MouseUp += HandleContentsMouseUp;
        contents.Resize += (_, _) => ResizeNameColumn();
        browser.Panel2.Controls.Add(contents);

        Controls.Add(browser);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        SynchronizeCollapsedPanelBounds();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        SynchronizeCollapsedPanelBounds();
    }

    private void SynchronizeCollapsedPanelBounds()
    {
        if (!browser.Panel2Collapsed || browser.ClientSize.Width <= 0 || browser.ClientSize.Height <= 0)
        {
            return;
        }

        browser.Panel1.Bounds = browser.ClientRectangle;
        folders.Bounds = browser.Panel1.ClientRectangle;
    }

    private void LoadDesktopRoot()
    {
        using Bitmap? icon = WindowsShellIconProvider.GetSmallIcon(ShellItemInfo.DesktopParsingName);
        int iconIndex = AddIcon(ShellItemInfo.Desktop, icon is null ? null : new Bitmap(icon));
        var state = new TreeNodeState(ShellItemInfo.Desktop);
        var desktop = new TreeNode(ShellItemInfo.Desktop.DisplayName, iconIndex, iconIndex) { Tag = state };
        desktop.Nodes.Add(CreatePlaceholder());
        folders.Nodes.Add(desktop);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            await EnsureInitializedAsync(lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        initialization ??= InitializeCoreAsync(cancellationToken);
        return initialization;
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        if (folders.Nodes.Count == 0)
        {
            return;
        }

        TreeNode desktop = folders.Nodes[0];
        await LoadChildrenAsync(desktop, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        desktop.Expand();
        folders.SelectedNode = desktop;
        folders.TopNode = desktop;
    }

    private async void HandleBeforeExpand(object? sender, TreeViewCancelEventArgs eventArgs)
    {
        if (eventArgs.Node?.Tag is not TreeNodeState state || state.ChildrenLoaded || state.LoadingChildren)
        {
            return;
        }

        eventArgs.Cancel = true;
        try
        {
            await LoadChildrenAsync(eventArgs.Node, lifetime.Token).ConfigureAwait(true);
            if (!lifetime.IsCancellationRequested && !IsDisposed)
            {
                eventArgs.Node.Expand();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task LoadChildrenAsync(TreeNode node, CancellationToken cancellationToken)
    {
        if (node.Tag is not TreeNodeState state || state.ChildrenLoaded || state.LoadingChildren)
        {
            return;
        }

        state.LoadingChildren = true;
        SetNodeLoading(node);
        BeginOperation();
        IReadOnlyList<ShellEntry>? entries = null;
        try
        {
            entries = await WindowsShellNamespace.EnumerateAsync(
                state.Item.ParsingName,
                foldersOnly: true,
                cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDisposed)
            {
                return;
            }

            node.Nodes.Clear();
            foreach (ShellEntry entry in entries)
            {
                node.Nodes.Add(CreateNode(entry));
            }

            state.ChildrenLoaded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            state.LoadingChildren = false;
            if (entries is not null)
            {
                WindowsShellNamespace.DisposeEntries(entries);
            }

            EndOperation();
        }
    }

    private async void HandleAfterSelect(object? sender, TreeViewEventArgs eventArgs)
    {
        if (eventArgs.Node?.Tag is not TreeNodeState state)
        {
            return;
        }

        SelectionChanged?.Invoke(this, new ShellItemEventArgs(state.Item));
        await LoadContentsAsync(state.Item).ConfigureAwait(true);
    }

    private async Task LoadContentsAsync(ShellItemInfo folder)
    {
        contentsLoad.Cancel();
        contentsLoad.Dispose();
        contentsLoad = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        CancellationToken cancellationToken = contentsLoad.Token;
        long generation = Interlocked.Increment(ref contentsGeneration);
        ShowContentsStatus("Loading…");
        BeginOperation();
        IReadOnlyList<ShellEntry>? entries = null;
        try
        {
            entries = await WindowsShellNamespace.EnumerateAsync(
                folder.ParsingName,
                foldersOnly: false,
                cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested
                || generation != Interlocked.Read(ref contentsGeneration)
                || IsDisposed)
            {
                return;
            }

            contents.BeginUpdate();
            try
            {
                contents.Items.Clear();
                foreach (ShellEntry entry in entries)
                {
                    ShellItemInfo child = entry.Item;
                    var item = new ListViewItem(child.DisplayName, AddIcon(child, entry.SmallIcon)) { Tag = child };
                    item.SubItems.Add(child.IsFolder ? "Folder" : GetFileType(child.ParsingName));
                    item.SubItems.Add(child.IsFolder ? string.Empty : SafeSize(child.ParsingName));
                    item.SubItems.Add(SafeModified(child.ParsingName));
                    contents.Items.Add(item);
                }
            }
            finally
            {
                contents.EndUpdate();
                ResizeNameColumn();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (entries is not null)
            {
                WindowsShellNamespace.DisposeEntries(entries);
            }

            EndOperation();
        }
    }

    private async void HandleContentsDoubleClick(object? sender, EventArgs eventArgs) =>
        await ActivateSelectedItemAsync().ConfigureAwait(true);

    private async void HandleContentsKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode != Keys.Enter)
        {
            return;
        }

        eventArgs.Handled = true;
        await ActivateSelectedItemAsync().ConfigureAwait(true);
    }

    private async Task ActivateSelectedItemAsync()
    {
        if (contents.SelectedItems.Count != 1 || contents.SelectedItems[0].Tag is not ShellItemInfo item)
        {
            return;
        }

        if (!item.IsFolder)
        {
            ItemActivated?.Invoke(this, new ShellItemEventArgs(item));
            return;
        }

        TreeNode? selectedNode = folders.SelectedNode;
        if (selectedNode is null)
        {
            return;
        }

        await LoadChildrenAsync(selectedNode, lifetime.Token).ConfigureAwait(true);
        if (lifetime.IsCancellationRequested || IsDisposed)
        {
            return;
        }

        TreeNode? childNode = selectedNode.Nodes.Cast<TreeNode>()
            .FirstOrDefault(node => node.Tag is TreeNodeState state
                && string.Equals(state.Item.ParsingName, item.ParsingName, StringComparison.OrdinalIgnoreCase));
        if (childNode is not null)
        {
            folders.SelectedNode = childNode;
            childNode.EnsureVisible();
        }
    }

    private void HandleFolderMouseClick(object? sender, TreeNodeMouseClickEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Right || eventArgs.Node?.Tag is not TreeNodeState state)
        {
            return;
        }

        folders.SelectedNode = eventArgs.Node;
        WindowsShellContextMenu.Show(folders, state.Item.ParsingName, folders.PointToScreen(eventArgs.Location));
    }

    private void HandleContentsMouseUp(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Right || ContentsContextMenu is not null)
        {
            return;
        }

        ListViewItem? clicked = contents.GetItemAt(eventArgs.X, eventArgs.Y);
        if (clicked?.Tag is not ShellItemInfo item)
        {
            return;
        }

        if (!clicked.Selected)
        {
            contents.SelectedItems.Clear();
            clicked.Selected = true;
        }

        WindowsShellContextMenu.Show(contents, item.ParsingName, contents.PointToScreen(eventArgs.Location));
    }

    private TreeNode CreateNode(ShellEntry entry)
    {
        int iconIndex = AddIcon(entry.Item, entry.SmallIcon);
        var state = new TreeNodeState(entry.Item);
        var node = new TreeNode(entry.Item.DisplayName, iconIndex, iconIndex) { Tag = state };
        if (entry.Item.IsFolder)
        {
            node.Nodes.Add(CreatePlaceholder());
        }

        return node;
    }

    private static TreeNode CreatePlaceholder() => new() { Name = PlaceholderNodeName, Text = string.Empty };

    private static void SetNodeLoading(TreeNode node)
    {
        node.Nodes.Clear();
        node.Nodes.Add(new TreeNode("Loading…") { Name = PlaceholderNodeName });
    }

    private void ShowContentsStatus(string text)
    {
        contents.Items.Clear();
        contents.Items.Add(new ListViewItem(text));
    }

    private int AddIcon(ShellItemInfo item, Bitmap? suppliedIcon)
    {
        if (iconIndexes.TryGetValue(item.ParsingName, out int existing))
        {
            return existing;
        }

        Bitmap icon = suppliedIcon is null ? CreateFallbackIcon(item.IsFolder) : new Bitmap(suppliedIcon);
        int index = shellIcons.Images.Count;
        shellIcons.Images.Add(icon);
        iconIndexes[item.ParsingName] = index;
        return index;
    }

    private static Bitmap CreateFallbackIcon(bool folder)
    {
        var bitmap = new Bitmap(16, 16);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        using var brush = new SolidBrush(folder ? Color.Goldenrod : Color.SteelBlue);
        graphics.FillRectangle(brush, folder ? new Rectangle(1, 5, 14, 10) : new Rectangle(3, 1, 10, 14));
        return bitmap;
    }

    private void BeginFileDrag()
    {
        string[] paths = SelectedItems
            .Select(item => item.ParsingName)
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        DoDragDrop(new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy);
    }

    private void ResizeNameColumn()
    {
        if (contents.Columns.Count == 0)
        {
            return;
        }

        int remaining = contents.ClientSize.Width - contents.Columns.Cast<ColumnHeader>().Skip(1).Sum(column => column.Width) - 6;
        contents.Columns[0].Width = Math.Max(120, remaining);
    }

    private void BeginOperation()
    {
        bool wasBusy = IsBusy;
        activeOperations++;
        if (!wasBusy)
        {
            BusyStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void EndOperation()
    {
        bool wasBusy = IsBusy;
        activeOperations = Math.Max(0, activeOperations - 1);
        if (wasBusy != IsBusy && !IsDisposed)
        {
            BusyStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string GetFileType(string path)
    {
        string extension = Path.GetExtension(path).TrimStart('.');
        return extension.Length == 0 ? "File" : extension.ToUpperInvariant();
    }

    private static string SafeSize(string path)
    {
        try
        {
            long bytes = new FileInfo(path).Length;
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0 ? $"{bytes} B" : $"{value:0.##} {units[unit]}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return "—";
        }
    }

    private static string SafeModified(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return string.Empty;
            }

            return File.GetLastWriteTime(path).ToString("g", System.Globalization.CultureInfo.CurrentCulture);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return "—";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lifetime.Cancel();
            contentsLoad.Cancel();
            contentsLoad.Dispose();
            lifetime.Dispose();
            shellIcons.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class TreeNodeState(ShellItemInfo item)
    {
        internal ShellItemInfo Item { get; } = item;

        internal bool ChildrenLoaded { get; set; }

        internal bool LoadingChildren { get; set; }
    }
}


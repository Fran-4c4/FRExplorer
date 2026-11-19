using System.Runtime.ExceptionServices;
using Xunit;

namespace FRExplorer.Tests;

public sealed class ShellExplorerTests
{
    [Fact]
    public async Task DesktopNamespaceIsEnumeratedOffTheCallingThread()
    {
        IReadOnlyList<ShellEntry> entries = await WindowsShellNamespace.EnumerateAsync(
            ShellItemInfo.DesktopParsingName,
            foldersOnly: true,
            CancellationToken.None);
        try
        {
            Assert.NotEmpty(entries);
            Assert.All(entries, entry =>
            {
                Assert.True(entry.Item.IsFolder);
                Assert.False(string.IsNullOrWhiteSpace(entry.Item.DisplayName));
                Assert.False(string.IsNullOrWhiteSpace(entry.Item.ParsingName));
            });
        }
        finally
        {
            WindowsShellNamespace.DisposeEntries(entries);
        }
    }

    [Fact]
    public async Task ShellWorkerUsesABackgroundStaThread()
    {
        int callingThread = Environment.CurrentManagedThreadId;
        (int workerThread, ApartmentState apartment, bool background) = await StaTask.Run(
            () => (Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState(), Thread.CurrentThread.IsBackground),
            CancellationToken.None);

        Assert.NotEqual(callingThread, workerThread);
        Assert.Equal(ApartmentState.STA, apartment);
        Assert.True(background);
    }

    [Fact]
    public async Task CancelledEnumerationDoesNotStartShellWork()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            WindowsShellNamespace.EnumerateAsync(
                ShellItemInfo.DesktopParsingName,
                foldersOnly: true,
                cancellation.Token));
    }

    [Fact]
    public void ControlFillsItsParentAndDocumentsPaneDefault() => RunSta(() =>
    {
        using var parent = new Panel { Size = new Size(900, 600) };
        using var explorer = new ShellExplorerControl();
        parent.Controls.Add(explorer);

        Assert.Equal(DockStyle.Fill, explorer.Dock);
        Assert.True(explorer.ContentsPaneVisible);
        Assert.Equal(parent.ClientSize, explorer.Size);

        explorer.ContentsPaneVisible = false;
        Assert.False(explorer.ContentsPaneVisible);
    });

    [Fact]
    public void CompactTreeFillsANarrowParentVertically() => RunSta(() =>
    {
        using var form = new Form { ClientSize = new Size(180, 370), ShowInTaskbar = false, Opacity = 0 };
        using var explorer = new ShellExplorerControl { ContentsPaneVisible = false };
        form.Controls.Add(explorer);
        form.Show();
        Application.DoEvents();

        TreeView tree = Assert.Single(FindControls<TreeView>(explorer));
        SplitContainer split = Assert.Single(FindControls<SplitContainer>(explorer));
        Assert.Equal(split.ClientRectangle, split.Panel1.Bounds);
        Assert.Equal(split.Panel1.ClientRectangle, tree.Bounds);
    });

    [Fact]
    public void ShownControlLoadsDesktopTreeAndContents() => RunSta(() =>
    {
        Exception? asynchronousFailure = null;
        ThreadExceptionEventHandler handler = (_, eventArgs) => asynchronousFailure = eventArgs.Exception;
        Application.ThreadException += handler;
        try
        {
            using var form = new Form
            {
                ClientSize = new Size(900, 600),
                ShowInTaskbar = false,
                Opacity = 0,
            };
            using var explorer = new ShellExplorerControl();
            form.Controls.Add(explorer);
            form.Show();

            TreeView tree = Assert.Single(FindControls<TreeView>(explorer));
            ListView contents = Assert.Single(FindControls<ListView>(explorer));
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            do
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
            while (asynchronousFailure is null
                && (tree.Nodes[0].Nodes.Count <= 1
                    || contents.Items.Count == 0
                    || contents.Items[0].Text == "Loading…")
                && DateTime.UtcNow < deadline);

            Assert.Null(asynchronousFailure);
            Assert.True(tree.Nodes[0].Nodes.Count > 1);
            Assert.True(tree.Nodes[0].IsExpanded);
            Assert.NotEmpty(contents.Items.Cast<ListViewItem>());
            Assert.DoesNotContain(contents.Items.Cast<ListViewItem>(), item => item.Text == "Loading…");
        }
        finally
        {
            Application.ThreadException -= handler;
        }
    });

    [Fact]
    public void ClosingDuringAsynchronousLoadingDoesNotEscapeCancellation() => RunSta(() =>
    {
        Exception? asynchronousFailure = null;
        ThreadExceptionEventHandler handler = (_, eventArgs) => asynchronousFailure = eventArgs.Exception;
        Application.ThreadException += handler;
        try
        {
            using var form = new Form { ClientSize = new Size(900, 600), ShowInTaskbar = false, Opacity = 0 };
            var explorer = new ShellExplorerControl();
            form.Controls.Add(explorer);
            form.Show();
            Application.DoEvents();
            explorer.Dispose();

            DateTime settle = DateTime.UtcNow.AddMilliseconds(500);
            do
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
            while (DateTime.UtcNow < settle);

            Assert.Null(asynchronousFailure);
        }
        finally
        {
            Application.ThreadException -= handler;
        }
    });

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindControls<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}


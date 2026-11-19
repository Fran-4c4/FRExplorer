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


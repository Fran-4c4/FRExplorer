using System.Diagnostics;
using FRExplorer;

namespace FRExplorer.Demo;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new DemoForm());
    }
}

internal sealed class DemoForm : Form
{
    private readonly ShellExplorerControl explorer = new();
    private readonly ToolStripStatusLabel location = new("Desktop");
    private readonly ToolStripStatusLabel activity = new() { Alignment = ToolStripItemAlignment.Right };

    internal DemoForm()
    {
        Text = "FRExplorer demo";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 650);
        MinimumSize = new Size(640, 420);
        AutoScaleMode = AutoScaleMode.Dpi;

        var toolbar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        var desktop = new ToolStripButton("Desktop");
        var contents = new ToolStripButton("Contents") { CheckOnClick = true, Checked = true };
        desktop.Click += async (_, _) => await explorer.ResetToDesktopAsync().ConfigureAwait(true);
        contents.CheckedChanged += (_, _) => explorer.ContentsPaneVisible = contents.Checked;
        toolbar.Items.AddRange([desktop, contents]);

        var status = new StatusStrip();
        status.Items.AddRange([location, activity]);

        explorer.SelectionChanged += (_, eventArgs) => location.Text = eventArgs.Item.DisplayName;
        explorer.BusyStateChanged += (_, _) => activity.Text = explorer.IsBusy ? "Loading Shell data…" : "Ready";
        explorer.ItemActivated += (_, eventArgs) => OpenItem(eventArgs.Item);

        Controls.Add(explorer);
        Controls.Add(status);
        Controls.Add(toolbar);
    }

    private void OpenItem(ShellItemInfo item)
    {
        try
        {
            Process.Start(new ProcessStartInfo(item.ParsingName) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}


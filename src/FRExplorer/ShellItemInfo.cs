namespace FRExplorer;

/// <summary>Identifies an item returned by the Windows Shell namespace.</summary>
/// <param name="DisplayName">Localized label supplied by Windows.</param>
/// <param name="ParsingName">
/// Stable Shell parsing name. For physical items this is normally an absolute
/// path; virtual items may use a Shell namespace identifier.
/// </param>
/// <param name="IsFolder">Whether Windows exposes the item as a folder.</param>
public sealed record ShellItemInfo(string DisplayName, string ParsingName, bool IsFolder)
{
    /// <summary>The Shell parsing name of the Windows desktop root.</summary>
    public const string DesktopParsingName = "shell:Desktop";

    /// <summary>The logical desktop root used by <see cref="ShellExplorerControl"/>.</summary>
    public static ShellItemInfo Desktop { get; } = new("Desktop", DesktopParsingName, IsFolder: true);
}

/// <summary>Provides a Shell item for selection and activation events.</summary>
public sealed class ShellItemEventArgs(ShellItemInfo item) : EventArgs
{
    /// <summary>The item associated with the event.</summary>
    public ShellItemInfo Item { get; } = item ?? throw new ArgumentNullException(nameof(item));
}


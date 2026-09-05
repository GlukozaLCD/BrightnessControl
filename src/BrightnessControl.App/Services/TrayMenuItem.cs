namespace BrightnessControl.App.Services;

public sealed record TrayMenuItem
{
    public string Header { get; init; } = string.Empty;
    public uint? Id { get; init; }
    public bool IsSeparator { get; init; }
    public bool IsEnabled { get; init; } = true;
    public IReadOnlyList<TrayMenuItem>? SubItems { get; init; }

    public static TrayMenuItem Separator() => new() { IsSeparator = true };
}

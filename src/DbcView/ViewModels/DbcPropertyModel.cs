using System.Globalization;
using DbcView.Application;
using DbcView.Domain;

namespace DbcView.ViewModels;

/// <summary>
/// PanelModel for the DBC Properties panel. Holds read-only property values
/// for the selected file, message, or signal.
/// </summary>
public sealed class DbcPropertyModel
{
    public string? TargetName { get; private set; }

    public string? TargetType { get; private set; }

    public string? Id { get; private set; }

    public string? Name { get; private set; }

    public string? Length { get; private set; }

    public string? Transmitter { get; private set; }

    public string? StartBit { get; private set; }

    public string? Factor { get; private set; }

    public string? Offset { get; private set; }

    public string? Min { get; private set; }

    public string? Max { get; private set; }

    public event Action? StateChanged;

    public void ShowFile(DbcFileEntry file)
    {
        ArgumentNullException.ThrowIfNull(file);

        Reset();
        TargetType = "File";
        TargetName = file.Id.Key;
        Name = file.Id.Key;
        Notify();
    }

    public void ShowMessage(DbcMessage msg)
    {
        Reset();
        TargetType = "Message";
        TargetName = msg.Name;
        Id = msg.Id.ToString(CultureInfo.InvariantCulture);
        Name = msg.Name;
        Length = msg.Length.ToString(CultureInfo.InvariantCulture);
        Transmitter = msg.Transmitter;
        Notify();
    }

    public void ShowSignal(DbcSignal sig)
    {
        Reset();
        TargetType = "Signal";
        TargetName = sig.Name;
        Name = sig.Name;
        StartBit = sig.StartBit.ToString(CultureInfo.InvariantCulture);
        Length = sig.Length.ToString(CultureInfo.InvariantCulture);
        Factor = FormatNumber(sig.Factor);
        Offset = FormatNumber(sig.Offset);
        Min = FormatNumber(sig.Min);
        Max = FormatNumber(sig.Max);
        Notify();
    }

    private void Reset()
    {
        TargetName = null;
        TargetType = null;
        Id = null;
        Name = null;
        Length = null;
        Transmitter = null;
        StartBit = null;
        Factor = null;
        Offset = null;
        Min = null;
        Max = null;
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private void Notify()
    {
        StateChanged?.Invoke();
    }
}

namespace DbcView.Domain;

/// <summary>
/// Immutable in-memory representation of a parsed .dbc (CAN database) document.
/// Messages and raw (<c>CM_</c>/<c>BA_</c>/<c>VAL_</c>/…) lines are kept in one
/// ordered section list so serialization can preserve their relative position.
/// </summary>
public sealed class DbcDocument
{
    public DbcDocument(
        string version,
        IReadOnlyList<string> nodes,
        string headerSections,
        IReadOnlyList<DbcSection> sections)
    {
        Version = version;
        Nodes = nodes;
        HeaderSections = headerSections;
        Sections = sections;
        Messages = sections.OfType<DbcMessageSection>().Select(section => section.Message).ToArray();
    }

    public string Version { get; }

    /// <summary>Node names declared by the <c>BU_</c> section.</summary>
    public IReadOnlyList<string> Nodes { get; }

    /// <summary>Raw <c>NS_</c>/<c>BS_</c> header blocks between <c>VERSION</c> and <c>BU_</c>, preserved verbatim.</summary>
    public string HeaderSections { get; }

    /// <summary>Messages and raw sections in file order.</summary>
    public IReadOnlyList<DbcSection> Sections { get; }

    /// <summary>Messages declared by <c>BO_</c> sections, in file order.</summary>
    public IReadOnlyList<DbcMessage> Messages { get; }
}

/// <summary>An ordered piece of a DBC document body.</summary>
public abstract class DbcSection
{
}

/// <summary>A <c>BO_</c> message and its signals.</summary>
public sealed class DbcMessageSection : DbcSection
{
    public DbcMessageSection(DbcMessage message)
    {
        Message = message;
    }

    public DbcMessage Message { get; }
}

/// <summary>Raw non-message lines preserved verbatim, including blank lines.</summary>
public sealed class DbcRawSection : DbcSection
{
    public DbcRawSection(string text)
    {
        Text = text;
    }

    public string Text { get; }
}

/// <summary>A CAN message declared by a <c>BO_</c> section.</summary>
public sealed class DbcMessage
{
    public DbcMessage(
        uint id,
        string name,
        byte length,
        string transmitter,
        IReadOnlyList<DbcSignal> signals)
    {
        Id = id;
        Name = name;
        Length = length;
        Transmitter = transmitter;
        Signals = signals;
    }

    public uint Id { get; }

    public string Name { get; }

    public byte Length { get; }

    public string Transmitter { get; }

    /// <summary>Signals declared by <c>SG_</c> lines, in file order.</summary>
    public IReadOnlyList<DbcSignal> Signals { get; }
}

/// <summary>A CAN signal declared by an <c>SG_</c> line.</summary>
public sealed class DbcSignal
{
    public DbcSignal(
        string name,
        int startBit,
        int length,
        bool isBigEndian,
        bool isSigned,
        double factor,
        double offset,
        double min,
        double max,
        string unit,
        string receiver,
        string? multiplexer = null)
    {
        Name = name;
        StartBit = startBit;
        Length = length;
        IsBigEndian = isBigEndian;
        IsSigned = isSigned;
        Factor = factor;
        Offset = offset;
        Min = min;
        Max = max;
        Unit = unit;
        Receiver = receiver;
        Multiplexer = multiplexer;
    }

    public string Name { get; }

    public int StartBit { get; }

    public int Length { get; }

    /// <summary><c>@1</c> big-endian (Motorola) vs <c>@0</c> little-endian (Intel).</summary>
    public bool IsBigEndian { get; }

    /// <summary><c>-</c> signed vs <c>+</c> unsigned representation.</summary>
    public bool IsSigned { get; }

    public double Factor { get; }

    public double Offset { get; }

    public double Min { get; }

    public double Max { get; }

    public string Unit { get; }

    public string Receiver { get; }

    /// <summary><c>M</c> (multiplexor) or <c>m&lt;n&gt;</c> (multiplexed) marker, when present.</summary>
    public string? Multiplexer { get; }
}

using System.Globalization;
using DbcView.Domain;

namespace DbcView.Web.ViewModels;

public enum DbcPaletteResultKind
{
    Message,
    Signal,
}

public enum DbcPaletteMatchRank
{
    Exact,
    Prefix,
    Substring,
}

public sealed record DbcPaletteResult(
    DbcPaletteResultKind Kind,
    int MessageIndex,
    int SignalIndex,
    DbcMessage Message,
    DbcSignal? Signal,
    DbcPaletteMatchRank Rank,
    DbcCommentEntry? Comment = null)
{
    public string Key => Kind == DbcPaletteResultKind.Message
        ? string.Create(CultureInfo.InvariantCulture, $"m:{MessageIndex}")
        : string.Create(CultureInfo.InvariantCulture, $"s:{MessageIndex}:{SignalIndex}");

    public string PrimaryText => Signal?.Name ?? Message.Name;

    public string SecondaryText => Signal is null
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"0x{Message.Id:X} · {Message.Id} · {Message.Transmitter}")
        : string.Create(
            CultureInfo.InvariantCulture,
            $"0x{Message.Id:X} · {Message.Name} · {Signal.Receiver}");
}

/// <summary>A workspace command offered by the palette (for example, re-run validation).</summary>
public sealed record DbcPaletteCommand(
    string Id,
    string Title,
    string Detail,
    DbcPaletteMatchRank Rank);

/// <summary>
/// Deterministic search over the active document's messages, signals and
/// workspace commands. Matching is exact &gt; prefix &gt; substring, ties keep
/// source order, and the grouped result list is capped at 50 entries.
/// </summary>
public sealed class DbcCommandPaletteModel
{
    public const int MaxResults = 50;
    public const string CheckDocumentCommandId = "check-document";

    private static readonly CommandDefinition[] Commands =
    {
        new(
            CheckDocumentCommandId,
            "Check current document",
            "Re-run DBC validation for the active file (Ctrl+Enter)",
            "check validate validation recheck problems diagnostics"),
    };

    private DbcDocument? _document;
    private List<DbcCommentEntry>?[] _messageCommentsByIndex = Array.Empty<List<DbcCommentEntry>?>();
    private List<DbcCommentEntry>?[][] _signalCommentsByIndex = Array.Empty<List<DbcCommentEntry>?[]>();
    private IReadOnlyList<DbcPaletteCommand> _commandResults = Array.Empty<DbcPaletteCommand>();
    private IReadOnlyList<DbcPaletteResult> _messageResults = Array.Empty<DbcPaletteResult>();
    private IReadOnlyList<DbcPaletteResult> _signalResults = Array.Empty<DbcPaletteResult>();

    public DbcDocument? Document => _document;

    public string Query { get; private set; } = string.Empty;

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    public IReadOnlyList<DbcPaletteCommand> CommandResults => _commandResults;

    public IReadOnlyList<DbcPaletteResult> MessageResults => _messageResults;

    public IReadOnlyList<DbcPaletteResult> SignalResults => _signalResults;

    public int ResultCount =>
        _commandResults.Count + _messageResults.Count + _signalResults.Count;

    public int TotalMatchCount { get; private set; }

    public void SetDocument(DbcDocument? document, DbcSemanticIndex? semantics = null)
    {
        _document = document;
        _messageCommentsByIndex = Array.Empty<List<DbcCommentEntry>?>();
        _signalCommentsByIndex = Array.Empty<List<DbcCommentEntry>?[]>();
        if (document is not null)
        {
            _messageCommentsByIndex = new List<DbcCommentEntry>?[document.Messages.Count];
            _signalCommentsByIndex = new List<DbcCommentEntry>?[document.Messages.Count][];
            for (var messageIndex = 0; messageIndex < document.Messages.Count; messageIndex++)
            {
                _signalCommentsByIndex[messageIndex] =
                    new List<DbcCommentEntry>?[document.Messages[messageIndex].Signals.Count];
            }
        }

        if (document is not null && semantics is not null)
        {
            var messageIndexes = new Dictionary<uint, int>();
            for (var index = 0; index < document.Messages.Count; index++)
            {
                messageIndexes.TryAdd(document.Messages[index].Id, index);
            }

            foreach (var comment in semantics.MessageComments)
            {
                if (comment.MessageId is not uint messageId
                    || !messageIndexes.TryGetValue(messageId, out var messageIndex))
                {
                    continue;
                }

                _messageCommentsByIndex[messageIndex] ??= new List<DbcCommentEntry>();
                _messageCommentsByIndex[messageIndex]!.Add(comment);
            }

            foreach (var comment in semantics.SignalComments)
            {
                if (comment.MessageId is not uint messageId
                    || comment.SignalName is not { Length: > 0 } signalName
                    || !messageIndexes.TryGetValue(messageId, out var messageIndex))
                {
                    continue;
                }

                var message = document.Messages[messageIndex];
                for (var signalIndex = 0; signalIndex < message.Signals.Count; signalIndex++)
                {
                    if (!string.Equals(
                            message.Signals[signalIndex].Name,
                            signalName,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    _signalCommentsByIndex[messageIndex][signalIndex] ??=
                        new List<DbcCommentEntry>();
                    _signalCommentsByIndex[messageIndex][signalIndex]!.Add(comment);
                    break;
                }
            }
        }

        Query = string.Empty;
        _commandResults = Array.Empty<DbcPaletteCommand>();
        _messageResults = Array.Empty<DbcPaletteResult>();
        _signalResults = Array.Empty<DbcPaletteResult>();
        TotalMatchCount = 0;
    }

    public void SetQuery(string? query)
    {
        var value = query ?? string.Empty;
        if (string.Equals(Query, value, StringComparison.Ordinal))
        {
            return;
        }

        Query = value;
        Refresh();
    }

    private void Refresh()
    {
        TotalMatchCount = 0;
        var needle = Query.Trim();
        if (_document is null || needle.Length == 0)
        {
            _commandResults = Array.Empty<DbcPaletteCommand>();
            _messageResults = Array.Empty<DbcPaletteResult>();
            _signalResults = Array.Empty<DbcPaletteResult>();
            return;
        }

        _commandResults = MatchCommands(needle);
        var exact = new List<DbcPaletteResult>(MaxResults);
        var prefix = new List<DbcPaletteResult>(MaxResults);
        var substring = new List<DbcPaletteResult>(MaxResults);
        var total = _commandResults.Count;
        var numericQuery = TryParseMessageId(needle, out var numericMessageId);

        for (var messageIndex = 0; messageIndex < _document.Messages.Count; messageIndex++)
        {
            var message = _document.Messages[messageIndex];
            var messageRank = MatchMessage(
                message,
                needle,
                numericQuery,
                numericMessageId);
            var commentRank = MatchComments(
                _messageCommentsByIndex[messageIndex],
                needle,
                out var messageComment);
            var bestMessageRank = Best(messageRank, commentRank);
            if (bestMessageRank is not null)
            {
                total++;
                Add(exact, prefix, substring, new DbcPaletteResult(
                    DbcPaletteResultKind.Message,
                    messageIndex,
                    -1,
                    message,
                    null,
                    bestMessageRank.Value,
                    commentRank == bestMessageRank ? messageComment : null));
            }

            for (var signalIndex = 0; signalIndex < message.Signals.Count; signalIndex++)
            {
                var signal = message.Signals[signalIndex];
                var signalRank = MatchSignal(signal, needle);
                var signalCommentRank = MatchComments(
                    _signalCommentsByIndex[messageIndex][signalIndex],
                    needle,
                    out var signalComment);
                var bestSignalRank = Best(signalRank, signalCommentRank);
                if (bestSignalRank is null)
                {
                    continue;
                }

                total++;
                Add(exact, prefix, substring, new DbcPaletteResult(
                    DbcPaletteResultKind.Signal,
                    messageIndex,
                    signalIndex,
                    message,
                    signal,
                    bestSignalRank.Value,
                    signalCommentRank == bestSignalRank ? signalComment : null));
            }
        }

        TotalMatchCount = total;

        var ranked = new List<DbcPaletteResult>(
            Math.Min(MaxResults, exact.Count + prefix.Count + substring.Count));
        AppendBounded(ranked, exact);
        AppendBounded(ranked, prefix);
        AppendBounded(ranked, substring);

        var messages = new List<DbcPaletteResult>();
        var signals = new List<DbcPaletteResult>();
        foreach (var result in ranked)
        {
            if (result.Kind == DbcPaletteResultKind.Message)
            {
                messages.Add(result);
            }
            else
            {
                signals.Add(result);
            }
        }

        _messageResults = messages;
        _signalResults = signals;
    }

    private static void Add(
        List<DbcPaletteResult> exact,
        List<DbcPaletteResult> prefix,
        List<DbcPaletteResult> substring,
        DbcPaletteResult result)
    {
        var bucket = result.Rank switch
        {
            DbcPaletteMatchRank.Exact => exact,
            DbcPaletteMatchRank.Prefix => prefix,
            _ => substring,
        };

        if (bucket.Count < MaxResults)
        {
            bucket.Add(result);
        }
    }

    private static void AppendBounded(
        List<DbcPaletteResult> target,
        List<DbcPaletteResult> source)
    {
        foreach (var result in source)
        {
            if (target.Count >= MaxResults)
            {
                return;
            }

            target.Add(result);
        }
    }

    private static IReadOnlyList<DbcPaletteCommand> MatchCommands(string query)
    {
        List<DbcPaletteCommand>? matches = null;
        foreach (var command in Commands)
        {
            var rank = MatchCommand(command, query);
            if (rank is null)
            {
                continue;
            }

            matches ??= new List<DbcPaletteCommand>(Commands.Length);
            matches.Add(new DbcPaletteCommand(
                command.Id,
                command.Title,
                command.Detail,
                rank.Value));
        }

        return matches is null ? Array.Empty<DbcPaletteCommand>() : matches;
    }

    private static DbcPaletteMatchRank? MatchCommand(
        CommandDefinition command,
        string query)
    {
        var rank = MatchText(command.Title, query);
        foreach (var keyword in command.Keywords.Split(
                     ' ',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            rank = Best(rank, MatchText(keyword, query));
        }

        return Best(rank, MatchText(command.Detail, query));
    }

    private static DbcPaletteMatchRank? MatchMessage(
        DbcMessage message,
        string query,
        bool numericQuery,
        uint numericMessageId)
    {
        var rank = MatchText(message.Name, query);
        rank = Best(rank, MatchText(message.Transmitter, query));
        if (numericQuery && message.Id == numericMessageId)
        {
            rank = Best(rank, DbcPaletteMatchRank.Exact);
        }

        return rank;
    }

    private static bool TryParseMessageId(string query, out uint messageId)
    {
        const NumberStyles style = NumberStyles.AllowHexSpecifier;
        if (query.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(
                query.AsSpan(2),
                style,
                CultureInfo.InvariantCulture,
                out messageId))
        {
            return true;
        }

        return uint.TryParse(
            query,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out messageId);
    }

    private static DbcPaletteMatchRank? MatchSignal(DbcSignal signal, string query)
    {
        var rank = MatchText(signal.Name, query);
        rank = Best(rank, MatchText(signal.Receiver, query));
        return rank;
    }

    private static DbcPaletteMatchRank? MatchComments(
        IReadOnlyList<DbcCommentEntry>? comments,
        string query,
        out DbcCommentEntry? match)
    {
        match = null;
        DbcPaletteMatchRank? rank = null;
        if (comments is null)
        {
            return null;
        }

        foreach (var comment in comments)
        {
            var candidate = MatchText(comment.Text, query);
            if (candidate is null)
            {
                continue;
            }

            if (rank is null || candidate < rank)
            {
                rank = candidate;
                match = comment;
            }
        }

        return rank;
    }

    private static DbcPaletteMatchRank? MatchText(string value, string query)
    {
        if (value.Length == 0)
        {
            return null;
        }

        if (value.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return DbcPaletteMatchRank.Exact;
        }

        if (value.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return DbcPaletteMatchRank.Prefix;
        }

        return value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            ? DbcPaletteMatchRank.Substring
            : null;
    }

    private static DbcPaletteMatchRank? Best(
        DbcPaletteMatchRank? current,
        DbcPaletteMatchRank? candidate)
    {
        if (current is null)
        {
            return candidate;
        }

        if (candidate is null)
        {
            return current;
        }

        return candidate < current ? candidate : current;
    }

    private readonly record struct CommandDefinition(
        string Id,
        string Title,
        string Detail,
        string Keywords);
}

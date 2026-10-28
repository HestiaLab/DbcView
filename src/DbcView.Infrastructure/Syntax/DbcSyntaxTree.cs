using System.Globalization;
using System.Text;

namespace DbcView.Infrastructure.Syntax;

internal readonly record struct DbcTextSpan(int Start, int Length)
{
    public int End => Start + Length;
}

internal sealed record DbcTextChange(DbcTextSpan Span, string NewText);

internal enum DbcStatementKind
{
    Message,
    Signal,
    Comment,
    Raw,
}

internal enum DbcTokenKind
{
    Identifier,
    Number,
    String,
    Symbol,
}

internal sealed class DbcSyntaxToken
{
    public DbcSyntaxToken(DbcTokenKind kind, int start, int length)
    {
        Kind = kind;
        Start = start;
        Length = length;
    }

    public DbcTokenKind Kind { get; }

    public int Start { get; }

    public int Length { get; }
}

internal sealed record DbcMessageInfo(
    uint Id,
    string Name,
    byte Length,
    string Transmitter);

internal sealed record DbcSignalInfo(
    string Name,
    int StartBit,
    int Length,
    bool IsBigEndian,
    bool IsSigned);

internal struct DbcStatementRecord
{
    public DbcStatementKind Kind;
    public int RelativeStart;
    public int Length;
    public long GreenId;
}

internal readonly record struct DbcSpannedRecord(
    DbcStatementKind Kind,
    int Start,
    int Length,
    long GreenId);

internal sealed class DbcStatementBlock
{
    public DbcStatementBlock(int start, int firstIndex, DbcStatementRecord[] records)
    {
        Start = start;
        FirstIndex = firstIndex;
        Records = records;
        Length = records.Length == 0
            ? 0
            : records[^1].RelativeStart + records[^1].Length;
    }

    public int Start { get; }

    public int FirstIndex { get; }

    public int Length { get; }

    public DbcStatementRecord[] Records { get; }
}

internal readonly record struct DbcRecordHandle(
    DbcStatementBlock Block,
    int BlockIndex,
    int LocalIndex,
    DbcStatementRecord Record,
    int AbsoluteStart);

internal sealed class DbcStatement
{
    private readonly DbcSyntaxTree _tree;
    private readonly DbcRecordHandle _handle;
    private string? _rawText;
    private IReadOnlyList<DbcSyntaxToken>? _tokens;
    private DbcMessageInfo? _message;
    private DbcSignalInfo? _signal;
    private string? _comment;
    private bool _messageMaterialized;
    private bool _signalMaterialized;
    private bool _commentMaterialized;

    internal DbcStatement(DbcSyntaxTree tree, DbcRecordHandle handle)
    {
        _tree = tree;
        _handle = handle;
    }

    public DbcStatementKind Kind => _handle.Record.Kind;

    public long GreenId => _handle.Record.GreenId;

    public int Start => _handle.AbsoluteStart;

    public int FullWidth => _handle.Record.Length;

    public string RawText => _rawText ??= _tree.GetSlice(Start, FullWidth);

    public IReadOnlyList<DbcSyntaxToken> Tokens => _tokens ??= DbcSyntaxTree.Lex(RawText);

    public DbcMessageInfo? Message
    {
        get
        {
            if (_messageMaterialized)
            {
                return _message;
            }

            _messageMaterialized = true;
            if (Kind == DbcStatementKind.Message)
            {
                DbcSyntaxTree.TryParseMessage(RawText, out _message);
            }

            return _message;
        }
    }

    public DbcSignalInfo? Signal
    {
        get
        {
            if (_signalMaterialized)
            {
                return _signal;
            }

            _signalMaterialized = true;
            if (Kind == DbcStatementKind.Signal)
            {
                DbcSyntaxTree.TryParseSignal(RawText, out _signal);
            }

            return _signal;
        }
    }

    public string? Comment
    {
        get
        {
            if (_commentMaterialized)
            {
                return _comment;
            }

            _commentMaterialized = true;
            if (Kind == DbcStatementKind.Comment)
            {
                DbcSyntaxTree.TryParseComment(RawText, out _comment);
            }

            return _comment;
        }
    }
}

internal sealed class DbcStatementList : IReadOnlyList<DbcStatement>
{
    private readonly DbcSyntaxTree _tree;

    internal DbcStatementList(DbcSyntaxTree tree)
    {
        _tree = tree;
    }

    public int Count => _tree.StatementCount;

    public DbcStatement this[int index] => _tree.GetStatement(index);

    public IEnumerator<DbcStatement> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return _tree.GetStatement(index);
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

internal sealed class DbcRedDocument
{
    private readonly DbcSyntaxTree _tree;
    private DbcRedStatementList? _statements;

    internal DbcRedDocument(DbcSyntaxTree tree)
    {
        _tree = tree;
    }

    public DbcTextSpan FullSpan => new(0, _tree.TextLength);

    public IReadOnlyList<DbcRedStatement> Statements =>
        _statements ??= new DbcRedStatementList(_tree);
}

internal sealed class DbcRedStatementList : IReadOnlyList<DbcRedStatement>
{
    private readonly DbcSyntaxTree _tree;

    internal DbcRedStatementList(DbcSyntaxTree tree)
    {
        _tree = tree;
    }

    public int Count => _tree.StatementCount;

    public DbcRedStatement this[int index] => new(_tree, index);

    public IEnumerator<DbcRedStatement> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

internal sealed class DbcRedStatement
{
    private readonly DbcSyntaxTree _tree;
    private readonly int _index;
    private IReadOnlyList<DbcRedToken>? _tokens;

    internal DbcRedStatement(DbcSyntaxTree tree, int index)
    {
        _tree = tree;
        _index = index;
    }

    public int Position => _tree.GetHandle(_index).AbsoluteStart;

    public DbcTextSpan FullSpan => new(Position, _tree.GetHandle(_index).Record.Length);

    public int FullWidth => FullSpan.Length;

    public DbcStatement GreenStatement => _tree.GetStatement(_index);

    public IReadOnlyList<DbcRedToken> Tokens
    {
        get
        {
            if (_tokens is not null)
            {
                return _tokens;
            }

            var tokens = new List<DbcRedToken>();
            var green = GreenStatement;
            foreach (var token in green.Tokens)
            {
                tokens.Add(new DbcRedToken(this, token, green.Start + token.Start));
            }

            _tokens = tokens;
            return _tokens;
        }
    }
}

internal sealed class DbcRedToken
{
    public DbcRedToken(DbcRedStatement parent, DbcSyntaxToken green, int position)
    {
        Parent = parent;
        Green = green;
        Position = position;
    }

    public DbcRedStatement Parent { get; }

    public DbcSyntaxToken Green { get; }

    public int Position { get; }

    public DbcTextSpan Span => new(Position, Green.Length);
}

internal sealed class DbcSyntaxTree
{
    private const int BlockSize = 512;

    private static readonly HashSet<string> KeywordSet = new(StringComparer.Ordinal)
    {
        "VERSION",
        "NS_",
        "BS_",
        "BU_",
        "BO_",
        "SG_",
        "CM_",
        "BA_DEF_",
        "BA_DEF_DEF_",
        "BA_",
        "VAL_",
        "VAL_TABLE_",
        "EV_",
        "EV_DATA_",
        "ENVVAR_DATA_",
        "SGTYPE_",
        "SGTYPE_VAL_",
        "SIG_TYPE_REF_",
        "BO_TX_BU_",
        "SIG_GROUP_",
        "SIG_VALTYPE_",
        "SIGTYPE_VALTYPE_",
        "SG_MUL_VAL_",
        "BA_DEF_REL_",
        "BA_REL_",
        "BA_DEF_DEF_REL_",
        "BA_DEF_SGTYPE_",
        "BA_SGTYPE_",
        "BU_SG_REL_",
        "BU_EV_REL_",
        "BU_BO_REL_",
        "CAT_DEF_",
        "CAT_",
        "FILTER",
        "NS_DESC_",
        "VECTOR__INDEPENDENT_SIG_MSG",
        "VECTOR__XXX",
    };

    private static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> KeywordLookup =
        KeywordSet.GetAlternateLookup<ReadOnlySpan<char>>();

    private readonly DbcTextBuffer _text;
    private readonly DbcStatementBlock[] _blocks;
    private readonly int _statementCount;
    private readonly long _nextGreenId;
    private DbcStatementList? _statements;
    private DbcRedDocument? _root;

    private DbcSyntaxTree(
        DbcTextBuffer text,
        DbcStatementBlock[] blocks,
        int statementCount,
        long nextGreenId)
    {
        _text = text;
        _blocks = blocks;
        _statementCount = statementCount;
        _nextGreenId = nextGreenId;
    }

    internal int TextLength => _text.Length;

    internal int StatementCount => _statementCount;

    internal IReadOnlyList<DbcStatement> Statements =>
        _statements ??= new DbcStatementList(this);

    internal DbcRedDocument Root => _root ??= new DbcRedDocument(this);

    internal int FindFirstKind(DbcStatementKind kind)
    {
        for (var index = 0; index < _statementCount; index++)
        {
            if (GetHandle(index).Record.Kind == kind)
            {
                return index;
            }
        }

        return -1;
    }

    internal DbcRecordHandle GetHandle(int index)
    {
        if (index < 0 || index >= _statementCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var low = 0;
        var high = _blocks.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (_blocks[middle].FirstIndex <= index)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        var block = _blocks[low];
        var local = index - block.FirstIndex;
        var record = block.Records[local];
        return new DbcRecordHandle(
            block,
            low,
            local,
            record,
            block.Start + record.RelativeStart);
    }

    internal DbcStatement GetStatement(int index) => new(this, GetHandle(index));

    internal long GetGreenId(int index) => GetHandle(index).Record.GreenId;

    internal string GetSlice(int start, int length) => _text.Slice(start, length);

    internal string GetText() => _text.GetText();

    internal static DbcSyntaxTree Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var buffer = DbcTextBuffer.FromText(text);
        var scanned = Scan(text, 0, 0);
        var blocks = BuildBlocks(scanned, 0);
        return new DbcSyntaxTree(buffer, blocks, scanned.Count, scanned.Count);
    }

    internal DbcSyntaxTree WithChanges(DbcTextChange change)
    {
        var span = change.Span;
        if (span.Start < 0
            || span.Length < 0
            || span.End > _text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(change));
        }

        var newText = _text.Replace(span.Start, span.Length, change.NewText);
        if (_statementCount == 0)
        {
            return new DbcSyntaxTree(
                newText,
                Array.Empty<DbcStatementBlock>(),
                0,
                _nextGreenId);
        }

        var first = FindFirstRecord(span.Start, span.End);
        var last = FindLastRecord(span.End, first);
        var firstBlockIndex = first.BlockIndex;
        var lastBlockIndex = last.BlockIndex;
        var regionStart = first.AbsoluteStart;
        var regionEnd = last.AbsoluteStart + last.Record.Length;
        var delta = change.NewText.Length - span.Length;
        var regionLength = regionEnd - regionStart + delta;
        if (regionLength < 0)
        {
            return Parse(newText.GetText());
        }

        var regionText = newText.Slice(regionStart, regionLength);
        var reparsed = Scan(regionText, regionStart, _nextGreenId);

        var combined = new List<DbcSpannedRecord>();
        var firstBlock = _blocks[firstBlockIndex];
        for (var local = 0; local < first.LocalIndex; local++)
        {
            var record = firstBlock.Records[local];
            combined.Add(new DbcSpannedRecord(
                record.Kind,
                firstBlock.Start + record.RelativeStart,
                record.Length,
                record.GreenId));
        }

        combined.AddRange(reparsed);

        var lastBlock = _blocks[lastBlockIndex];
        for (var local = last.LocalIndex + 1; local < lastBlock.Records.Length; local++)
        {
            var record = lastBlock.Records[local];
            combined.Add(new DbcSpannedRecord(
                record.Kind,
                lastBlock.Start + record.RelativeStart + delta,
                record.Length,
                record.GreenId));
        }

        var firstIndex = firstBlock.FirstIndex;
        var rebuilt = BuildBlocks(combined, firstIndex);
        var removed = last.BlockIndex == first.BlockIndex && last.BlockIndex == firstBlockIndex
            ? last.LocalIndex - first.LocalIndex + 1
            : (firstBlock.Records.Length - first.LocalIndex)
                + (last.LocalIndex + 1)
                + SumCounts(firstBlockIndex + 1, lastBlockIndex - 1);
        var countDelta = reparsed.Count - removed;

        var result = new List<DbcStatementBlock>(
            firstBlockIndex + rebuilt.Length + (_blocks.Length - lastBlockIndex - 1));
        for (var index = 0; index < firstBlockIndex; index++)
        {
            result.Add(_blocks[index]);
        }

        result.AddRange(rebuilt);
        for (var index = lastBlockIndex + 1; index < _blocks.Length; index++)
        {
            var block = _blocks[index];
            result.Add(new DbcStatementBlock(
                block.Start + delta,
                block.FirstIndex + countDelta,
                block.Records));
        }

        return new DbcSyntaxTree(
            newText,
            result.ToArray(),
            _statementCount - removed + reparsed.Count,
            _nextGreenId + reparsed.Count);
    }

    internal static IReadOnlyList<DbcSyntaxToken> Lex(string rawText)
    {
        var tokens = new List<DbcSyntaxToken>();
        var index = 0;
        while (index < rawText.Length)
        {
            var current = rawText[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '/' && index + 1 < rawText.Length && rawText[index + 1] == '/')
            {
                while (index < rawText.Length && rawText[index] != '\n')
                {
                    index++;
                }

                continue;
            }

            if (current == '(' && index + 1 < rawText.Length && rawText[index + 1] == '*')
            {
                index += 2;
                while (index + 1 < rawText.Length
                       && !(rawText[index] == '*' && rawText[index + 1] == ')'))
                {
                    index++;
                }

                index = Math.Min(index + 2, rawText.Length);
                continue;
            }

            var start = index;
            if (char.IsLetter(current) || current == '_')
            {
                index++;
                while (index < rawText.Length
                       && (char.IsLetterOrDigit(rawText[index]) || rawText[index] == '_'))
                {
                    index++;
                }

                tokens.Add(new DbcSyntaxToken(DbcTokenKind.Identifier, start, index - start));
                continue;
            }

            if (char.IsDigit(current)
                || ((current == '-' || current == '+')
                    && index + 1 < rawText.Length
                    && char.IsDigit(rawText[index + 1])))
            {
                index++;
                while (index < rawText.Length)
                {
                    var next = rawText[index];
                    if (char.IsDigit(next) || next is '.' or 'e' or 'E')
                    {
                        index++;
                        continue;
                    }

                    if ((next == '-' || next == '+')
                        && index > start
                        && rawText[index - 1] is 'e' or 'E')
                    {
                        index++;
                        continue;
                    }

                    break;
                }

                tokens.Add(new DbcSyntaxToken(DbcTokenKind.Number, start, index - start));
                continue;
            }

            if (current == '"')
            {
                index++;
                while (index < rawText.Length)
                {
                    if (rawText[index] == '\\' && index + 1 < rawText.Length)
                    {
                        index += 2;
                        continue;
                    }

                    if (rawText[index] == '"')
                    {
                        index++;
                        break;
                    }

                    index++;
                }

                tokens.Add(new DbcSyntaxToken(DbcTokenKind.String, start, index - start));
                continue;
            }

            index++;
            tokens.Add(new DbcSyntaxToken(DbcTokenKind.Symbol, start, 1));
        }

        return tokens;
    }

    internal static bool TryParseMessage(string rawText, out DbcMessageInfo? message)
    {
        message = null;
        var raw = rawText.AsSpan();
        var index = 0;
        SkipWhitespace(raw, ref index);
        if (!raw[index..].StartsWith("BO_"))
        {
            return false;
        }

        index += 3;
        SkipWhitespace(raw, ref index);
        var idStart = index;
        while (index < raw.Length && char.IsDigit(raw[index]))
        {
            index++;
        }

        if (index == idStart
            || !uint.TryParse(
                raw[idStart..index],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var id))
        {
            return false;
        }

        SkipWhitespace(raw, ref index);
        var nameStart = index;
        while (index < raw.Length
               && !char.IsWhiteSpace(raw[index])
               && raw[index] != ':')
        {
            index++;
        }

        if (index == nameStart)
        {
            return false;
        }

        var name = raw[nameStart..index].ToString();
        SkipWhitespace(raw, ref index);
        if (index >= raw.Length || raw[index] != ':')
        {
            return false;
        }

        index++;
        SkipWhitespace(raw, ref index);
        var lengthStart = index;
        while (index < raw.Length && char.IsDigit(raw[index]))
        {
            index++;
        }

        if (index == lengthStart
            || !byte.TryParse(
                raw[lengthStart..index],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var length))
        {
            return false;
        }

        SkipWhitespace(raw, ref index);
        var transmitter = index < raw.Length
            ? raw[index..].TrimEnd().ToString()
            : string.Empty;
        message = new DbcMessageInfo(id, name, length, transmitter);
        return true;
    }

    internal static bool TryParseSignal(string rawText, out DbcSignalInfo? signal)
    {
        signal = null;
        var raw = rawText.AsSpan();
        var index = 0;
        SkipWhitespace(raw, ref index);
        if (!raw[index..].StartsWith("SG_"))
        {
            return false;
        }

        index += 3;
        SkipWhitespace(raw, ref index);
        var nameStart = index;
        while (index < raw.Length
               && !char.IsWhiteSpace(raw[index])
               && raw[index] != ':')
        {
            index++;
        }

        if (index == nameStart)
        {
            return false;
        }

        var name = raw[nameStart..index].ToString();
        SkipWhitespace(raw, ref index);
        if (index < raw.Length && raw[index] != ':')
        {
            while (index < raw.Length
                   && !char.IsWhiteSpace(raw[index])
                   && raw[index] != ':')
            {
                index++;
            }

            SkipWhitespace(raw, ref index);
        }

        if (index >= raw.Length || raw[index] != ':')
        {
            return false;
        }

        index++;
        SkipWhitespace(raw, ref index);
        var startStart = index;
        while (index < raw.Length && char.IsDigit(raw[index]))
        {
            index++;
        }

        if (index == startStart
            || !int.TryParse(
                raw[startStart..index],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var startBit))
        {
            return false;
        }

        if (index >= raw.Length || raw[index] != '|')
        {
            return false;
        }

        index++;
        var lengthStart = index;
        while (index < raw.Length && char.IsDigit(raw[index]))
        {
            index++;
        }

        if (index == lengthStart
            || !int.TryParse(
                raw[lengthStart..index],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var length))
        {
            return false;
        }

        if (index >= raw.Length || raw[index] != '@')
        {
            return false;
        }

        index++;
        if (index >= raw.Length || raw[index] is not ('0' or '1'))
        {
            return false;
        }

        var isBigEndian = raw[index] == '1';
        index++;
        if (index >= raw.Length || raw[index] is not ('+' or '-'))
        {
            return false;
        }

        signal = new DbcSignalInfo(
            name,
            startBit,
            length,
            isBigEndian,
            raw[index] == '-');
        return true;
    }

    internal static bool TryParseComment(string rawText, out string? comment)
    {
        comment = null;
        var raw = rawText.AsSpan();
        var quote = raw.IndexOf('"');
        if (quote < 0)
        {
            return false;
        }

        var start = quote + 1;
        var index = start;
        while (index < raw.Length)
        {
            if (raw[index] == '\\' && index + 1 < raw.Length)
            {
                index += 2;
                continue;
            }

            if (raw[index] == '"')
            {
                comment = UnescapeComment(raw[start..index].ToString());
                return true;
            }

            index++;
        }

        comment = UnescapeComment(raw[start..].ToString());
        return true;
    }

    private static string UnescapeComment(string rawText)
    {
        if (!rawText.Contains('\\', StringComparison.Ordinal))
        {
            return rawText;
        }

        var builder = new StringBuilder(rawText.Length);
        for (var index = 0; index < rawText.Length; index++)
        {
            if (rawText[index] == '\\' && index + 1 < rawText.Length)
            {
                builder.Append(rawText[++index]);
                continue;
            }

            builder.Append(rawText[index]);
        }

        return builder.ToString();
    }

    private DbcRecordHandle FindFirstRecord(int changeStart, int changeEnd)
    {
        for (var blockIndex = 0; blockIndex < _blocks.Length; blockIndex++)
        {
            var block = _blocks[blockIndex];
            var blockEnd = block.Start + block.Length;
            if (changeStart < blockEnd
                || (changeStart == blockEnd && blockIndex == _blocks.Length - 1))
            {
                for (var local = 0; local < block.Records.Length; local++)
                {
                    var record = block.Records[local];
                    var end = block.Start + record.RelativeStart + record.Length;
                    if (changeStart < end
                        || (changeStart == end
                            && blockIndex == _blocks.Length - 1
                            && local == block.Records.Length - 1))
                    {
                        return new DbcRecordHandle(
                            block,
                            blockIndex,
                            local,
                            record,
                            block.Start + record.RelativeStart);
                    }
                }
            }
        }

        var lastBlock = _blocks[^1];
        var lastLocal = lastBlock.Records.Length - 1;
        var lastRecord = lastBlock.Records[lastLocal];
        return new DbcRecordHandle(
            lastBlock,
            _blocks.Length - 1,
            lastLocal,
            lastRecord,
            lastBlock.Start + lastRecord.RelativeStart);
    }

    private DbcRecordHandle FindLastRecord(int changeEnd, DbcRecordHandle first)
    {
        for (var blockIndex = first.BlockIndex; blockIndex < _blocks.Length; blockIndex++)
        {
            var block = _blocks[blockIndex];
            var blockEnd = block.Start + block.Length;
            if (changeEnd <= blockEnd || blockIndex == _blocks.Length - 1)
            {
                for (var local = 0; local < block.Records.Length; local++)
                {
                    var record = block.Records[local];
                    var end = block.Start + record.RelativeStart + record.Length;
                    if (changeEnd <= end
                        || (blockIndex == _blocks.Length - 1
                            && local == block.Records.Length - 1))
                    {
                        return new DbcRecordHandle(
                            block,
                            blockIndex,
                            local,
                            record,
                            block.Start + record.RelativeStart);
                    }
                }
            }
        }

        return first;
    }

    private int SumCounts(int fromInclusive, int toInclusive)
    {
        var count = 0;
        for (var index = fromInclusive; index <= toInclusive; index++)
        {
            count += _blocks[index].Records.Length;
        }

        return count;
    }

    private static DbcStatementBlock[] BuildBlocks(
        IReadOnlyList<DbcSpannedRecord> records,
        int firstIndex)
    {
        if (records.Count == 0)
        {
            return Array.Empty<DbcStatementBlock>();
        }

        var blocks = new List<DbcStatementBlock>(
            (records.Count + BlockSize - 1) / BlockSize);
        var statementIndex = firstIndex;
        for (var offset = 0; offset < records.Count; offset += BlockSize)
        {
            var count = Math.Min(BlockSize, records.Count - offset);
            var start = records[offset].Start;
            var blockRecords = new DbcStatementRecord[count];
            for (var local = 0; local < count; local++)
            {
                var record = records[offset + local];
                blockRecords[local] = new DbcStatementRecord
                {
                    Kind = record.Kind,
                    RelativeStart = record.Start - start,
                    Length = record.Length,
                    GreenId = record.GreenId,
                };
            }

            blocks.Add(new DbcStatementBlock(start, statementIndex, blockRecords));
            statementIndex += count;
        }

        return blocks.ToArray();
    }

    private static List<DbcSpannedRecord> Scan(string text, int baseOffset, long firstGreenId)
    {
        var records = new List<DbcSpannedRecord>();
        if (text.Length == 0)
        {
            return records;
        }

        var segmentStart = 0;
        var segmentKind = DbcStatementKind.Raw;
        var inString = false;
        var inBlockComment = false;
        var inNsBlock = false;
        var lineStart = 0;
        var lineStartInString = false;
        var lineStartInBlockComment = false;

        void Flush(int end)
        {
            if (end <= segmentStart)
            {
                return;
            }

            records.Add(new DbcSpannedRecord(
                segmentKind,
                baseOffset + segmentStart,
                end - segmentStart,
                firstGreenId + records.Count));
        }

        void ProcessLine(
            int start,
            int end,
            bool startsInString,
            bool startsInBlockComment)
        {
            var line = text.AsSpan(start, end - start);
            var trimmed = line.TrimStart();
            var indented = trimmed.Length != line.Length;
            var firstWord = FirstWord(trimmed);

            var startsNew = false;
            if (!startsInString && !startsInBlockComment)
            {
                if (inNsBlock && !indented)
                {
                    if (IsHeaderTerminator(firstWord))
                    {
                        inNsBlock = firstWord.SequenceEqual("NS_");
                    }
                    else if (trimmed.Length > 0)
                    {
                        inNsBlock = false;
                    }
                }

                startsNew = (!inNsBlock || !indented) && KeywordLookup.Contains(firstWord);
                if (!startsNew
                    && !inNsBlock
                    && !indented
                    && firstWord.Length > 0
                    && firstWord.Length < trimmed.Length
                    && char.IsWhiteSpace(trimmed[firstWord.Length])
                    && (char.IsLetter(firstWord[0]) || firstWord[0] == '_')
                    && trimmed.IndexOf(';') >= 0)
                {
                    startsNew = true;
                }

                if (startsNew && firstWord.SequenceEqual("NS_"))
                {
                    inNsBlock = true;
                }
            }

            if (!startsNew)
            {
                return;
            }

            if (start > segmentStart)
            {
                Flush(start);
                segmentStart = start;
            }

            segmentKind = KindFromFirstWord(firstWord);
        }

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (inString)
            {
                if (current == '\\')
                {
                    index++;
                    continue;
                }

                if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*'
                    && index + 1 < text.Length
                    && text[index + 1] == ')')
                {
                    inBlockComment = false;
                    index++;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                var newline = text.IndexOf('\n', index + 2);
                if (newline < 0)
                {
                    index = text.Length;
                    break;
                }

                index = newline - 1;
                continue;
            }

            if (current == '(' && index + 1 < text.Length && text[index + 1] == '*')
            {
                inBlockComment = true;
                index++;
                continue;
            }

            if (current != '\n')
            {
                continue;
            }

            ProcessLine(
                lineStart,
                index + 1,
                lineStartInString,
                lineStartInBlockComment);
            lineStart = index + 1;
            lineStartInString = inString;
            lineStartInBlockComment = inBlockComment;
        }

        if (lineStart < text.Length
            && !lineStartInString
            && !lineStartInBlockComment)
        {
            ProcessLine(
                lineStart,
                text.Length,
                lineStartInString,
                lineStartInBlockComment);
        }

        Flush(text.Length);
        return records;
    }

    private static DbcStatementKind KindFromFirstWord(ReadOnlySpan<char> firstWord)
    {
        if (firstWord.SequenceEqual("BO_"))
        {
            return DbcStatementKind.Message;
        }

        if (firstWord.SequenceEqual("SG_"))
        {
            return DbcStatementKind.Signal;
        }

        if (firstWord.SequenceEqual("CM_"))
        {
            return DbcStatementKind.Comment;
        }

        return DbcStatementKind.Raw;
    }

    private static void SkipWhitespace(ReadOnlySpan<char> raw, ref int index)
    {
        while (index < raw.Length && char.IsWhiteSpace(raw[index]))
        {
            index++;
        }
    }

    private static ReadOnlySpan<char> FirstWord(ReadOnlySpan<char> line)
    {
        var index = 0;
        while (index < line.Length
               && (char.IsLetterOrDigit(line[index]) || line[index] == '_'))
        {
            index++;
        }

        return line[..index];
    }

    private static bool IsHeaderTerminator(ReadOnlySpan<char> word)
    {
        return word.SequenceEqual("BS_")
            || word.SequenceEqual("BU_")
            || word.SequenceEqual("BO_")
            || word.SequenceEqual("VERSION")
            || word.SequenceEqual("NS_");
    }

}

internal sealed class DbcTextBuffer
{
    private readonly List<DbcTextPiece> _pieces;

    private DbcTextBuffer(List<DbcTextPiece> pieces)
    {
        _pieces = pieces;
        var length = 0;
        foreach (var piece in pieces)
        {
            length += piece.Length;
        }

        Length = length;
    }

    public int Length { get; }

    public static DbcTextBuffer FromText(string text)
    {
        return new DbcTextBuffer(
            text.Length == 0
                ? new List<DbcTextPiece>()
                : new List<DbcTextPiece> { new(text, 0, text.Length) });
    }

    public string GetText()
    {
        if (_pieces.Count == 0)
        {
            return string.Empty;
        }

        if (_pieces.Count == 1
            && _pieces[0].Start == 0
            && _pieces[0].Length == _pieces[0].Text.Length)
        {
            return _pieces[0].Text;
        }

        var builder = new StringBuilder(Length);
        foreach (var piece in _pieces)
        {
            builder.Append(piece.Text.AsSpan(piece.Start, piece.Length));
        }

        return builder.ToString();
    }

    public string Slice(int start, int length)
    {
        if (length == 0)
        {
            return string.Empty;
        }

        var position = 0;
        foreach (var piece in _pieces)
        {
            var pieceEnd = position + piece.Length;
            if (start >= position && start + length <= pieceEnd)
            {
                return piece.Text.Substring(
                    piece.Start + (start - position),
                    length);
            }

            position = pieceEnd;
        }

        var builder = new StringBuilder(length);
        position = 0;
        var remainingStart = start;
        var remainingLength = length;
        foreach (var piece in _pieces)
        {
            var pieceEnd = position + piece.Length;
            if (remainingLength <= 0)
            {
                break;
            }

            if (pieceEnd <= remainingStart)
            {
                position = pieceEnd;
                continue;
            }

            var localStart = Math.Max(remainingStart, position);
            var take = Math.Min(pieceEnd, remainingStart + remainingLength) - localStart;
            if (take > 0)
            {
                builder.Append(piece.Text.AsSpan(
                    piece.Start + (localStart - position),
                    take));
                remainingStart = localStart + take;
                remainingLength -= take;
            }

            position = pieceEnd;
        }

        return builder.ToString();
    }

    public DbcTextBuffer Replace(int start, int length, string newText)
    {
        var pieces = new List<DbcTextPiece>(_pieces.Count + 2);
        var inserted = false;

        void InsertNew()
        {
            if (newText.Length > 0)
            {
                pieces.Add(new DbcTextPiece(newText, 0, newText.Length));
            }

            inserted = true;
        }

        var position = 0;
        foreach (var piece in _pieces)
        {
            var pieceEnd = position + piece.Length;
            if (pieceEnd <= start)
            {
                pieces.Add(piece);
                position = pieceEnd;
                continue;
            }

            if (position >= start + length)
            {
                if (!inserted)
                {
                    InsertNew();
                }

                pieces.Add(piece);
                position = pieceEnd;
                continue;
            }

            var keepPrefixEnd = Math.Min(start, pieceEnd);
            if (position < keepPrefixEnd)
            {
                pieces.Add(new DbcTextPiece(
                    piece.Text,
                    piece.Start,
                    keepPrefixEnd - position));
            }

            if (!inserted)
            {
                InsertNew();
            }

            var keepSuffixStart = Math.Max(start + length, position);
            if (keepSuffixStart < pieceEnd)
            {
                pieces.Add(new DbcTextPiece(
                    piece.Text,
                    piece.Start + (keepSuffixStart - position),
                    pieceEnd - keepSuffixStart));
            }

            position = pieceEnd;
        }

        if (!inserted)
        {
            InsertNew();
        }

        return new DbcTextBuffer(pieces);
    }
}

internal readonly record struct DbcTextPiece(string Text, int Start, int Length);

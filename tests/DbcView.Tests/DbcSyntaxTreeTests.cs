#nullable enable
using System.Diagnostics;
using DbcView.Infrastructure.Syntax;
using DbcView.Web.Services;
using Xunit;
using Xunit.Abstractions;

namespace DbcView.Tests;

public sealed class DbcSyntaxTreeTests
{
    private const string Sample =
        "VERSION \"\"\n" +
        "BU_: XXX\n" +
        "\n" +
        "BO_ 100 First: 2 XXX\n" +
        " SG_ A : 0|8@0+ (1,0) [0|255] \"\" XXX\n" +
        "CM_ SG_ 100 A \"one; two\";\n" +
        "CM_ \"file comment\"\n";

    [Fact]
    public void Parse_preserves_text_and_types_core_statements()
    {
        var tree = DbcSyntaxTree.Parse(Sample);

        Assert.Equal(Sample, tree.GetText());
        Assert.Equal(6, tree.Statements.Count);
        Assert.Equal(DbcStatementKind.Raw, tree.Statements[0].Kind);
        Assert.Equal(DbcStatementKind.Raw, tree.Statements[1].Kind);
        Assert.Equal(DbcStatementKind.Message, tree.Statements[2].Kind);
        Assert.Equal(DbcStatementKind.Signal, tree.Statements[3].Kind);
        Assert.Equal(DbcStatementKind.Comment, tree.Statements[4].Kind);
        Assert.Equal(DbcStatementKind.Comment, tree.Statements[5].Kind);

        var message = tree.Statements[2].Message;
        Assert.NotNull(message);
        Assert.Equal(100u, message!.Id);
        Assert.Equal("First", message.Name);
        Assert.Equal(2, message.Length);
        Assert.Equal("XXX", message.Transmitter);

        var signal = tree.Statements[3].Signal;
        Assert.NotNull(signal);
        Assert.Equal("A", signal!.Name);
        Assert.Equal(0, signal.StartBit);
        Assert.Equal(8, signal.Length);
        Assert.False(signal.IsBigEndian);
        Assert.False(signal.IsSigned);

        Assert.Equal("one; two", tree.Statements[4].Comment);
        Assert.Equal("file comment", tree.Statements[5].Comment);
        Assert.All(tree.Statements, statement => Assert.NotEmpty(statement.Tokens));
    }

    [Fact]
    public void Parse_handles_multiline_strings_and_missing_semicolons()
    {
        const string text =
            "VERSION \"\"\n" +
            "BU_: XXX\n" +
            "CM_ \"line1\n" +
            "line2\";\n" +
            "CM_ \"no semicolon\"\n" +
            "CM_ \"next\";\n";

        var tree = DbcSyntaxTree.Parse(text);

        Assert.Equal(text, tree.GetText());
        Assert.Equal(5, tree.Statements.Count);
        Assert.Equal("line1\nline2", tree.Statements[2].Comment);
        Assert.Equal("no semicolon", tree.Statements[3].Comment);
        Assert.Equal("next", tree.Statements[4].Comment);
        Assert.Equal(DbcStatementKind.Comment, tree.Statements[2].Kind);
        Assert.Equal(DbcStatementKind.Comment, tree.Statements[3].Kind);
        Assert.Equal(DbcStatementKind.Comment, tree.Statements[4].Kind);
    }

    [Fact]
    public void Parse_does_not_split_keyword_entries_inside_the_ns_block()
    {
        const string text =
            "VERSION \"\"\n" +
            "NS_ :\n" +
            "    CM_\n" +
            "    BA_DEF_\n" +
            "BS_:\n" +
            "BU_: XXX\n" +
            "BO_ 100 M: 1 XXX\n";

        var tree = DbcSyntaxTree.Parse(text);

        Assert.Equal(text, tree.GetText());
        Assert.Equal(5, tree.Statements.Count);
        Assert.DoesNotContain(
            tree.Statements,
            statement => statement.Kind == DbcStatementKind.Comment);
        Assert.Contains("CM_", tree.Statements[1].RawText, StringComparison.Ordinal);
        Assert.Equal(DbcStatementKind.Message, tree.Statements[4].Kind);
    }

    [Fact]
    public void WithChanges_reuses_unchanged_statements_and_preserves_text()
    {
        const string text =
            "CM_ \"zero\";\n" +
            "CM_ \"old\";\n" +
            "CM_ \"two\";\n" +
            "CM_ \"three\";\n";
        var tree = DbcSyntaxTree.Parse(text);
        var offset = tree.Statements[0].FullWidth + tree.Statements[1].RawText.IndexOf("old", StringComparison.Ordinal);
        var expected = text.Remove(offset, 3).Insert(offset, "NEW");

        var updated = tree.WithChanges(new DbcTextChange(new DbcTextSpan(offset, 3), "NEW"));

        Assert.Equal(expected, updated.GetText());
        Assert.Equal(4, updated.Statements.Count);
        Assert.Equal(tree.GetGreenId(0), updated.GetGreenId(0));
        Assert.NotEqual(tree.GetGreenId(1), updated.GetGreenId(1));
        Assert.Equal(tree.GetGreenId(2), updated.GetGreenId(2));
        Assert.Equal(tree.GetGreenId(3), updated.GetGreenId(3));
        Assert.Equal("NEW", updated.Statements[1].Comment);
    }

    [Fact]
    public void WithChanges_updates_typed_message_fields()
    {
        const string text = "VERSION \"\"\nBU_: XXX\nBO_ 100 First: 2 XXX\n";
        var tree = DbcSyntaxTree.Parse(text);
        var offset = tree.Statements[0].FullWidth
            + tree.Statements[1].FullWidth
            + tree.Statements[2].RawText.IndexOf("First", StringComparison.Ordinal);
        var expected = text.Remove(offset, 5).Insert(offset, "Other");

        var updated = tree.WithChanges(new DbcTextChange(new DbcTextSpan(offset, 5), "Other"));

        Assert.Equal(expected, updated.GetText());
        Assert.Equal(DbcStatementKind.Message, updated.Statements[2].Kind);
        Assert.Equal("Other", updated.Statements[2].Message!.Name);
        Assert.Equal(tree.GetGreenId(0), updated.GetGreenId(0));
        Assert.Equal(tree.GetGreenId(1), updated.GetGreenId(1));
        Assert.NotEqual(tree.GetGreenId(2), updated.GetGreenId(2));
    }

    [Fact]
    public void Red_tree_positions_follow_green_widths()
    {
        var tree = DbcSyntaxTree.Parse(Sample);
        var root = tree.Root;

        var expected = 0;
        foreach (var statement in root.Statements)
        {
            Assert.Equal(expected, statement.Position);
            Assert.Equal(statement.GreenStatement.FullWidth, statement.FullSpan.Length);
            foreach (var token in statement.Tokens)
            {
                Assert.InRange(
                    token.Span.Start,
                    statement.Position,
                    statement.Position + statement.FullWidth);
                Assert.InRange(
                    token.Span.End,
                    statement.Position,
                    statement.Position + statement.FullWidth);
            }

            expected += statement.FullWidth;
        }

        Assert.Equal(Sample.Length, expected);
        Assert.Equal(Sample.Length, root.FullSpan.Length);
    }

    [Fact]
    public void WithChanges_appends_a_new_statement_at_end_of_file()
    {
        const string text = "VERSION \"\"\nBU_: XXX\nCM_ \"first\";\n";
        var tree = DbcSyntaxTree.Parse(text);
        var addition = "CM_ \"second\";\n";
        var expected = text + addition;

        var updated = tree.WithChanges(
            new DbcTextChange(new DbcTextSpan(text.Length, 0), addition));

        Assert.Equal(expected, updated.GetText());
        Assert.Equal(4, updated.Statements.Count);
        Assert.Equal("first", updated.Statements[2].Comment);
        Assert.Equal("second", updated.Statements[3].Comment);
        Assert.Equal(tree.GetGreenId(0), updated.GetGreenId(0));
        Assert.Equal(tree.GetGreenId(1), updated.GetGreenId(1));
    }
}

public sealed class DbcSyntaxTreeSpikeTests
{
    private readonly ITestOutputHelper _output;

    public DbcSyntaxTreeSpikeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Probe_parse_and_single_statement_incremental_on_four_megabyte_envelope()
    {
        var content = DbcSyntheticGenerator.Create();
        var parseWatch = Stopwatch.StartNew();
        var tree = DbcSyntaxTree.Parse(content);
        parseWatch.Stop();

        var statementCount = tree.StatementCount;
        var commentIndex = tree.FindFirstKind(DbcStatementKind.Comment);
        Assert.True(commentIndex >= 0);
        var comment = tree.GetStatement(commentIndex);
        var quote = comment.RawText.IndexOf('"');
        var editOffset = comment.Start + quote + 1;
        var expected = content.Insert(editOffset, "X");
        var editWatch = Stopwatch.StartNew();
        var updated = tree.WithChanges(
            new DbcTextChange(new DbcTextSpan(editOffset, 0), "X"));
        editWatch.Stop();

        var reused = 0;
        for (var index = 0; index < statementCount; index++)
        {
            if (tree.GetGreenId(index) == updated.GetGreenId(index))
            {
                reused++;
            }
        }

        var managedGreen = GC.GetTotalMemory(false) / 1024d / 1024d;
        var redWatch = Stopwatch.StartNew();
        var redCount = updated.Root.Statements.Count;
        var firstRed = updated.Root.Statements[0];
        redWatch.Stop();
        var managedRed = GC.GetTotalMemory(false) / 1024d / 1024d;

        _output.WriteLine(
            $"bytes={content.Length} statements={statementCount} " +
            $"parse={parseWatch.ElapsedMilliseconds}ms edit={editWatch.ElapsedMilliseconds}ms " +
            $"red-view={redWatch.ElapsedMilliseconds}ms reused={reused} " +
            $"redStatements={redCount} redFirst={firstRed.Position} " +
            $"managed-green={managedGreen:0.0}MB managed-red={managedRed:0.0}MB");

        Assert.Equal(expected, updated.GetText());
        Assert.Equal(statementCount, updated.StatementCount);
        Assert.True(reused >= statementCount - 1);
    }
}

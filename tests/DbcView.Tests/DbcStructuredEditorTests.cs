#nullable enable
using DbcView.Application;
using DbcView.Domain;
using DbcView.Infrastructure;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcStructuredEditorTests
{
    private const string Content =
        "VERSION \"\"\nBU_: XXX\n\n" +
        "BO_ 100 Engine: 2 XXX\n" +
        " SG_ S : 0|8@0+ (1,0) [0|255] \"\" XXX\n\n" +
        "CM_ \"file note\";\n" +
        "CM_ BO_ 100 \"message note\";\n" +
        "CM_ SG_ 100 S \"signal note\";\n" +
        "BA_ \"Cycle\" BO_ 100 50;\n" +
        "BA_ \"Cycle\" BO_ 100 60;\n" +
        "VAL_ 100 S VAL_TABLE_ State;\n" +
        "VAL_TABLE_ State 0 \"Off\" 1 \"On\";\n";

    [Fact]
    public void Editing_a_comment_replaces_only_that_statement()
    {
        var file = Parse(Content);
        var build = DbcStructuredEditor.Build(
            file,
            new DbcStructuredEdit(
                DbcStructuredEditKind.Comment,
                DbcSemanticScope.Message,
                MessageId: 100,
                ExistingLine: 8,
                CommentText: "new message note"));

        Assert.True(build.Success, build.Error);
        Assert.Equal("modified", build.Diff?.Change);
        Assert.Equal(8, build.Diff?.LineNumber);
        Assert.Contains("CM_ BO_ 100 \"new message note\";", build.Content, StringComparison.Ordinal);
        Assert.Contains("CM_ SG_ 100 S \"signal note\";", build.Content, StringComparison.Ordinal);
        Assert.Contains("BA_ \"Cycle\" BO_ 100 50;", build.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("CM_ BO_ 100 \"message note\";", build.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_a_comment_uses_the_same_type_anchor()
    {
        var file = Parse(Content);
        var build = DbcStructuredEditor.Build(
            file,
            new DbcStructuredEdit(
                DbcStructuredEditKind.Comment,
                DbcSemanticScope.Message,
                MessageId: 100,
                CommentText: "added"));

        Assert.True(build.Success, build.Error);
        Assert.Equal("added", build.Diff?.Change);
        var lines = build.Content.Split('\n').Select(line => line.Trim()).ToArray();
        var oldIndex = Array.IndexOf(lines, "CM_ BO_ 100 \"message note\";");
        var newIndex = Array.IndexOf(lines, "CM_ BO_ 100 \"added\";");
        Assert.True(newIndex > oldIndex);
        Assert.True(newIndex < Array.IndexOf(lines, "CM_ SG_ 100 S \"signal note\";"));
    }

    [Fact]
    public void Deleting_a_comment_removes_one_newline_and_keeps_other_content()
    {
        var file = Parse(Content);
        var build = DbcStructuredEditor.Build(
            file,
            new DbcStructuredEdit(
                DbcStructuredEditKind.Comment,
                DbcSemanticScope.File,
                ExistingLine: 7,
                Delete: true));

        Assert.True(build.Success, build.Error);
        Assert.Equal("deleted", build.Diff?.Change);
        Assert.DoesNotContain("CM_ \"file note\";", build.Content, StringComparison.Ordinal);
        Assert.Contains("CM_ BO_ 100 \"message note\";", build.Content, StringComparison.Ordinal);
        Assert.Contains("BO_ 100 Engine: 2 XXX", build.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Editing_the_effective_duplicate_attribute_leaves_the_earlier_one()
    {
        var file = Parse(Content);
        var build = DbcStructuredEditor.Build(
            file,
            new DbcStructuredEdit(
                DbcStructuredEditKind.Attribute,
                DbcSemanticScope.Message,
                MessageId: 100,
                ExistingLine: 11,
                AttributeName: "Cycle",
                AttributeValue: "70",
                AttributeValueType: "INT"));

        Assert.True(build.Success, build.Error);
        Assert.Contains("BA_ \"Cycle\" BO_ 100 50;", build.Content, StringComparison.Ordinal);
        Assert.Contains("BA_ \"Cycle\" BO_ 100 70;", build.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("BA_ \"Cycle\" BO_ 100 60;", build.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_a_string_attribute_quotes_and_escapes_the_value()
    {
        var file = Parse(Content);
        var build = DbcStructuredEditor.Build(
            file,
            new DbcStructuredEdit(
                DbcStructuredEditKind.Attribute,
                DbcSemanticScope.Message,
                MessageId: 100,
                AttributeName: "Label",
                AttributeValue: "a \"quoted\" value",
                AttributeValueType: "STRING"));

        Assert.True(build.Success, build.Error);
        Assert.Contains(
            "BA_ \"Label\" BO_ 100 \"a \\\"quoted\\\" value\";",
            build.Content,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Editing_a_referenced_value_table_creates_an_inline_copy()
    {
        var file = Parse(Content);
        var build = DbcStructuredEditor.Build(
            file,
            new DbcStructuredEdit(
                DbcStructuredEditKind.ValueTable,
                DbcSemanticScope.Signal,
                MessageId: 100,
                SignalName: "S",
                ExistingLine: 12,
                ValueEntries: new[]
                {
                    new DbcValueDescription("0", "Off"),
                    new DbcValueDescription("1", "On"),
                }));

        Assert.True(build.Success, build.Error);
        Assert.Contains("VAL_ 100 S 0 \"Off\" 1 \"On\";", build.Content, StringComparison.Ordinal);
        Assert.Contains("VAL_TABLE_ State 0 \"Off\" 1 \"On\";", build.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("VAL_ 100 S VAL_TABLE_ State;", build.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_a_value_table_appends_after_the_last_val_statement()
    {
        var file = Parse(Content);
        var build = DbcStructuredEditor.Build(
            file,
            new DbcStructuredEdit(
                DbcStructuredEditKind.ValueTable,
                DbcSemanticScope.Signal,
                MessageId: 100,
                SignalName: "S",
                ValueEntries: new[]
                {
                    new DbcValueDescription("2", "Two"),
                }));

        Assert.True(build.Success, build.Error);
        Assert.Equal("added", build.Diff?.Change);
        Assert.Contains("VAL_ 100 S 2 \"Two\";", build.Content, StringComparison.Ordinal);
        Assert.Contains("VAL_ 100 S VAL_TABLE_ State;", build.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Edits_preserve_crlf_line_endings()
    {
        var content = Content.Replace("\n", "\r\n", StringComparison.Ordinal);
        var file = Parse(content);
        var build = DbcStructuredEditor.Build(
            file,
            new DbcStructuredEdit(
                DbcStructuredEditKind.Comment,
                DbcSemanticScope.File,
                ExistingLine: 7,
                CommentText: "crlf note"));

        Assert.True(build.Success, build.Error);
        Assert.Contains("CM_ \"crlf note\";\r\n", build.Content, StringComparison.Ordinal);
        Assert.False(
            build.Content.Replace("\r\n", string.Empty, StringComparison.Ordinal)
                .Contains('\n'));
    }

    [Fact]
    public void Unknown_statement_line_fails_without_changing_content()
    {
        var file = Parse(Content);
        var build = DbcStructuredEditor.Build(
            file,
            new DbcStructuredEdit(
                DbcStructuredEditKind.Comment,
                DbcSemanticScope.File,
                ExistingLine: 999,
                CommentText: "missing"));

        Assert.False(build.Success);
        Assert.Equal(string.Empty, build.Content);
        Assert.Contains("not found", build.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_applies_the_edit_and_reparses()
    {
        using var workspace = TestWorkspace.Create();
        var entry = workspace.AddFile("can-a", "edit.dbc", Content);

        var outcome = workspace.ApplyStructuredEdit(
            entry.Id,
            new DbcStructuredEdit(
                DbcStructuredEditKind.Comment,
                DbcSemanticScope.Message,
                MessageId: 100,
                ExistingLine: 8,
                CommentText: "workspace edit"));

        Assert.True(outcome.Success, outcome.Error);
        Assert.NotNull(outcome.ParseResult);
        var updated = workspace.Find(entry.Id);
        Assert.NotNull(updated);
        Assert.Contains("CM_ BO_ 100 \"workspace edit\";", updated!.Content, StringComparison.Ordinal);
        Assert.True(updated.ParseResult.IsValid);
    }

    private static DbcFileEntry Parse(string content)
    {
        var parse = DbcParser.Parse(content);
        Assert.True(parse.IsValid, string.Join(Environment.NewLine, parse.Diagnostics));
        return new DbcFileEntry(new DbcDocumentId("can-a", "edit.dbc"), content, parse);
    }
}

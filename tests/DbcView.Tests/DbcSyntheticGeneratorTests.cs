#nullable enable
using DbcView.Infrastructure;
using DbcView.Web.Services;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcSyntheticGeneratorTests
{
    [Fact]
    public void Create_builds_a_parseable_envelope_with_semantics()
    {
        var content = DbcSyntheticGenerator.Create(
            messageCount: 100,
            signalsPerMessage: 2,
            semanticCount: 50,
            targetBytes: 0);

        var result = DbcParser.Parse(content);

        Assert.True(result.IsValid);
        Assert.Equal(100, result.Document.Messages.Count);
        Assert.Equal(200, result.Document.Messages.Sum(message => message.Signals.Count));
        Assert.Contains("CM_ SG_ ", content, StringComparison.Ordinal);
        Assert.Contains("BA_ \"GenMsgCycleTime\"", content, StringComparison.Ordinal);
        Assert.Contains("VAL_ ", content, StringComparison.Ordinal);
        Assert.True(content.Length < 4 * 1024 * 1024);
    }

    [Fact]
    public void Create_pads_to_the_requested_target_and_stays_parseable()
    {
        const int target = 20_000;
        var content = DbcSyntheticGenerator.Create(
            messageCount: 10,
            signalsPerMessage: 2,
            semanticCount: 0,
            targetBytes: target);

        var result = DbcParser.Parse(content);

        Assert.True(result.IsValid);
        Assert.True(content.Length >= target);
        Assert.Contains("padding comment", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_supports_messages_without_signals()
    {
        var content = DbcSyntheticGenerator.Create(
            messageCount: 3,
            signalsPerMessage: 0,
            semanticCount: 0,
            targetBytes: 0);

        var result = DbcParser.Parse(content);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.Document.Messages.Count);
        Assert.All(result.Document.Messages, message => Assert.Empty(message.Signals));
    }

    [Fact]
    public void Create_rejects_an_empty_message_set()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DbcSyntheticGenerator.Create(messageCount: 0));
    }
}

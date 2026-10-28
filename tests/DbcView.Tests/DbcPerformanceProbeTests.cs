#nullable enable
using System.Diagnostics;
using System.Text;
using DbcView.Application;
using DbcView.Domain;
using DbcView.Infrastructure;
using DbcView.Web.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace DbcView.Tests;

public sealed class DbcPerformanceProbeTests
{
    private readonly ITestOutputHelper _output;

    public DbcPerformanceProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Probe_expand_all_on_large_envelope()
    {
        var content = CreateEnvelope(messageCount: 28000, semanticCount: 0);
        var parseWatch = Stopwatch.StartNew();
        var result = DbcParser.Parse(content);
        parseWatch.Stop();

        using var workspace = TestWorkspace.Create();
        var entry = new DbcFileEntry(
            new DbcDocumentId("perf", "envelope.dbc"),
            content,
            result);
        var model = new DbcEditorModel(workspace);
        model.Open(entry);

        var expandWatch = Stopwatch.StartNew();
        model.ExpandAll();
        expandWatch.Stop();

        _output.WriteLine(
            $"bytes={content.Length} messages={model.MessageCount} signals={model.SignalCount} " +
            $"rows={model.RowCount} parse={parseWatch.ElapsedMilliseconds}ms " +
            $"expand={expandWatch.ElapsedMilliseconds}ms");

        Assert.False(result.HasErrors);
        Assert.Equal(28000, model.MessageCount);
        Assert.Equal(56000, model.SignalCount);
        Assert.Equal(84000, model.RowCount);
    }

    private static string CreateEnvelope(int messageCount, int semanticCount)
    {
        var builder = new StringBuilder(messageCount * 160);
        builder.AppendLine("VERSION \"\"");
        builder.AppendLine("BU_: ECU1 ECU2");
        builder.AppendLine();

        for (var index = 0; index < messageCount; index++)
        {
            var id = 0x100 + index;
            builder.Append("BO_ ").Append(id).Append(" Msg_").Append(index).AppendLine(": 8 ECU1");
            builder.Append(" SG_ Sig_").Append(index).AppendLine("_A : 0|8@0+ (1,0) [0|255] \"\" ECU2");
            builder.Append(" SG_ Sig_").Append(index).AppendLine("_B : 8|8@0+ (1,0) [0|255] \"\" ECU2");
        }

        for (var index = 0; index < semanticCount; index++)
        {
            var messageIndex = index % messageCount;
            var id = 0x100 + messageIndex;
            builder.Append("CM_ SG_ ").Append(id).Append(" Sig_").Append(messageIndex)
                .Append("_A \"Generated comment ").Append(index).AppendLine("\";");
        }

        return builder.ToString();
    }
}

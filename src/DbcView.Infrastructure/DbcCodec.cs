using DbcView.Application;
using DbcView.Domain;

namespace DbcView.Infrastructure;

/// <summary>Adapter that binds the hand-written parser to the application port.</summary>
public sealed class DbcCodec : IDbcCodec
{
    public DbcParseResult Parse(string content) => DbcParser.Parse(content);

    public string Serialize(DbcDocument document) => DbcParser.Serialize(document);
}

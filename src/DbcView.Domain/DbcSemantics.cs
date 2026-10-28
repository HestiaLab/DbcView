namespace DbcView.Domain;

public enum DbcSemanticScope
{
    File,
    Node,
    Message,
    Signal,
    EnvironmentVariable,
}

public sealed record DbcCommentEntry(
    DbcSemanticScope Scope,
    uint? MessageId,
    string? SignalName,
    string? TargetName,
    string Text,
    int LineNumber);

public sealed record DbcAttributeDefinition(
    string Name,
    DbcSemanticScope Scope,
    string ValueType,
    double? Minimum,
    double? Maximum,
    IReadOnlyList<string> EnumValues,
    int LineNumber);

public sealed record DbcAttributeDefault(
    string Name,
    string RawValue,
    int LineNumber);

public sealed record DbcAttributeValue(
    string Name,
    DbcSemanticScope Scope,
    uint? MessageId,
    string? SignalName,
    string? TargetName,
    string RawValue,
    int LineNumber);

public sealed record DbcValueDescription(
    string Value,
    string Description);

public sealed record DbcValueTableDefinition(
    string Name,
    IReadOnlyList<DbcValueDescription> Entries,
    int LineNumber);

public sealed record DbcSignalValueTable(
    uint MessageId,
    string SignalName,
    IReadOnlyList<DbcValueDescription> Entries,
    string? ReferencedTable,
    int LineNumber);

public abstract record DbcCommonStatement(
    string Keyword,
    string RawText,
    int LineNumber);

public sealed record DbcTransmitterStatement(
    uint MessageId,
    IReadOnlyList<string> Transmitters,
    string RawText,
    int LineNumber)
    : DbcCommonStatement("BO_TX_BU_", RawText, LineNumber);

public sealed record DbcSignalGroupStatement(
    uint MessageId,
    string GroupName,
    int? Repetitions,
    IReadOnlyList<string> SignalNames,
    string RawText,
    int LineNumber)
    : DbcCommonStatement("SIG_GROUP_", RawText, LineNumber);

public sealed record DbcSignalValueTypeStatement(
    uint MessageId,
    string SignalName,
    int? ValueType,
    string RawText,
    int LineNumber)
    : DbcCommonStatement("SIG_VALTYPE_", RawText, LineNumber);

public readonly record struct DbcMultiplexRange(uint Minimum, uint Maximum);

public sealed record DbcExtendedMultiplexStatement(
    uint MessageId,
    string SignalName,
    string MultiplexerSignalName,
    IReadOnlyList<DbcMultiplexRange> Ranges,
    string RawText,
    int LineNumber)
    : DbcCommonStatement("SG_MUL_VAL_", RawText, LineNumber);

public sealed record DbcEnvironmentVariableStatement(
    string Name,
    uint? ValueType,
    double? Minimum,
    double? Maximum,
    string Unit,
    string InitialValue,
    uint? Id,
    string AccessType,
    IReadOnlyList<string> AccessNodes,
    string RawText,
    int LineNumber)
    : DbcCommonStatement("EV_", RawText, LineNumber);

public sealed record DbcEnvironmentVariableDataStatement(
    string Keyword,
    string Name,
    uint? DataSize,
    string RawText,
    int LineNumber)
    : DbcCommonStatement(Keyword, RawText, LineNumber);

public sealed record DbcSignalTypeValueStatement(
    string SignalTypeName,
    int? ValueType,
    string RawText,
    int LineNumber)
    : DbcCommonStatement("SIGTYPE_VALTYPE_", RawText, LineNumber);

public sealed record DbcSignalTypeAttributeDefinition(
    string Name,
    string ValueType,
    double? Minimum,
    double? Maximum,
    IReadOnlyList<string> EnumValues,
    string RawText,
    int LineNumber)
    : DbcCommonStatement("BA_DEF_SGTYPE_", RawText, LineNumber);

public sealed record DbcSignalTypeAttributeValue(
    string Name,
    string SignalTypeName,
    string RawValue,
    string RawText,
    int LineNumber)
    : DbcCommonStatement("BA_SGTYPE_", RawText, LineNumber);

public sealed record DbcRelationAttributeDefinition(
    string RelationType,
    string Name,
    string ValueType,
    double? Minimum,
    double? Maximum,
    IReadOnlyList<string> EnumValues,
    string RawText,
    int LineNumber)
    : DbcCommonStatement("BA_DEF_REL_", RawText, LineNumber);

public sealed record DbcRelationAttributeValue(
    string Name,
    string RelationType,
    string? NodeName,
    uint? MessageId,
    string? SignalName,
    string? EnvironmentName,
    string RawValue,
    string RawText,
    int LineNumber)
    : DbcCommonStatement("BA_REL_", RawText, LineNumber);

public sealed record DbcRelationAttributeDefault(
    string Name,
    string RawValue,
    string RawText,
    int LineNumber)
    : DbcCommonStatement("BA_DEF_DEF_REL_", RawText, LineNumber);

public sealed record DbcRelationStatement(
    string Keyword,
    string? NodeName,
    uint? MessageId,
    string? SignalName,
    string? EnvironmentName,
    string RawText,
    int LineNumber)
    : DbcCommonStatement(Keyword, RawText, LineNumber);

public sealed record DbcCategoryDefinitionStatement(
    string ObjectType,
    string AttributeName,
    string ValueType,
    string RawText,
    int LineNumber)
    : DbcCommonStatement("CAT_DEF_", RawText, LineNumber);

public sealed record DbcFilterStatement(
    uint? MessageId,
    string? SignalName,
    string? ValueType,
    string RawText,
    int LineNumber)
    : DbcCommonStatement("FILTER", RawText, LineNumber);

public sealed record DbcLegacyStatement(
    string Keyword,
    string RawText,
    int LineNumber)
    : DbcCommonStatement(Keyword, RawText, LineNumber);

public sealed record DbcVendorStatement(
    string Keyword,
    string RawText,
    int LineNumber)
    : DbcCommonStatement(Keyword, RawText, LineNumber);

public sealed record DbcOpaqueCommonStatement(
    string Keyword,
    string RawText,
    int LineNumber)
    : DbcCommonStatement(Keyword, RawText, LineNumber);

public sealed record DbcSemanticReference(
    string Kind,
    string RawText,
    int LineNumber);

public sealed record DbcOpaqueStatementEntry(
    string Keyword,
    string RawText,
    int LineNumber);

/// <summary>Read-only semantic projection of a parsed DBC document.</summary>
public sealed class DbcSemanticIndex
{
    public DbcSemanticIndex(
        IReadOnlyList<DbcCommentEntry> fileComments,
        IReadOnlyList<DbcCommentEntry> nodeComments,
        IReadOnlyList<DbcCommentEntry> messageComments,
        IReadOnlyList<DbcCommentEntry> signalComments,
        IReadOnlyList<DbcCommentEntry> environmentComments,
        IReadOnlyList<DbcAttributeDefinition> attributeDefinitions,
        IReadOnlyList<DbcAttributeDefault> attributeDefaults,
        IReadOnlyList<DbcAttributeValue> attributeValues,
        IReadOnlyList<DbcValueTableDefinition> valueTables,
        IReadOnlyList<DbcSignalValueTable> signalValueTables,
        IReadOnlyList<DbcCommonStatement> commonStatements,
        IReadOnlyList<DbcOpaqueStatementEntry> opaqueStatements,
        IReadOnlyList<DbcSemanticReference> unresolved)
    {
        FileComments = fileComments;
        NodeComments = nodeComments;
        MessageComments = messageComments;
        SignalComments = signalComments;
        EnvironmentComments = environmentComments;
        AttributeDefinitions = attributeDefinitions;
        AttributeDefaults = attributeDefaults;
        AttributeValues = attributeValues;
        ValueTables = valueTables;
        SignalValueTables = signalValueTables;
        CommonStatements = commonStatements;
        OpaqueStatements = opaqueStatements;
        Unresolved = unresolved;
    }

    public IReadOnlyList<DbcCommentEntry> FileComments { get; }

    public IReadOnlyList<DbcCommentEntry> NodeComments { get; }

    public IReadOnlyList<DbcCommentEntry> MessageComments { get; }

    public IReadOnlyList<DbcCommentEntry> SignalComments { get; }

    public IReadOnlyList<DbcCommentEntry> EnvironmentComments { get; }

    public IReadOnlyList<DbcAttributeDefinition> AttributeDefinitions { get; }

    public IReadOnlyList<DbcAttributeDefault> AttributeDefaults { get; }

    public IReadOnlyList<DbcAttributeValue> AttributeValues { get; }

    public IReadOnlyList<DbcValueTableDefinition> ValueTables { get; }

    public IReadOnlyList<DbcSignalValueTable> SignalValueTables { get; }

    public IReadOnlyList<DbcCommonStatement> CommonStatements { get; }

    public IReadOnlyList<DbcOpaqueStatementEntry> OpaqueStatements { get; }

    public IReadOnlyList<DbcSemanticReference> Unresolved { get; }
}

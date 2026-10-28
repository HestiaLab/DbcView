#nullable enable
using DbcView.Domain;
using DbcView.Infrastructure;
using DbcView.Web.Services;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcSemanticIndexTests
{
    private const string SemanticFixture =
        "VERSION \"1\"\n" +
        "BU_: ECU1 ECU2\n\n" +
        "BO_ 100 First: 3 ECU1\n" +
        " SG_ A : 0|8@0+ (1,0) [0|255] \"\" ECU2\n" +
        " SG_ B M : 8|8@0+ (1,0) [0|255] \"\" ECU2\n" +
        " SG_ C m1 : 16|8@0+ (1,0) [0|255] \"\" ECU2\n" +
        "BO_ 200 Second: 1 ECU2\n" +
        " SG_ D : 0|8@0+ (1,0) [0|1] \"\" ECU1\n" +
        "CM_ \"file comment\";\n" +
        "CM_ BO_ 100 \"message comment\";\n" +
        "CM_ SG_ 100 A \"signal comment\";\n" +
        "CM_ BU_ ECU1 \"node comment\";\n" +
        "CM_ EV_ Env \"env comment\";\n" +
        "CM_ SG_ 999 Missing \"dangling comment\";\n" +
        "BA_DEF_ \"BusType\" STRING ;\n" +
        "BA_DEF_ BO_ \"GenMsgCycleTime\" INT 0 65535;\n" +
        "BA_DEF_ SG_ \"GenSigStartValue\" FLOAT 0 100;\n" +
        "BA_DEF_ SG_ \"EnumAttr\" ENUM \"Off\",\"On\";\n" +
        "BA_DEF_DEF_ \"BusType\" \"CAN\";\n" +
        "BA_DEF_DEF_ \"GenMsgCycleTime\" 100;\n" +
        "BA_ \"BusType\" \"CAN\";\n" +
        "BA_ \"GenMsgCycleTime\" BO_ 100 50;\n" +
        "BA_ \"GenSigStartValue\" SG_ 100 A 1.5;\n" +
        "BA_ \"EnumAttr\" SG_ 100 B 1;\n" +
        "BA_ \"MissingAttr\" BO_ 200 7;\n" +
        "VAL_ 100 A 0 \"Off\" 1 \"On\";\n" +
        "VAL_TABLE_ State 0 \"Off\" 1 \"On\";\n" +
        "VAL_ 100 B VAL_TABLE_ State;\n" +
        "VAL_ 999 Missing 0 \"x\";\n" +
        "VAL_ 100 C VAL_TABLE_ MissingTable;\n" +
        "BO_TX_BU_ 100 : ECU1,ECU2;\n" +
        "SIG_GROUP_ 100 Group1 1 : A B;\n" +
        "SIG_VALTYPE_ 100 A : 1;\n" +
        "SG_MUL_VAL_ 100 C B 0-1;\n" +
        "EV_ Env : 0 [0|100] \"\" 0 1 DUMMY_NODE_VECTOR0 Vector__XXX;\n";

    [Fact]
    public void Index_collects_comments_by_scope()
    {
        var semantics = ParseSemantics(SemanticFixture);

        Assert.Equal("file comment", Assert.Single(semantics.FileComments).Text);
        Assert.Equal(100u, Assert.Single(semantics.MessageComments).MessageId);
        Assert.Equal("message comment", semantics.MessageComments[0].Text);
        Assert.Equal(2, semantics.SignalComments.Count);
        var signalComment = semantics.SignalComments.Single(
            comment => comment.MessageId == 100 && comment.SignalName == "A");
        Assert.Equal("signal comment", signalComment.Text);
        Assert.Equal("ECU1", Assert.Single(semantics.NodeComments).TargetName);
        Assert.Equal("node comment", semantics.NodeComments[0].Text);
        Assert.Equal("Env", Assert.Single(semantics.EnvironmentComments).TargetName);
        Assert.Equal("env comment", semantics.EnvironmentComments[0].Text);
    }

    [Fact]
    public void Index_collects_attribute_definitions_defaults_and_values()
    {
        var semantics = ParseSemantics(SemanticFixture);

        Assert.Equal(4, semantics.AttributeDefinitions.Count);
        var stringDefinition = semantics.AttributeDefinitions.Single(
            definition => definition.Name == "BusType");
        Assert.Equal(DbcSemanticScope.File, stringDefinition.Scope);
        Assert.Equal("STRING", stringDefinition.ValueType);

        var intDefinition = semantics.AttributeDefinitions.Single(
            definition => definition.Name == "GenMsgCycleTime");
        Assert.Equal(DbcSemanticScope.Message, intDefinition.Scope);
        Assert.Equal(0, intDefinition.Minimum);
        Assert.Equal(65535, intDefinition.Maximum);

        var enumDefinition = semantics.AttributeDefinitions.Single(
            definition => definition.Name == "EnumAttr");
        Assert.Equal(new[] { "Off", "On" }, enumDefinition.EnumValues);

        Assert.Equal(2, semantics.AttributeDefaults.Count);
        Assert.Contains(
            semantics.AttributeDefaults,
            attributeDefault => attributeDefault.Name == "GenMsgCycleTime"
                && attributeDefault.RawValue == "100");

        Assert.Equal(5, semantics.AttributeValues.Count);
        Assert.Contains(
            semantics.AttributeValues,
            value => value.Name == "GenMsgCycleTime"
                && value.Scope == DbcSemanticScope.Message
                && value.MessageId == 100
                && value.RawValue == "50");
        Assert.Contains(
            semantics.AttributeValues,
            value => value.Name == "BusType"
                && value.Scope == DbcSemanticScope.File);
    }

    [Fact]
    public void Index_collects_value_tables_and_references()
    {
        var semantics = ParseSemantics(SemanticFixture);

        var table = Assert.Single(semantics.ValueTables);
        Assert.Equal("State", table.Name);
        Assert.Equal(new[] { "Off", "On" }, table.Entries.Select(entry => entry.Description));

        Assert.Equal(4, semantics.SignalValueTables.Count);
        var inline = semantics.SignalValueTables.Single(tableValue => tableValue.SignalName == "A");
        Assert.Null(inline.ReferencedTable);
        Assert.Equal(2, inline.Entries.Count);

        var referenced = semantics.SignalValueTables.Single(tableValue => tableValue.SignalName == "B");
        Assert.Equal("State", referenced.ReferencedTable);
        Assert.Empty(referenced.Entries);

        var missing = semantics.SignalValueTables.Single(tableValue => tableValue.SignalName == "C");
        Assert.Equal("MissingTable", missing.ReferencedTable);
    }

    [Fact]
    public void Index_collects_common_typed_statements_and_unresolved_entries()
    {
        var semantics = ParseSemantics(SemanticFixture);

        Assert.Equal(
            new[] { "BO_TX_BU_", "SIG_GROUP_", "SIG_VALTYPE_", "SG_MUL_VAL_", "EV_" },
            semantics.CommonStatements.Select(statement => statement.Keyword));

        var transmitters = Assert.IsType<DbcTransmitterStatement>(
            semantics.CommonStatements.Single(statement => statement is DbcTransmitterStatement));
        Assert.Equal(100u, transmitters.MessageId);
        Assert.Equal(new[] { "ECU1", "ECU2" }, transmitters.Transmitters);

        var signalGroup = Assert.IsType<DbcSignalGroupStatement>(
            semantics.CommonStatements.Single(statement => statement is DbcSignalGroupStatement));
        Assert.Equal(100u, signalGroup.MessageId);
        Assert.Equal("Group1", signalGroup.GroupName);
        Assert.Equal(1, signalGroup.Repetitions);
        Assert.Equal(new[] { "A", "B" }, signalGroup.SignalNames);

        var valueType = Assert.IsType<DbcSignalValueTypeStatement>(
            semantics.CommonStatements.Single(statement => statement is DbcSignalValueTypeStatement));
        Assert.Equal(100u, valueType.MessageId);
        Assert.Equal("A", valueType.SignalName);
        Assert.Equal(1, valueType.ValueType);

        var multiplex = Assert.IsType<DbcExtendedMultiplexStatement>(
            semantics.CommonStatements.Single(statement => statement is DbcExtendedMultiplexStatement));
        Assert.Equal(100u, multiplex.MessageId);
        Assert.Equal("C", multiplex.SignalName);
        Assert.Equal("B", multiplex.MultiplexerSignalName);
        Assert.Equal(new[] { new DbcMultiplexRange(0, 1) }, multiplex.Ranges);

        var environment = Assert.IsType<DbcEnvironmentVariableStatement>(
            semantics.CommonStatements.Single(statement => statement is DbcEnvironmentVariableStatement));
        Assert.Equal("Env", environment.Name);
        Assert.Equal(0u, environment.ValueType);
        Assert.Equal(0d, environment.Minimum);
        Assert.Equal(100d, environment.Maximum);
        Assert.Equal("", environment.Unit);
        Assert.Equal("0", environment.InitialValue);
        Assert.Equal(1u, environment.Id);
        Assert.Equal("DUMMY_NODE_VECTOR0", environment.AccessType);
        Assert.Equal(new[] { "Vector__XXX" }, environment.AccessNodes);

        Assert.Equal(4, semantics.Unresolved.Count);
        Assert.Contains(semantics.Unresolved, reference => reference.Kind == "CM_");
        Assert.Contains(semantics.Unresolved, reference => reference.Kind == "BA_DEF_");
        Assert.Contains(semantics.Unresolved, reference => reference.Kind == "VAL_");
        Assert.Contains(semantics.Unresolved, reference => reference.Kind == "VAL_TABLE_");
    }

    [Fact]
    public void Index_falls_back_to_opaque_for_malformed_common_anchors()
    {
        var semantics = ParseSemantics(
            "VERSION \"\"\nBU_: XXX\n\nBO_TX_BU_ nope : ECU1;\nSIG_VALTYPE_ nope X : 1;\n");

        Assert.Equal(2, semantics.CommonStatements.Count);
        Assert.All(
            semantics.CommonStatements,
            statement => Assert.IsType<DbcOpaqueCommonStatement>(statement));
    }

    [Fact]
    public void Index_types_environment_data_statements()
    {
        var semantics = ParseSemantics(
            "VERSION \"\"\nBU_: XXX\n\nEV_DATA_ Env : 8;\nENVVAR_DATA_ Env2 : 4;\n");

        Assert.Equal(2, semantics.CommonStatements.Count);
        var data = Assert.IsType<DbcEnvironmentVariableDataStatement>(semantics.CommonStatements[0]);
        Assert.Equal("EV_DATA_", data.Keyword);
        Assert.Equal("Env", data.Name);
        Assert.Equal(8u, data.DataSize);

        var variableData = Assert.IsType<DbcEnvironmentVariableDataStatement>(
            semantics.CommonStatements[1]);
        Assert.Equal("ENVVAR_DATA_", variableData.Keyword);
        Assert.Equal("Env2", variableData.Name);
        Assert.Equal(4u, variableData.DataSize);
    }

    [Fact]
    public void Generated_envelope_semantics_resolve()
    {
        var content = DbcSyntheticGenerator.Create(
            messageCount: 100,
            signalsPerMessage: 2,
            semanticCount: 50,
            targetBytes: 0);
        var result = DbcParser.Parse(content);
        var semantics = result.Semantics;

        Assert.NotNull(semantics);
        Assert.Empty(semantics!.Unresolved);
    }

    [Fact]
    public void Semantics_full_sample_exposes_v1_capabilities()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Samples",
            "semantics-full.dbc");
        var result = DbcParser.Parse(File.ReadAllText(path));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        var semantics = result.Semantics;
        Assert.NotNull(semantics);
        Assert.NotEmpty(semantics!.FileComments);
        Assert.NotEmpty(semantics.MessageComments);
        Assert.NotEmpty(semantics.SignalComments);
        Assert.NotEmpty(semantics.AttributeDefinitions);
        Assert.NotEmpty(semantics.AttributeDefaults);
        Assert.NotEmpty(semantics.AttributeValues);
        Assert.NotEmpty(semantics.ValueTables);
        Assert.NotEmpty(semantics.SignalValueTables);
        Assert.NotEmpty(semantics.CommonStatements);
        Assert.Contains(
            semantics.AttributeValues,
            value => value.Name == "UndefinedAttr"
                && value.Scope == DbcSemanticScope.Message);
        Assert.Equal(
            new[] { "50", "60" },
            semantics.AttributeValues
                .Where(value => value.Name == "GenMsgCycleTime" && value.MessageId == 100)
                .Select(value => value.RawValue));
        Assert.Contains(
            semantics.CommonStatements,
            statement => statement.Keyword == "SG_MUL_VAL_");
        Assert.Contains(
            semantics.OpaqueStatements,
            statement => statement.Keyword == "VENDOR_UNKNOWN_");
        Assert.Contains(semantics.Unresolved, reference => reference.Kind == "CM_");
        Assert.Contains(semantics.Unresolved, reference => reference.Kind == "BA_DEF_");
        Assert.Contains(semantics.Unresolved, reference => reference.Kind == "VAL_TABLE_");
    }

    [Fact]
    public void Bmw_sample_exposes_its_comments()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Samples", "bmw_e9x_e8x.dbc");
        var result = DbcParser.Parse(File.ReadAllText(path));

        var semantics = result.Semantics;

        Assert.NotNull(semantics);
        Assert.Equal(5, semantics!.FileComments.Count);
        Assert.Equal(33, semantics.SignalComments.Count);
        Assert.Empty(semantics.MessageComments);
        Assert.Empty(semantics.AttributeDefinitions);
    }

    private static DbcSemanticIndex ParseSemantics(string content)
    {
        var result = DbcParser.Parse(content);
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        var semantics = result.Semantics;
        Assert.NotNull(semantics);
        return semantics!;
    }
}

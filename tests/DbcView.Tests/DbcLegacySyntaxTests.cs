#nullable enable
using DbcView.Domain;
using DbcView.Infrastructure;
using DbcView.Infrastructure.Syntax;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcLegacySyntaxTests
{
    private const string Fixture =
        "VERSION \"\"\n" +
        "BU_: ECU1 ECU2\n\n" +
        "BO_ 100 First: 3 ECU1\n" +
        " SG_ A : 0|8@0+ (1,0) [0|255] \"\" ECU2\n" +
        " SG_ B : 8|8@0+ (1,0) [0|255] \"\" ECU2\n" +
        " SG_ C : 16|8@0+ (1,0) [0|255] \"\" ECU2\n" +
        "BO_ 200 Second: 1 ECU2\n" +
        " SIGTYPE_VALTYPE_ TempType : 1;\n" +
        "BA_DEF_SGTYPE_ \"SigTypeAttr\" INT 0 100;\n" +
        "BA_DEF_SGTYPE_ \"SigTypeEnum\" ENUM \"Off\",\"On\";\n" +
        "BA_SGTYPE_ \"SigTypeAttr\" TempType 5;\n" +
        "BA_DEF_REL_ BU_SG_REL_ \"RelAttr\" INT 0 10;\n" +
        "BA_DEF_REL_ BU_EV_REL_ \"RelString\" STRING ;\n" +
        "BA_REL_ \"RelAttr\" BU_SG_REL_ ECU1 SG_ 100 A 3;\n" +
        "BA_REL_ \"RelAttr\" BU_BO_REL_ ECU2 200 7;\n" +
        "BA_REL_ \"RelString\" BU_EV_REL_ ECU1 EV_ Env \"x\";\n" +
        "BA_DEF_DEF_REL_ \"RelAttr\" 0;\n" +
        "BU_SG_REL_ ECU1 SG_ 100 A;\n" +
        "BU_BO_REL_ ECU2 200;\n" +
        "BU_EV_REL_ ECU1 EV_ Env;\n" +
        "CAT_DEF_ BO_ \"CategoryAttr\" INT;\n" +
        "CAT_ Category1;\n" +
        "FILTER 100 A INT;\n" +
        "NS_DESC_ \"legacy description\";\n" +
        "VECTOR__INDEPENDENT_SIG_MSG \"vendor payload\";\n" +
        "VECTOR__XXX \"vendor payload 2\";\n";

    [Fact]
    public void Semantic_index_types_legacy_and_relation_statements()
    {
        var result = DbcParser.Parse(Fixture);
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        var semantics = result.Semantics;
        Assert.NotNull(semantics);
        Assert.Empty(semantics!.Unresolved);
        Assert.Empty(semantics.OpaqueStatements);
        Assert.Equal(19, semantics.CommonStatements.Count);

        var signalTypeValue = Assert.IsType<DbcSignalTypeValueStatement>(
            semantics.CommonStatements.Single(
                statement => statement is DbcSignalTypeValueStatement));
        Assert.Equal("TempType", signalTypeValue.SignalTypeName);
        Assert.Equal(1, signalTypeValue.ValueType);

        var signalTypeDefinitions = semantics.CommonStatements
            .OfType<DbcSignalTypeAttributeDefinition>()
            .ToList();
        Assert.Equal(2, signalTypeDefinitions.Count);
        var integerDefinition = signalTypeDefinitions.Single(
            definition => definition.Name == "SigTypeAttr");
        Assert.Equal("INT", integerDefinition.ValueType);
        Assert.Equal(0, integerDefinition.Minimum);
        Assert.Equal(100, integerDefinition.Maximum);
        var enumDefinition = signalTypeDefinitions.Single(
            definition => definition.Name == "SigTypeEnum");
        Assert.Equal(new[] { "Off", "On" }, enumDefinition.EnumValues);

        var signalTypeAttribute = Assert.IsType<DbcSignalTypeAttributeValue>(
            semantics.CommonStatements.Single(
                statement => statement is DbcSignalTypeAttributeValue));
        Assert.Equal("SigTypeAttr", signalTypeAttribute.Name);
        Assert.Equal("TempType", signalTypeAttribute.SignalTypeName);
        Assert.Equal("5", signalTypeAttribute.RawValue);

        var relationDefinitions = semantics.CommonStatements
            .OfType<DbcRelationAttributeDefinition>()
            .ToList();
        Assert.Equal(2, relationDefinitions.Count);
        var signalRelationDefinition = relationDefinitions.Single(
            definition => definition.RelationType == "BU_SG_REL_");
        Assert.Equal("RelAttr", signalRelationDefinition.Name);
        Assert.Equal("INT", signalRelationDefinition.ValueType);
        Assert.Equal(0, signalRelationDefinition.Minimum);
        Assert.Equal(10, signalRelationDefinition.Maximum);
        var environmentRelationDefinition = relationDefinitions.Single(
            definition => definition.RelationType == "BU_EV_REL_");
        Assert.Equal("STRING", environmentRelationDefinition.ValueType);

        var relationValues = semantics.CommonStatements
            .OfType<DbcRelationAttributeValue>()
            .ToList();
        Assert.Equal(3, relationValues.Count);
        var signalValue = relationValues.Single(value => value.RelationType == "BU_SG_REL_");
        Assert.Equal("RelAttr", signalValue.Name);
        Assert.Equal("ECU1", signalValue.NodeName);
        Assert.Equal(100u, signalValue.MessageId);
        Assert.Equal("A", signalValue.SignalName);
        Assert.Equal("3", signalValue.RawValue);
        var messageValue = relationValues.Single(value => value.RelationType == "BU_BO_REL_");
        Assert.Equal("ECU2", messageValue.NodeName);
        Assert.Equal(200u, messageValue.MessageId);
        Assert.Equal("7", messageValue.RawValue);
        var environmentValue = relationValues.Single(value => value.RelationType == "BU_EV_REL_");
        Assert.Equal("ECU1", environmentValue.NodeName);
        Assert.Equal("Env", environmentValue.EnvironmentName);
        Assert.Equal("\"x\"", environmentValue.RawValue);

        var relationDefault = Assert.IsType<DbcRelationAttributeDefault>(
            semantics.CommonStatements.Single(
                statement => statement is DbcRelationAttributeDefault));
        Assert.Equal("RelAttr", relationDefault.Name);
        Assert.Equal("0", relationDefault.RawValue);

        var relations = semantics.CommonStatements.OfType<DbcRelationStatement>().ToList();
        Assert.Equal(3, relations.Count);
        var signalRelation = relations.Single(relation => relation.Keyword == "BU_SG_REL_");
        Assert.Equal("ECU1", signalRelation.NodeName);
        Assert.Equal(100u, signalRelation.MessageId);
        Assert.Equal("A", signalRelation.SignalName);
        var messageRelation = relations.Single(relation => relation.Keyword == "BU_BO_REL_");
        Assert.Equal("ECU2", messageRelation.NodeName);
        Assert.Equal(200u, messageRelation.MessageId);
        var environmentRelation = relations.Single(relation => relation.Keyword == "BU_EV_REL_");
        Assert.Equal("ECU1", environmentRelation.NodeName);
        Assert.Equal("Env", environmentRelation.EnvironmentName);

        var categoryDefinition = Assert.IsType<DbcCategoryDefinitionStatement>(
            semantics.CommonStatements.Single(
                statement => statement is DbcCategoryDefinitionStatement));
        Assert.Equal("BO_", categoryDefinition.ObjectType);
        Assert.Equal("CategoryAttr", categoryDefinition.AttributeName);
        Assert.Equal("INT", categoryDefinition.ValueType);

        var filter = Assert.IsType<DbcFilterStatement>(
            semantics.CommonStatements.Single(statement => statement is DbcFilterStatement));
        Assert.Equal(100u, filter.MessageId);
        Assert.Equal("A", filter.SignalName);
        Assert.Equal("INT", filter.ValueType);

        Assert.Equal(
            new[] { "CAT_", "NS_DESC_" },
            semantics.CommonStatements
                .OfType<DbcLegacyStatement>()
                .Select(statement => statement.Keyword));
        Assert.Equal(
            new[] { "VECTOR__INDEPENDENT_SIG_MSG", "VECTOR__XXX" },
            semantics.CommonStatements
                .OfType<DbcVendorStatement>()
                .Select(statement => statement.Keyword));
    }

    [Fact]
    public void Malformed_legacy_statements_fall_back_without_losing_classification()
    {
        const string content =
            "VERSION \"\"\nBU_: ECU1\n\n" +
            "SIGTYPE_VALTYPE_ TempType;\n" +
            "BA_DEF_SGTYPE_ no-quote;\n" +
            "BA_SGTYPE_ \"Attr\";\n" +
            "BA_DEF_REL_ \"Rel\" INT;\n" +
            "BA_REL_ \"Rel\" BU_SG_REL_ ECU1;\n" +
            "BA_DEF_DEF_REL_ no-quote;\n" +
            "BU_SG_REL_ ECU1;\n" +
            "BU_BO_REL_ ECU1;\n" +
            "BU_EV_REL_ ECU1;\n" +
            "CAT_DEF_ BO_ \"Attr\";\n" +
            "CAT_ ;\n" +
            "FILTER ;\n" +
            "NS_DESC_ \"x\";\n";

        var result = DbcParser.Parse(content);
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        var semantics = result.Semantics;
        Assert.NotNull(semantics);
        Assert.Equal(13, semantics!.CommonStatements.Count);

        var opaqueKeywords = semantics.CommonStatements
            .OfType<DbcOpaqueCommonStatement>()
            .Select(statement => statement.Keyword)
            .ToList();
        Assert.Equal(
            new[]
            {
                "SIGTYPE_VALTYPE_",
                "BA_DEF_SGTYPE_",
                "BA_SGTYPE_",
                "BA_DEF_REL_",
                "BA_DEF_DEF_REL_",
                "BU_SG_REL_",
                "BU_BO_REL_",
                "BU_EV_REL_",
                "CAT_DEF_",
            },
            opaqueKeywords);

        var partialRelation = Assert.IsType<DbcRelationAttributeValue>(
            semantics.CommonStatements.Single(
                statement => statement is DbcRelationAttributeValue));
        Assert.Equal("ECU1", partialRelation.NodeName);
        Assert.Null(partialRelation.MessageId);
        Assert.Null(partialRelation.SignalName);

        var filter = Assert.IsType<DbcFilterStatement>(
            semantics.CommonStatements.Single(statement => statement is DbcFilterStatement));
        Assert.Null(filter.MessageId);
        Assert.Null(filter.SignalName);
        Assert.Null(filter.ValueType);

        Assert.Equal(
            new[] { "CAT_", "NS_DESC_" },
            semantics.CommonStatements
                .OfType<DbcLegacyStatement>()
                .Select(statement => statement.Keyword));
    }

    [Fact]
    public void Legacy_statements_round_trip_losslessly()
    {
        var result = DbcParser.Parse(Fixture);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(
            NormalizeLines(Fixture),
            NormalizeLines(DbcParser.Serialize(result.Document)));
    }

    [Fact]
    public void Standard_signal_type_statements_are_known_but_not_interpreted()
    {
        const string content =
            "VERSION \"\"\nBU_: ECU1\n\n" +
            "SGTYPE_ TempType : 8@1+ (1,0) [0|255] \"\" 0 , ;\n" +
            "SGTYPE_VAL_ TempType 0 \"Off\" 1 \"On\";\n" +
            "SIG_TYPE_REF_ 100 A : TempType;\n";

        var result = DbcParser.Parse(content);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        var semantics = result.Semantics;
        Assert.NotNull(semantics);
        Assert.Equal(
            new[] { "SGTYPE_", "SGTYPE_VAL_", "SIG_TYPE_REF_" },
            semantics!.CommonStatements
                .OfType<DbcOpaqueCommonStatement>()
                .Select(statement => statement.Keyword));
        Assert.Empty(semantics.CommonStatements.OfType<DbcVendorStatement>());
        Assert.Equal(
            NormalizeLines(content),
            NormalizeLines(DbcParser.Serialize(result.Document)));
    }

    [Fact]
    public void Unknown_vendor_lines_form_their_own_raw_statement()
    {
        const string content =
            "VERSION \"\"\nBU_: ECU1\n\n" +
            "BO_ 100 First: 1 ECU1\n" +
            " SG_ A : 0|1@0+ (1,0) [0|1] \"\" ECU1\n" +
            "UNKNOWN_VENDOR_ 1 2 3;\n";

        var tree = DbcSyntaxTree.Parse(content);

        Assert.Equal(5, tree.StatementCount);
        Assert.Equal(DbcStatementKind.Raw, tree.Statements[4].Kind);
        Assert.Equal("UNKNOWN_VENDOR_ 1 2 3;", tree.Statements[4].RawText.Trim());
    }

    [Fact]
    public void Unknown_vendor_statements_are_preserved_and_not_typed()
    {
        const string content =
            "VERSION \"\"\nBU_: ECU1\n\n" +
            "BO_ 100 First: 1 ECU1\n" +
            " SG_ A : 0|1@0+ (1,0) [0|1] \"\" ECU1\n" +
            "UNKNOWN_VENDOR_ 1 2 3;\n" +
            "BO_ 200 Second: 1 ECU1\n";

        var result = DbcParser.Parse(content);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        var semantics = result.Semantics;
        Assert.NotNull(semantics);
        Assert.DoesNotContain(
            semantics!.CommonStatements,
            statement => statement.Keyword == "UNKNOWN_VENDOR_");
        Assert.Empty(semantics.Unresolved);
        var opaque = Assert.Single(semantics.OpaqueStatements);
        Assert.Equal("UNKNOWN_VENDOR_", opaque.Keyword);
        Assert.Equal(6, opaque.LineNumber);
        Assert.Contains("UNKNOWN_VENDOR_ 1 2 3;", opaque.RawText, StringComparison.Ordinal);
        Assert.Contains(
            "UNKNOWN_VENDOR_ 1 2 3;",
            DbcParser.Serialize(result.Document),
            StringComparison.Ordinal);
    }

    private static string[] NormalizeLines(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
    }
}

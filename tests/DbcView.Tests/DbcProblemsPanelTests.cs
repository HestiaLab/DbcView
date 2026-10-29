#nullable enable
using System;
using DbcView.Domain;
using DbcView.Web.Components;
using Bunit;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcProblemsPanelTests : BunitContext
{
    public DbcProblemsPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        AtlasJsInteropSetup.Setup(JSInterop);
    }

    [Fact]
    public void Renders_severity_line_code_and_message_for_each_diagnostic()
    {
        var diagnostics = new[]
        {
            new DbcDiagnostic(
                DbcDiagnosticSeverity.Error,
                "DBC001",
                "Missing VERSION section.",
                0),
            new DbcDiagnostic(
                DbcDiagnosticSeverity.Warning,
                "DBC006",
                "Signal 'TooWide' spans bits 31..38.",
                61),
        };

        var cut = Render<DbcProblemsPanel>(p => p
            .Add(x => x.DocumentKey, "DBC/bmw.dbc")
            .Add(x => x.Diagnostics, diagnostics));

        var rows = cut.FindAll("[data-testid='dbc-problem-row']");
        Assert.Equal(2, rows.Count);

        Assert.Equal("error", rows[0].GetAttribute("data-severity"));
        Assert.Contains("Error", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("DBC001", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Missing VERSION section.", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Equal(
            "—",
            cut.FindAll("[data-testid='dbc-problem-line']")[0].TextContent.Trim());

        Assert.Equal("warning", rows[1].GetAttribute("data-severity"));
        Assert.Equal(
            "61",
            cut.FindAll("[data-testid='dbc-problem-line']")[1].TextContent.Trim());
        Assert.Contains("spans bits", rows[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_state_explains_when_no_document_is_open()
    {
        var cut = Render<DbcProblemsPanel>(p => p
            .Add(x => x.Diagnostics, Array.Empty<DbcDiagnostic>()));

        Assert.Contains(
            "Open a .dbc file",
            cut.Find("[data-testid='dbc-problems-empty']").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_state_reports_no_problems_for_an_open_document()
    {
        var cut = Render<DbcProblemsPanel>(p => p
            .Add(x => x.DocumentKey, "DBC/a.dbc")
            .Add(x => x.Diagnostics, Array.Empty<DbcDiagnostic>()));

        Assert.Contains(
            "No problems found.",
            cut.Find("[data-testid='dbc-problems-empty']").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Row_click_reports_the_line_number()
    {
        int? located = null;
        var diagnostics = new[]
        {
            new DbcDiagnostic(
                DbcDiagnosticSeverity.Warning,
                "DBC006",
                "Out of range.",
                12),
        };

        var cut = Render<DbcProblemsPanel>(p => p
            .Add(x => x.DocumentKey, "DBC/a.dbc")
            .Add(x => x.Diagnostics, diagnostics)
            .Add(x => x.OnLocate, line => located = line));

        cut.Find("[data-testid='dbc-problem-row']").Click();

        Assert.Equal(12, located);
    }
}

#nullable enable
using Bunit;
using DbcView.Web.Components;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcViewTopToolbarTests : BunitContext
{
    private static readonly string[] RemovedPlaceholders =
    {
        "File", "Edit", "View", "Navigate", "Code", "Run", "Run target", "Debug",
    };

    [Fact]
    public void Top_toolbar_exposes_only_real_layout_history_actions()
    {
        var cut = Render<DbcViewTopToolbar>();

        var buttons = cut.FindAll("button");
        Assert.Equal(2, buttons.Count);
        Assert.Equal(2, buttons.Count(button => button.HasAttribute("disabled")));

        foreach (var button in buttons)
        {
            Assert.False(string.IsNullOrWhiteSpace(button.GetAttribute("aria-label")));
            Assert.False(string.IsNullOrWhiteSpace(button.GetAttribute("title")));
            Assert.NotNull(button.QuerySelector("svg[aria-hidden='true']"));
        }

        foreach (var placeholder in RemovedPlaceholders)
        {
            Assert.DoesNotContain(
                buttons,
                button => string.Equals(button.TextContent.Trim(), placeholder, StringComparison.Ordinal)
                    || string.Equals(button.GetAttribute("aria-label"), placeholder, StringComparison.Ordinal)
                    || string.Equals(button.GetAttribute("title"), placeholder, StringComparison.Ordinal));
        }

        Assert.DoesNotContain("feature/dbc-view", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Run target", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Debug", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='dbc-open-palette']"));
    }

    [Fact]
    public void Top_toolbar_exposes_search_only_when_a_document_is_open()
    {
        var searchCount = 0;
        var cut = Render<DbcViewTopToolbar>(parameters => parameters
            .Add(component => component.CanSearch, true)
            .Add(
                component => component.OnSearch,
                EventCallback.Factory.Create(this, () => searchCount++)));

        var search = cut.Find("[data-testid='dbc-open-palette']");
        Assert.Equal("Search messages and signals", search.GetAttribute("aria-label"));
        Assert.Contains("/", search.GetAttribute("title"), StringComparison.Ordinal);

        search.Click();

        Assert.Equal(1, searchCount);
    }

    [Fact]
    public void Top_toolbar_enables_and_invokes_the_real_history_callbacks()
    {
        var undoCount = 0;
        var redoCount = 0;

        var cut = Render<DbcViewTopToolbar>(parameters => parameters
            .Add(component => component.CanUndo, true)
            .Add(component => component.CanRedo, true)
            .Add(component => component.OnUndo, EventCallback.Factory.Create(this, () => undoCount++))
            .Add(component => component.OnRedo, EventCallback.Factory.Create(this, () => redoCount++)));

        var buttons = cut.FindAll("button");
        Assert.False(buttons[0].HasAttribute("disabled"));
        Assert.False(buttons[1].HasAttribute("disabled"));

        buttons[0].Click();
        buttons[1].Click();

        Assert.Equal(1, undoCount);
        Assert.Equal(1, redoCount);
    }
}

public sealed class DbcViewStatusBarTests : BunitContext
{
    private static readonly string[] RemovedPlaceholders =
    {
        "LF", "UTF-8", "4 spaces", "feature/dbc-view",
    };

    [Fact]
    public void Status_bar_renders_only_real_derived_state()
    {
        var cut = Render<DbcViewStatusBar>(parameters => parameters
            .Add(component => component.OperationStatus, "Opened DBC/engine.dbc")
            .Add(component => component.Document, "DBC/engine.dbc")
            .Add(component => component.Revision, 7)
            .Add(component => component.ErrorCount, 2)
            .Add(component => component.WarningCount, 1));

        var status = cut.Find("footer");
        Assert.Null(status.GetAttribute("role"));
        var message = cut.Find(".dbc-statusbar-message");
        Assert.Equal("status", message.GetAttribute("role"));
        Assert.Equal("polite", message.GetAttribute("aria-live"));
        Assert.Contains("Opened DBC/engine.dbc", status.TextContent, StringComparison.Ordinal);
        Assert.Contains("2 errors", cut.Find("[data-testid='dbc-status-errors']").TextContent, StringComparison.Ordinal);
        Assert.Contains("1 warning", cut.Find("[data-testid='dbc-status-warnings']").TextContent, StringComparison.Ordinal);
        Assert.Contains("DBC/engine.dbc", cut.Find("[data-testid='dbc-status-document']").TextContent, StringComparison.Ordinal);
        Assert.Contains("Revision 7", cut.Find("[data-testid='dbc-status-revision']").TextContent, StringComparison.Ordinal);

        foreach (var placeholder in RemovedPlaceholders)
        {
            Assert.DoesNotContain(placeholder, status.TextContent, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(@"\bmain\b", status.TextContent);
    }

    [Fact]
    public void Status_bar_omits_items_that_are_not_real_state()
    {
        var cut = Render<DbcViewStatusBar>();

        Assert.Equal("Ready", cut.Find(".dbc-statusbar-message").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid='dbc-status-errors']"));
        Assert.Empty(cut.FindAll("[data-testid='dbc-status-warnings']"));
        Assert.Empty(cut.FindAll("[data-testid='dbc-status-document']"));
        Assert.Empty(cut.FindAll("[data-testid='dbc-status-revision']"));

        foreach (var placeholder in RemovedPlaceholders)
        {
            Assert.DoesNotContain(placeholder, cut.Markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Status_bar_diagnostics_raise_the_problems_callback()
    {
        var selected = 0;
        var cut = Render<DbcViewStatusBar>(parameters => parameters
            .Add(component => component.ErrorCount, 2)
            .Add(component => component.WarningCount, 1)
            .Add(component => component.OnProblemsSelected, () => selected++));

        cut.Find("[data-testid='dbc-status-errors']").Click();
        cut.Find("[data-testid='dbc-status-warnings']").Click();

        Assert.Equal(2, selected);
    }

    [Fact]
    public void Status_bar_uses_singular_labels_for_single_diagnostics()
    {
        var cut = Render<DbcViewStatusBar>(parameters => parameters
            .Add(component => component.ErrorCount, 1)
            .Add(component => component.WarningCount, 1));

        Assert.Contains("1 error", cut.Find("[data-testid='dbc-status-errors']").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("1 errors", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("1 warning", cut.Find("[data-testid='dbc-status-warnings']").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("1 warnings", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_bar_shows_detected_encoding_and_undefined_bytes()
    {
        var cut = Render<DbcViewStatusBar>(parameters => parameters
            .Add(component => component.Encoding, "Windows-1252 (fallback)")
            .Add(component => component.UndefinedByteCount, 2));

        var encoding = cut.Find("[data-testid='dbc-status-encoding']");
        Assert.Contains("Windows-1252 (fallback)", encoding.TextContent, StringComparison.Ordinal);
        Assert.Contains("2 undefined", encoding.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_bar_omits_encoding_without_a_detected_document()
    {
        var cut = Render<DbcViewStatusBar>();

        Assert.Empty(cut.FindAll("[data-testid='dbc-status-encoding']"));
    }
}

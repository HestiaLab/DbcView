#nullable enable
using Bunit;
using DbcView.Domain;
using DbcView.Web;
using DbcView.Web.Components;
using DbcView.Web.ViewModels;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcCommandPaletteTests : BunitContext
{
    public DbcCommandPaletteTests()
    {
        Services.AddDbcViewServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        AtlasJsInteropSetup.Setup(JSInterop);
    }

    [Fact]
    public void Empty_query_shows_hint_and_no_results()
    {
        var cut = RenderPalette(out _, out _);

        Assert.NotNull(cut.Find("[data-testid='dbc-palette-hint']"));
        Assert.Empty(cut.FindAll("[data-testid='dbc-palette-result']"));
        var input = cut.Find("[data-testid='dbc-palette-input']");
        Assert.Equal("false", input.GetAttribute("aria-expanded"));
        Assert.Null(input.GetAttribute("aria-activedescendant"));
    }

    [Fact]
    public void Query_renders_grouped_results_and_announces_the_count()
    {
        var cut = RenderPalette(out _, out _);

        cut.Find("[data-testid='dbc-palette-input']").Input("speed");

        var results = cut.FindAll("[data-testid='dbc-palette-result']");
        Assert.Equal(3, results.Count);
        Assert.Equal("message", results[0].GetAttribute("data-kind"));
        Assert.Equal("signal", results[1].GetAttribute("data-kind"));
        Assert.Contains("Messages", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Signals", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("3 results.", cut.Find("[role='status']").TextContent.Trim());
        Assert.Equal("dbc-palette-option-0", results[0].GetAttribute("id"));
        Assert.Equal("true", results[0].GetAttribute("aria-selected"));
    }

    [Fact]
    public async Task Keyboard_moves_the_active_descendant_and_enter_selects()
    {
        var cut = RenderPalette(out var model, out var selected);
        var input = cut.Find("[data-testid='dbc-palette-input']");
        input.Input("speed");

        await input.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal(
            "dbc-palette-option-1",
            input.GetAttribute("aria-activedescendant"));
        Assert.Equal(
            "true",
            cut.FindAll("[data-testid='dbc-palette-result']")[1].GetAttribute("aria-selected"));

        await input.KeyDownAsync(new KeyboardEventArgs { Key = "End" });
        Assert.Equal(
            "dbc-palette-option-2",
            input.GetAttribute("aria-activedescendant"));

        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Home" });
        Assert.Equal(
            "dbc-palette-option-0",
            input.GetAttribute("aria-activedescendant"));

        await input.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.NotNull(selected.Value);
        Assert.Equal("SpeedWheel", selected.Value!.PrimaryText);
        Assert.Equal(DbcPaletteResultKind.Signal, selected.Value.Kind);
        Assert.Equal("SpeedWheel", model.SignalResults[0].PrimaryText);
    }

    [Fact]
    public async Task Escape_closes_the_palette()
    {
        var closed = 0;
        var cut = RenderPalette(out _, out _, () => closed++);

        var input = cut.Find("[data-testid='dbc-palette-input']");
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(1, closed);
    }

    [Fact]
    public void No_matches_renders_the_empty_state()
    {
        var cut = RenderPalette(out _, out _);

        cut.Find("[data-testid='dbc-palette-input']").Input("zzz");

        Assert.NotNull(cut.Find("[data-testid='dbc-palette-empty']"));
        Assert.Empty(cut.FindAll("[data-testid='dbc-palette-result']"));
        Assert.Equal("No matches.", cut.Find("[role='status']").TextContent.Trim());
    }

    [Fact]
    public async Task Close_button_closes_the_palette()
    {
        var closed = 0;
        var cut = RenderPalette(out _, out _, () => closed++);

        await cut.Find("[data-testid='dbc-palette-close']").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, closed);
    }

    private IRenderedComponent<DbcCommandPalette> RenderPalette(
        out DbcCommandPaletteModel model,
        out SelectedResult selected,
        Action? onClosed = null)
    {
        var paletteModel = new DbcCommandPaletteModel();
        paletteModel.SetDocument(Document);
        model = paletteModel;
        var selectedResult = new SelectedResult();
        selected = selectedResult;

        return Render<DbcCommandPalette>(parameters =>
        {
            parameters
                .Add(component => component.Model, paletteModel)
                .Add(
                    component => component.OnSelected,
                    EventCallback.Factory.Create<DbcPaletteResult>(
                        this,
                        result => selectedResult.Value = result));
            if (onClosed is not null)
            {
                parameters.Add(
                    component => component.OnClosed,
                    EventCallback.Factory.Create(this, onClosed));
            }
        });
    }

    private static readonly DbcDocument Document = CreateDocument(
        new DbcMessage(
            100,
            "EngineSpeed",
            8,
            "ECU1",
            new[]
            {
                Signal("SpeedWheel", "ECU2"),
                Signal("BrakePressure", "ECU1"),
            }),
        new DbcMessage(
            200,
            "EngineTemp",
            8,
            "ECU2",
            new[] { Signal("SpeedLimit", "ECU1") }));

    private sealed class SelectedResult
    {
        public DbcPaletteResult? Value { get; set; }
    }

    private static DbcSignal Signal(string name, string receiver)
    {
        return new DbcSignal(
            name,
            0,
            8,
            isBigEndian: false,
            isSigned: false,
            1,
            0,
            0,
            255,
            string.Empty,
            receiver);
    }

    private static DbcDocument CreateDocument(params DbcMessage[] messages)
    {
        return new DbcDocument(
            string.Empty,
            Array.Empty<string>(),
            string.Empty,
            messages.Select(message => (DbcSection)new DbcMessageSection(message)).ToArray());
    }
}

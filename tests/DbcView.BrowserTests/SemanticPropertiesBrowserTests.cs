using System.Text;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace DbcView.BrowserTests;

public sealed class SemanticPropertiesBrowserTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _fixture;

    public SemanticPropertiesBrowserTests(WebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Semantic_sections_render_and_source_lines_select_text()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await UploadAsync(page, "semantics-full.dbc", File.ReadAllText(SamplePath("semantics-full.dbc")));

        await page.Locator("[data-row-key='m:0'] [data-testid='dbc-message']").ClickAsync();
        await Expect(page.Locator("[data-testid='dbc-property-attributes']")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='dbc-attribute'][data-duplicate='true']"))
            .ToHaveCountAsync(2);
        await Expect(page.Locator("[data-testid='dbc-extended-multiplexing']")).ToBeVisibleAsync();

        await page.Locator("[data-testid='dbc-locate-source']").First.ClickAsync();

        var textarea = page.Locator("textarea");
        await Expect(textarea).ToBeVisibleAsync();
        await Expect(textarea).ToBeFocusedAsync();
        var selected = await textarea.EvaluateAsync<string>(
            "element => element.value.slice(element.selectionStart, element.selectionEnd)");
        Assert.False(string.IsNullOrWhiteSpace(selected));
        Assert.Contains("Engine message comment", selected, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Value_table_dialog_opens_and_returns_focus()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await UploadAsync(page, "semantics-values.dbc", File.ReadAllText(SamplePath("semantics-full.dbc")));

        await page.Locator("[data-row-key='m:0'] [data-testid='dbc-toggle-message']").ClickAsync();
        await page.Locator("[data-row-key='s:0:0'] [data-testid='dbc-signal']").ClickAsync();
        await Expect(page.Locator("[data-testid='dbc-property-value-table']")).ToBeVisibleAsync();

        var viewAll = page.Locator("[data-testid='dbc-view-value-table']");
        await viewAll.ClickAsync();
        var dialog = page.Locator("[data-testid='dbc-value-table-dialog']");
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog.Locator(".dbc-value-row")).ToHaveCountAsync(12);

        await page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(viewAll).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Comment_search_highlights_the_matching_property_comment()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await UploadAsync(page, "semantics-comments.dbc", File.ReadAllText(SamplePath("semantics-full.dbc")));

        await page.Locator("[data-testid='dbc-open-palette']").ClickAsync();
        var input = page.Locator("[data-testid='dbc-palette-input']");
        await input.FillAsync("Vehicle speed");
        var result = page.Locator("[data-testid='dbc-palette-result']");
        await Expect(result).ToHaveCountAsync(1);
        await Expect(result).ToContainTextAsync("Speed");
        await Expect(result).ToContainTextAsync("Vehicle speed");

        await input.PressAsync("Enter");

        await Expect(page.Locator("[data-testid='dbc-property-comments']")).ToBeVisibleAsync();
        await Expect(page.Locator(
            "[data-testid='dbc-comment'][data-highlighted='true']"))
            .ToContainTextAsync("Vehicle speed");
    }

    [Fact]
    public async Task Legacy_windows_1252_upload_reports_encoding_and_readable_comment()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await UploadBytesAsync(
            page,
            "legacy-windows1252.dbc",
            File.ReadAllBytes(SamplePath("legacy-windows1252.dbc")));

        await Expect(page.Locator("[data-testid='dbc-status-encoding']"))
            .ToContainTextAsync("Windows-1252 (fallback)");
        await Expect(page.Locator("[data-testid='dbc-status-encoding']"))
            .ToContainTextAsync("1 undefined");
        await Expect(page.Locator("[data-testid='dbc-file-encoding']"))
            .ToContainTextAsync("Windows-1252 (fallback)");
        await Expect(page.Locator("[data-testid='dbc-file-undefined-bytes']"))
            .ToContainTextAsync("1");

        await page.Locator("[data-testid='dbc-mode-text']").ClickAsync();
        var text = await page.Locator("textarea").InputValueAsync();
        Assert.Contains("café", text, StringComparison.Ordinal);
        Assert.Contains("\u20AC", text, StringComparison.Ordinal);
        Assert.Contains("\u201Cquoted\u201D", text, StringComparison.Ordinal);
        Assert.Contains("\uFFFD", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Narrow_source_jump_returns_to_the_editor()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await UploadAsync(page, "semantics-narrow.dbc", File.ReadAllText(SamplePath("semantics-full.dbc")));

        await page.Locator("[data-row-key='m:0'] [data-testid='dbc-message']").ClickAsync();
        await page.SetViewportSizeAsync(375, 800);
        await Expect(page.Locator("[data-testid='dbc-property-attributes']")).ToBeVisibleAsync();

        await page.Locator("[data-testid='dbc-locate-source']").First.ClickAsync();

        await Expect(page.Locator("[data-testid='dbc-compact-editor']"))
            .ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(page.Locator("textarea")).ToBeVisibleAsync();
        await Expect(page.Locator("textarea")).ToBeFocusedAsync();
    }

    private static string SamplePath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Samples", fileName);
    }

    private static async Task UploadAsync(IPage page, string fileName, string content)
    {
        await UploadBytesAsync(page, fileName, Encoding.UTF8.GetBytes(content));
    }

    private static async Task UploadBytesAsync(
        IPage page,
        string fileName,
        byte[] content)
    {
        await page.SetInputFilesAsync(
            "input[type=file]",
            new FilePayload
            {
                Name = fileName,
                MimeType = "text/plain",
                Buffer = content,
            });

        var file = page.Locator("[data-testid='dbc-file']").First;
        await Expect(file).ToBeVisibleAsync();
        await file.ClickAsync();
        await Expect(page.Locator("[data-testid='dbc-mode-text']")).ToBeVisibleAsync();
    }
}

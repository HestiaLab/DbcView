using System.Text;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace DbcView.BrowserTests;

public sealed class CommandPaletteBrowserTests : IClassFixture<WebAppFixture>
{
    private const string SmallDbc =
        "VERSION \"\"\nBU_: XXX\n\n" +
        "BO_ 100 First: 2 XXX\n" +
        " SG_ A : 0|8@0+ (1,0) [0|255] \"\" XXX\n" +
        " SG_ B : 8|8@0+ (1,0) [0|255] \"\" XXX\n\n" +
        "BO_ 200 Second: 1 XXX\n" +
        " SG_ C : 0|8@0+ (1,0) [0|255] \"\" XXX\n";

    private readonly WebAppFixture _fixture;

    public CommandPaletteBrowserTests(WebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Slash_opens_the_modal_and_enter_jumps_to_a_signal()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await UploadAsync(page, "palette.dbc", SmallDbc);

        await page.Keyboard.PressAsync("/");

        var dialog = page.Locator("[data-testid='dbc-command-palette']");
        await Expect(dialog).ToBeVisibleAsync();
        var input = page.Locator("[data-testid='dbc-palette-input']");
        await Expect(input).ToBeFocusedAsync();

        await input.FillAsync("C");
        var results = page.Locator("[data-testid='dbc-palette-result']");
        await Expect(results).ToHaveCountAsync(2);
        await Expect(results.First).ToContainTextAsync("Second");

        await input.PressAsync("ArrowDown");
        await input.PressAsync("Enter");

        await Expect(dialog).ToBeHiddenAsync();
        var signal = page.Locator("[data-testid='dbc-signal']").First;
        await Expect(signal).ToBeVisibleAsync();
        await Expect(signal).ToBeFocusedAsync();
        Assert.Contains("C", await signal.TextContentAsync(), StringComparison.Ordinal);
        await Expect(page.Locator("[data-testid='dbc-property']"))
            .ToContainTextAsync("Signal");
        await Expect(page.Locator("[data-testid='dbc-view-shell']"))
            .ToHaveAttributeAsync("data-compact-active", "editor");
    }

    [Fact]
    public async Task Escape_returns_focus_to_the_search_button_and_slash_stays_editable()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await UploadAsync(page, "palette-focus.dbc", SmallDbc);

        var searchButton = page.Locator("[data-testid='dbc-open-palette']");
        await searchButton.ClickAsync();
        var dialog = page.Locator("[data-testid='dbc-command-palette']");
        await Expect(dialog).ToBeVisibleAsync();
        Assert.True(await page.EvaluateAsync<bool>(
            "document.querySelector('[data-testid=\"dbc-command-palette\"]').matches(':modal')"));

        await page.Keyboard.PressAsync("Tab");
        Assert.True(await page.EvaluateAsync<bool>(
            "document.activeElement !== null && "
            + "document.activeElement.closest('[data-testid=\"dbc-command-palette\"]') !== null"));

        await page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(searchButton).ToBeFocusedAsync();

        await page.Locator("[data-testid='dbc-mode-text']").ClickAsync();
        var textarea = page.Locator("textarea");
        await textarea.ClickAsync();
        await page.Keyboard.PressAsync("/");

        await Expect(dialog).ToBeHiddenAsync();
        Assert.Contains("/", await textarea.InputValueAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Palette_selection_lands_in_the_editor_on_a_narrow_viewport()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await UploadAsync(page, "palette-narrow.dbc", SmallDbc);
        await page.SetViewportSizeAsync(375, 800);
        await Expect(page.Locator("[data-testid='dbc-compact-nav']")).ToBeVisibleAsync();

        await page.Locator("[data-testid='dbc-compact-properties']").ClickAsync();
        await Expect(page.Locator("[data-testid='dbc-table']")).ToBeHiddenAsync();

        await page.Locator("[data-testid='dbc-open-palette']").ClickAsync();
        var input = page.Locator("[data-testid='dbc-palette-input']");
        await Expect(input).ToBeFocusedAsync();
        await input.FillAsync("C");
        await Expect(page.Locator("[data-testid='dbc-palette-result']")).ToHaveCountAsync(2);
        await input.PressAsync("ArrowDown");
        await input.PressAsync("Enter");
        await Expect(page.Locator("[data-testid='dbc-command-palette']")).ToBeHiddenAsync();
        await Expect(page.Locator("[data-testid='dbc-property']")).ToContainTextAsync("Signal");

        await Expect(page.Locator("[data-testid='dbc-view-shell']"))
            .ToHaveAttributeAsync("data-compact-active", "editor");
        await Expect(page.Locator("[data-testid='dbc-compact-editor']"))
            .ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(page.Locator("[data-testid='dbc-table']")).ToBeVisibleAsync();
        var signal = page.Locator("[data-testid='dbc-signal']").First;
        await Expect(signal).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Palette_accepts_decimal_and_hex_message_ids()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await UploadAsync(page, "palette-id.dbc", SmallDbc);

        await page.Locator("[data-testid='dbc-open-palette']").ClickAsync();
        var input = page.Locator("[data-testid='dbc-palette-input']");

        await input.FillAsync("200");
        await Expect(page.Locator("[data-testid='dbc-palette-result'][data-kind='message']"))
            .ToContainTextAsync("Second");

        await input.FillAsync("0xC8");
        await Expect(page.Locator("[data-testid='dbc-palette-result'][data-kind='message']"))
            .ToContainTextAsync("Second");
    }

    private static async Task UploadAsync(
        IPage page,
        string fileName,
        string content,
        int timeoutMs = 30_000)
    {
        await page.SetInputFilesAsync(
            "input[type=file]",
            new FilePayload
            {
                Name = fileName,
                MimeType = "text/plain",
                Buffer = Encoding.UTF8.GetBytes(content),
            });

        var file = page.Locator("[data-testid='dbc-file']").First;
        await Expect(file).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = timeoutMs });
        await file.ClickAsync();
        await Expect(page.Locator("[data-testid='dbc-table']")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = timeoutMs });
    }
}

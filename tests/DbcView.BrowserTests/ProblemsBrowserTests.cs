using System.Text;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace DbcView.BrowserTests;

public sealed class ProblemsBrowserTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _fixture;

    public ProblemsBrowserTests(WebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Warnings_render_and_auto_check_reparses_unsaved_text()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await Expect(page.Locator("[data-testid='dbc-view-shell']")).ToBeVisibleAsync();

        await page.SetInputFilesAsync(
            "input[type=file]",
            new FilePayload
            {
                Name = "problems.dbc",
                MimeType = "text/plain",
                Buffer = Encoding.UTF8.GetBytes(
                    "VERSION \"\"\nBU_: XXX\n\nBO_ 100 M: 1 XXX\n" +
                    " SG_ TooWide : 7|2@0+ (1,0) [0|3] \"\" XXX\n"),
            });
        var file = page.Locator(
            "[data-testid='dbc-file'][data-file-key='DBC/problems.dbc']");
        await Expect(file).ToBeVisibleAsync();
        await file.ClickAsync();
        await Expect(page.Locator("[data-testid='dbc-table']")).ToBeVisibleAsync();

        await Expect(page.Locator("[data-testid='dbc-parse-warnings']")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-testid='dbc-problems-check']")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-testid='dbc-check-document']")).ToBeVisibleAsync();
        var row = page.Locator("[data-testid='dbc-problem-row']");
        await Expect(row).ToHaveCountAsync(1);
        await Expect(row).ToHaveAttributeAsync("data-severity", "warning");
        await Expect(page.Locator("[data-testid='dbc-problem-line']")).ToHaveTextAsync("5");
        await Expect(page.Locator("[data-testid='dbc-problem-code']")).ToHaveTextAsync("DBC006");
        await Expect(page.Locator("[data-testid='dbc-problem-message']"))
            .ToContainTextAsync("spans bits");

        await page.Locator("[data-testid='dbc-mode-text']").ClickAsync();
        await page.Locator("textarea").FillAsync("this is not a DBC file");
        await Expect(page.Locator("[data-testid='dbc-problem-code']").First)
            .ToHaveTextAsync("DBC001");
        await Expect(page.Locator("[data-testid='dbc-problem-code']").Nth(1))
            .ToHaveTextAsync("DBC002");
        await Expect(page.Locator("[data-testid='dbc-status-errors']"))
            .ToContainTextAsync("2 errors");

        await page.Keyboard.PressAsync("Control+Enter");
        await Expect(page.Locator("[data-testid='dbc-view-status-bar']"))
            .ToContainTextAsync("Checked DBC/problems.dbc");
    }
}

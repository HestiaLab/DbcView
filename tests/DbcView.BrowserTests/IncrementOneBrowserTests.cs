using System.Text;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace DbcView.BrowserTests;

public sealed class IncrementOneBrowserTests : IClassFixture<WebAppFixture>
{
    private const string SmallDbc =
        "VERSION \"\"\nBU_: XXX\n\n" +
        "BO_ 100 First: 2 XXX\n" +
        " SG_ A : 0|8@0+ (1,0) [0|255] \"\" XXX\n" +
        " SG_ B : 8|8@0+ (1,0) [0|255] \"\" XXX\n\n" +
        "BO_ 200 Second: 1 XXX\n" +
        " SG_ C : 0|8@0+ (1,0) [0|255] \"\" XXX\n";

    private readonly WebAppFixture _fixture;

    public IncrementOneBrowserTests(WebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Table_defaults_collapsed_and_supports_keyboard_navigation()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await UploadAsync(page, "small.dbc", SmallDbc);

        var table = page.Locator("[data-testid='dbc-table']");
        await Expect(table).ToBeVisibleAsync();
        await Expect(table).ToHaveAttributeAsync("aria-rowcount", "2");
        Assert.Equal(0, await page.Locator("[data-testid='dbc-signal']").CountAsync());
        Assert.Equal(2, await page.Locator("[data-testid='dbc-message']").CountAsync());

        await page.Locator("[data-testid='dbc-toggle-message']").First.ClickAsync();
        await Expect(table).ToHaveAttributeAsync("aria-rowcount", "4");
        Assert.Equal(2, await page.Locator("[data-testid='dbc-signal']").CountAsync());

        var firstMessage = page.Locator("[data-testid='dbc-message']").First;
        await firstMessage.FocusAsync();
        await firstMessage.PressAsync("ArrowDown");
        await Expect(page.Locator("[data-testid='dbc-signal']").First)
            .ToHaveAttributeAsync("tabindex", "0");

        await page.Locator("[data-testid='dbc-signal']").First.PressAsync("End");
        await Expect(page.Locator("[data-testid='dbc-message']").Last)
            .ToHaveAttributeAsync("tabindex", "0");

        await firstMessage.FocusAsync();
        await firstMessage.PressAsync("ArrowLeft");
        await Expect(table).ToHaveAttributeAsync("aria-rowcount", "2");

        await firstMessage.ClickAsync();
        await Expect(page.Locator("[data-testid='dbc-property']")).ToContainTextAsync("Message");
    }

    [Fact]
    public async Task Layout_keeps_the_table_reachable_at_narrow_and_wide_widths()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await UploadAsync(page, "layout.dbc", SmallDbc);

        var table = page.Locator("[data-testid='dbc-table']");
        foreach (var width in new[] { 320, 375, 620 })
        {
            await page.SetViewportSizeAsync(width, 800);
            await Expect(page.Locator("[data-testid='dbc-compact-nav']")).ToBeVisibleAsync();
            await Expect(table).ToBeVisibleAsync();
            await Expect(table).ToHaveAttributeAsync("aria-rowcount", "2");
        }

        await page.SetViewportSizeAsync(1440, 900);
        await Expect(page.Locator("[data-testid='dbc-compact-nav']")).ToBeHiddenAsync();
        await Expect(table).ToBeVisibleAsync();

        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = ColorScheme.Dark });
        await Expect(table).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Large_table_renders_a_bounded_window_and_scrolls_to_the_end()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await UploadAsync(page, "large.dbc", CreateLargeDbc(5000), timeoutMs: 90_000);

        var table = page.Locator("[data-testid='dbc-table']");
        await Expect(table).ToHaveAttributeAsync(
            "aria-rowcount",
            "5000",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 90_000 });
        Assert.InRange(
            await page.Locator("[data-testid='dbc-table'] [data-row-index]").CountAsync(),
            1,
            60);

        var scroll = page.Locator("[data-testid='dbc-table-scroll']");
        await scroll.EvaluateAsync("element => { element.scrollTop = element.scrollHeight; }");
        await Expect(page.Locator("[data-row-index='4999']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        Assert.InRange(await page.Locator("[data-row-index]").CountAsync(), 1, 60);

        await page.Locator("[data-testid='dbc-expand-all']").ClickAsync();
        await Expect(table).ToHaveAttributeAsync(
            "aria-rowcount",
            "15000",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 30_000 });
        await scroll.EvaluateAsync("element => { element.scrollTop = element.scrollHeight; }");
        await Expect(page.Locator("[data-row-index='14999']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        Assert.InRange(await page.Locator("[data-row-index]").CountAsync(), 1, 60);
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

    private static string CreateLargeDbc(int messageCount)
    {
        var builder = new StringBuilder(messageCount * 140);
        builder.AppendLine("VERSION \"\"");
        builder.AppendLine("BU_: XXX");
        builder.AppendLine();

        for (var index = 0; index < messageCount; index++)
        {
            builder.Append("BO_ ").Append(0x100 + index).Append(" Msg_").Append(index)
                .AppendLine(": 2 XXX");
            builder.Append(" SG_ A_").Append(index)
                .AppendLine(" : 0|8@0+ (1,0) [0|255] \"\" XXX");
            builder.Append(" SG_ B_").Append(index)
                .AppendLine(" : 8|8@0+ (1,0) [0|255] \"\" XXX");
        }

        return builder.ToString();
    }
}

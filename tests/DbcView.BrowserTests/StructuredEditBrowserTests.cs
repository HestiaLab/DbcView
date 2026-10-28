using System.Text;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace DbcView.BrowserTests;

public sealed class StructuredEditBrowserTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _fixture;

    public StructuredEditBrowserTests(WebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Comment_edit_previews_applies_and_reverts_all()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await UploadTextAsync(
            page,
            "edit-comment.dbc",
            File.ReadAllText(SamplePath("semantics-full.dbc")));

        await page.Locator("[data-row-key='m:0'] [data-testid='dbc-message']").ClickAsync();
        await page.Locator("[data-testid='dbc-edit-comment']").First.ClickAsync();

        var dialog = page.Locator("[data-testid='dbc-edit-dialog']");
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='dbc-edit-old']"))
            .ToContainTextAsync("Engine message comment");
        await page.Locator("[data-testid='dbc-edit-comment-text']")
            .FillAsync("Browser edited comment");
        await Expect(page.Locator("[data-testid='dbc-edit-preview']"))
            .ToContainTextAsync("CM_ BO_ 100 \"Browser edited comment\";");

        await page.Locator("[data-testid='dbc-edit-apply']").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(page.Locator("[data-testid='dbc-property-comments']"))
            .ToContainTextAsync("Browser edited comment");

        await page.Locator("[data-testid='dbc-mode-text']").ClickAsync();
        var textarea = page.Locator("textarea");
        await Expect(textarea).ToBeVisibleAsync();
        await Expect(textarea).ToHaveValueAsync(
            new System.Text.RegularExpressions.Regex("CM_ BO_ 100 \"Browser edited comment\";"));

        var revert = page.Locator("[data-testid='dbc-revert-all']");
        await Expect(revert).ToBeEnabledAsync();
        await revert.ClickAsync();
        await Expect(textarea).ToHaveValueAsync(
            new System.Text.RegularExpressions.Regex("CM_ BO_ 100 \"Engine message comment\";"));
        await Expect(page.Locator("[data-testid='dbc-property-comments']"))
            .ToContainTextAsync("Engine message comment");
    }

    [Fact]
    public async Task Referenced_value_table_edit_becomes_inline_and_keeps_the_global_table()
    {
        const string content =
            "VERSION \"\"\nBU_: XXX\n\nBO_ 100 M: 1 XXX\n" +
            " SG_ S : 0|8@0+ (1,0) [0|255] \"\" XXX\n" +
            "VAL_TABLE_ Shared 0 \"Off\" 1 \"On\";\n" +
            "VAL_ 100 S VAL_TABLE_ Shared;\n";
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await UploadTextAsync(page, "edit-value-table.dbc", content);

        await page.Locator("[data-row-key='m:0'] [data-testid='dbc-toggle-message']").ClickAsync();
        await page.Locator("[data-row-key='s:0:0'] [data-testid='dbc-signal']").ClickAsync();
        await page.Locator("[data-testid='dbc-edit-value-table']").ClickAsync();

        var dialog = page.Locator("[data-testid='dbc-edit-dialog']");
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='dbc-edit-inline']")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='dbc-edit-preview']"))
            .ToContainTextAsync("VAL_ 100 S 0 \"Off\" 1 \"On\";");

        await page.Locator("[data-testid='dbc-edit-apply']").ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await page.Locator("[data-testid='dbc-mode-text']").ClickAsync();

        var value = await page.Locator("textarea").InputValueAsync();
        Assert.Contains("VAL_ 100 S 0 \"Off\" 1 \"On\";", value, StringComparison.Ordinal);
        Assert.Contains("VAL_TABLE_ Shared 0 \"Off\" 1 \"On\";", value, StringComparison.Ordinal);
        Assert.DoesNotContain("VAL_ 100 S VAL_TABLE_ Shared;", value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Download_writes_utf8_reviewed_file()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await UploadTextAsync(
            page,
            "download.dbc",
            File.ReadAllText(SamplePath("semantics-full.dbc")));

        var downloadTask = page.WaitForDownloadAsync();
        await page.Locator("[data-testid='dbc-download']").ClickAsync();
        var download = await downloadTask;

        Assert.Equal("download.dbc.reviewed.dbc", download.SuggestedFilename);
        var path = await download.PathAsync();
        Assert.NotNull(path);
        var bytes = await File.ReadAllBytesAsync(path!);
        Assert.False(
            bytes.Length >= 3
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("VERSION \"semantics-full\"", text, StringComparison.Ordinal);
        await Expect(page.Locator("[data-testid='dbc-download-notice']"))
            .ToContainTextAsync("UTF-8");
    }

    [Fact]
    public async Task Legacy_encoding_download_reports_conversion()
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

        var downloadTask = page.WaitForDownloadAsync();
        await page.Locator("[data-testid='dbc-download']").ClickAsync();
        var download = await downloadTask;
        var path = await download.PathAsync();
        Assert.NotNull(path);
        var bytes = await File.ReadAllBytesAsync(path!);
        Assert.False(
            bytes.Length >= 3
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("café", text, StringComparison.Ordinal);
        Assert.Contains("\uFFFD", text, StringComparison.Ordinal);
        await Expect(page.Locator("[data-testid='dbc-download-notice']"))
            .ToContainTextAsync("converted to UTF-8");
    }

    [Fact]
    public async Task Invalid_text_blocks_download_and_dirty_text_disables_editing()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await UploadTextAsync(
            page,
            "invalid-download.dbc",
            File.ReadAllText(SamplePath("semantics-full.dbc")));

        await page.Locator("[data-row-key='m:0'] [data-testid='dbc-message']").ClickAsync();
        await page.Locator("[data-testid='dbc-mode-text']").ClickAsync();
        await page.Locator("textarea").FillAsync("not a dbc");

        await Expect(page.Locator("[data-testid='dbc-edit-disabled']")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='dbc-edit-comment']").First)
            .ToBeDisabledAsync();

        await page.Locator("[data-testid='dbc-download']").ClickAsync();
        await Expect(page.Locator("[data-testid='dbc-download-error']"))
            .ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='dbc-download-error']"))
            .ToContainTextAsync("blocked");
    }

    private static string SamplePath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Samples", fileName);
    }

    private static async Task UploadTextAsync(IPage page, string fileName, string content)
    {
        await UploadBytesAsync(page, fileName, Encoding.UTF8.GetBytes(content));
    }

    private static async Task UploadBytesAsync(IPage page, string fileName, byte[] content)
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

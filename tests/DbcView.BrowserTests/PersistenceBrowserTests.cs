using System.Text;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace DbcView.BrowserTests;

public sealed class PersistenceBrowserTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _fixture;

    public PersistenceBrowserTests(WebAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Default_session_is_off_and_writes_no_database()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await Expect(page.Locator("[data-testid='dbc-view-shell']")).ToBeVisibleAsync();

        var databases = await page.EvaluateAsync<string[]>(
            "async () => (await indexedDB.databases()).map(database => database.name)");

        Assert.DoesNotContain("dbcview-session", databases);
        Assert.Equal(0, await page.Locator("[data-testid='dbc-persistence-status']").CountAsync());
    }

    [Fact]
    public async Task Enabled_session_restores_files_tabs_modes_and_current_text()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        var content = File.ReadAllText(SamplePath("semantics-full.dbc"));
        await UploadAndOpenAsync(page, "persist-a.dbc", content);
        await UploadAndOpenAsync(page, "persist-b.dbc", content);

        await page.Locator("[data-row-key='m:0'] [data-testid='dbc-message']").ClickAsync();
        await page.Locator("[data-testid='dbc-edit-comment']").First.ClickAsync();
        await page.Locator("[data-testid='dbc-edit-comment-text']").FillAsync("Persisted edit");
        await page.Locator("[data-testid='dbc-edit-apply']").ClickAsync();
        await Expect(page.Locator("[data-testid='dbc-edit-dialog']")).ToBeHiddenAsync();
        await page.Locator("[data-testid='dbc-mode-text']").ClickAsync();

        await page.Locator("[data-testid='dbc-persistence']").ClickAsync();
        await page.Locator("[data-testid='dbc-persistence-enable']").ClickAsync();
        await Expect(page.Locator("[data-testid='dbc-persistence-status']"))
            .ToContainTextAsync("Session saved");

        await page.ReloadAsync();

        await Expect(page.Locator("[data-testid='dbc-persistence-notice']"))
            .ToContainTextAsync("Restored local session");
        await Expect(page.Locator("[data-testid='dbc-file']")).ToHaveCountAsync(2);
        await Expect(page.Locator("[data-atlas-group-kind='document'] [role='tab']"))
            .ToHaveCountAsync(2);
        await Expect(page.Locator("[data-testid='dbc-status-document']"))
            .ToContainTextAsync("DBC/persist-b.dbc");
        await Expect(page.Locator("[data-testid='dbc-mode-text']"))
            .ToHaveAttributeAsync("aria-pressed", "true");
        var text = await page.Locator("textarea").InputValueAsync();
        Assert.Contains("CM_ BO_ 100 \"Persisted edit\";", text, StringComparison.Ordinal);
        await Expect(page.Locator("[data-testid='dbc-persistence-status']"))
            .ToContainTextAsync("Session saved");
    }

    [Fact]
    public async Task Forget_clears_the_local_session_and_reload_starts_empty()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await UploadAndOpenAsync(
            page,
            "forget.dbc",
            "VERSION \"\"\nBU_: XXX\n\nBO_ 100 M: 1 XXX\n");

        await page.Locator("[data-testid='dbc-persistence']").ClickAsync();
        await page.Locator("[data-testid='dbc-persistence-enable']").ClickAsync();
        await Expect(page.Locator("[data-testid='dbc-persistence-status']"))
            .ToContainTextAsync("Session saved");

        await page.Locator("[data-testid='dbc-persistence']").ClickAsync();
        await page.Locator("[data-testid='dbc-persistence-forget']").ClickAsync();
        await Expect(page.Locator("[data-testid='dbc-persistence-notice']"))
            .ToContainTextAsync("removed");
        Assert.Equal(0, await page.Locator("[data-testid='dbc-persistence-status']").CountAsync());

        await page.ReloadAsync();
        await Expect(page.Locator("[data-testid='dbc-view-shell']")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid='dbc-file']")).ToHaveCountAsync(0);
        Assert.Equal(0, await page.Locator("[data-testid='dbc-persistence-notice']").CountAsync());
    }

    [Fact]
    public async Task Unsupported_schema_is_discarded_with_a_visible_message()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await WriteSnapshotAsync(page, "{\"schemaVersion\":99}");

        await page.ReloadAsync();

        await Expect(page.Locator("[data-testid='dbc-persistence-notice']"))
            .ToContainTextAsync("unsupported schema");
        await Expect(page.Locator("[data-testid='dbc-file']")).ToHaveCountAsync(0);
        Assert.Equal(0, await page.Locator("[data-testid='dbc-persistence-status']").CountAsync());
    }

    [Fact]
    public async Task Corrupt_json_is_discarded_with_a_visible_message()
    {
        await using var context = await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        await WriteSnapshotAsync(page, "{not valid json");

        await page.ReloadAsync();

        await Expect(page.Locator("[data-testid='dbc-persistence-notice']"))
            .ToContainTextAsync("corrupt");
        await Expect(page.Locator("[data-testid='dbc-file']")).ToHaveCountAsync(0);
    }

    private static string SamplePath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Samples", fileName);
    }

    private static async Task UploadAndOpenAsync(IPage page, string fileName, string content)
    {
        await page.SetInputFilesAsync(
            "input[type=file]",
            new FilePayload
            {
                Name = fileName,
                MimeType = "text/plain",
                Buffer = Encoding.UTF8.GetBytes(content),
            });
        var file = page.Locator(
            $"[data-testid='dbc-file'][data-file-key='DBC/{fileName}']");
        await Expect(file).ToBeVisibleAsync();
        await file.ClickAsync();
        await Expect(page.Locator("[data-testid='dbc-mode-text']")).ToBeVisibleAsync();
    }

    private static async Task WriteSnapshotAsync(IPage page, string json)
    {
        await page.EvaluateAsync(
            """
            async json => {
                await new Promise((resolve, reject) => {
                    const request = indexedDB.open("dbcview-session", 1);
                    request.onupgradeneeded = () => {
                        if (!request.result.objectStoreNames.contains("workspace")) {
                            request.result.createObjectStore("workspace");
                        }
                    };
                    request.onsuccess = () => {
                        const db = request.result;
                        const transaction = db.transaction("workspace", "readwrite");
                        transaction.objectStore("workspace").put(json, "snapshot");
                        transaction.oncomplete = () => resolve(true);
                        transaction.onerror = () => reject(transaction.error);
                    };
                    request.onerror = () => reject(request.error);
                });
            }
            """,
            json);
    }
}

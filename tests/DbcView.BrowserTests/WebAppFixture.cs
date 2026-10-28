using System.Diagnostics;
using Microsoft.Playwright;
using Xunit;

namespace DbcView.BrowserTests;

public sealed class WebAppFixture : IAsyncLifetime
{
    private readonly List<string> _serverOutput = new();
    private readonly bool _useExternalServer;
    private Process? _server;
    private IPlaywright? _playwright;

    public WebAppFixture()
    {
        var external = Environment.GetEnvironmentVariable("DBCVIEW_BASE_URL");
        if (!string.IsNullOrWhiteSpace(external))
        {
            BaseUrl = external.TrimEnd('/');
            _useExternalServer = true;
        }
        else
        {
            BaseUrl = "http://127.0.0.1:5210";
        }
    }

    public string BaseUrl { get; }

    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (!_useExternalServer)
        {
            _server = StartServer();
        }

        await WaitForServerAsync();
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        _playwright?.Dispose();

        if (_server is { HasExited: false })
        {
            try
            {
                _server.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            await _server.WaitForExitAsync();
        }

        _server?.Dispose();
    }

    private Process StartServer()
    {
        var root = FindRepositoryRoot();
        var webProject = Path.Combine(root, "src", "DbcView.Web", "DbcView.Web.csproj");
        var startInfo = new ProcessStartInfo(
            "dotnet",
            $"run --project \"{webProject}\" -c Release --no-launch-profile --urls {BaseUrl}")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start DbcView.Web.");
        process.OutputDataReceived += (_, args) => Capture(args.Data);
        process.ErrorDataReceived += (_, args) => Capture(args.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private void Capture(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_serverOutput)
        {
            _serverOutput.Add(line);
        }
    }

    private async Task WaitForServerAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (_server?.HasExited == true)
            {
                throw new InvalidOperationException(
                    "DbcView.Web exited before it became reachable:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, SnapshotOutput()));
            }

            try
            {
                using var response = await client.GetAsync(BaseUrl);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(500);
        }

        throw new TimeoutException(
            "DbcView.Web did not become reachable:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, SnapshotOutput()));
    }

    private string[] SnapshotOutput()
    {
        lock (_serverOutput)
        {
            return _serverOutput.TakeLast(20).ToArray();
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DbcView.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate DbcView.sln.");
    }
}

#nullable enable
using System.Text.RegularExpressions;
using Xunit;

namespace DbcView.Tests;

/// <summary>
/// Static policy checks for the release and board workflows. These run with
/// the normal test suite, so removing a gate, widening permissions, or
/// unpinning an Action fails locally before it reaches GitHub.
/// </summary>
public sealed class WorkflowPolicyTests
{
    [Fact]
    public void All_actions_are_pinned_to_full_commit_sha_with_version_comment()
    {
        foreach (var path in Directory.EnumerateFiles(WorkflowDirectory(), "*.yml"))
        {
            var text = File.ReadAllText(path);
            var uses = Regex.Matches(text, @"uses:\s*(?<ref>[^\s#]+)");
            Assert.NotEmpty(uses);

            foreach (Match use in uses)
            {
                var reference = use.Groups["ref"].Value;
                var parts = reference.Split('@');
                Assert.Equal(2, parts.Length);
                Assert.Matches("^[0-9a-f]{40}$", parts[1]);

                var lineEnd = text.IndexOf('\n', use.Index);
                var line = text[use.Index..(lineEnd < 0 ? text.Length : lineEnd)];
                Assert.Contains("# v", line, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Deploy_workflow_requires_verification_before_publish_and_deploy()
    {
        var text = ReadWorkflow("deploy.yml");
        var verify = JobIndex(text, "verify");
        var build = JobIndex(text, "build");
        var deploy = JobIndex(text, "deploy");

        Assert.True(verify >= 0);
        Assert.True(build > verify);
        Assert.True(deploy > build);

        var verifyBlock = text[verify..build];
        Assert.Contains("dotnet test DbcView.sln", verifyBlock, StringComparison.Ordinal);
        Assert.Contains("dotnet format DbcView.sln --verify-no-changes", verifyBlock, StringComparison.Ordinal);

        var buildBlock = text[build..deploy];
        Assert.Contains("needs: verify", buildBlock, StringComparison.Ordinal);

        var deployBlock = text[deploy..];
        Assert.Contains("needs: build", deployBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Deploy_permissions_are_scoped_to_the_deploy_job()
    {
        var text = ReadWorkflow("deploy.yml");
        var jobs = text.IndexOf("jobs:", StringComparison.Ordinal);
        Assert.True(jobs > 0);
        var topLevel = text[..jobs];
        Assert.Contains("permissions:", topLevel, StringComparison.Ordinal);
        Assert.Contains("contents: read", topLevel, StringComparison.Ordinal);
        Assert.DoesNotContain("pages: write", topLevel, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token: write", topLevel, StringComparison.Ordinal);

        var build = JobIndex(text, "build");
        var deploy = JobIndex(text, "deploy");
        var buildBlock = text[build..deploy];
        Assert.DoesNotContain("pages: write", buildBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token: write", buildBlock, StringComparison.Ordinal);

        var deployBlock = text[deploy..];
        Assert.Contains("pages: write", deployBlock, StringComparison.Ordinal);
        Assert.Contains("id-token: write", deployBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Base_href_rewrite_and_published_shell_are_asserted()
    {
        var text = ReadWorkflow("deploy.yml");

        Assert.Contains(
            "grep -qF '<base href=\"/DbcView/\"'",
            text,
            StringComparison.Ordinal);
        Assert.Contains("_framework/blazor.webassembly.js", text, StringComparison.Ordinal);
        Assert.Contains("_content/Atlas.Blazor/atlas-v2/atlas.css", text, StringComparison.Ordinal);
        Assert.Contains("exit 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Board_automation_skips_fork_pull_requests_without_exposing_pat()
    {
        var text = ReadWorkflow("add-to-project.yml");

        Assert.Contains("permissions:", text, StringComparison.Ordinal);
        Assert.Contains("contents: read", text, StringComparison.Ordinal);
        Assert.Contains(
            "github.event.pull_request.head.repo.full_name == github.repository",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request_target", text, StringComparison.Ordinal);
        Assert.DoesNotContain("echo", text.ToLowerInvariant(), StringComparison.Ordinal);
    }

    [Fact]
    public void CI_collects_coverage_on_one_os_and_uploads_an_artifact()
    {
        var text = ReadWorkflow("ci.yml");

        Assert.Contains("XPlat Code Coverage", text, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@", text, StringComparison.Ordinal);
        Assert.Contains("if: matrix.os == 'ubuntu-latest'", text, StringComparison.Ordinal);
        Assert.Contains("if-no-files-found: error", text, StringComparison.Ordinal);
        Assert.Contains("coverage.cobertura.xml", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Index_declares_a_favicon_that_exists()
    {
        var root = FindRepositoryRoot();
        var index = File.ReadAllText(
            Path.Combine(root, "src", "DbcView", "wwwroot", "index.html"));

        Assert.Contains("rel=\"icon\"", index, StringComparison.Ordinal);
        Assert.Contains("href=\"favicon.svg\"", index, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "src", "DbcView", "wwwroot", "favicon.svg")));
    }

    private static int JobIndex(string text, string job)
    {
        return text.IndexOf($"\n  {job}:\n", StringComparison.Ordinal);
    }

    private static string ReadWorkflow(string name)
    {
        return File.ReadAllText(Path.Combine(WorkflowDirectory(), name));
    }

    private static string WorkflowDirectory()
    {
        return Path.Combine(FindRepositoryRoot(), ".github", "workflows");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DbcView.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("DbcView.sln was not found.");
    }
}

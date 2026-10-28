#nullable enable
using System.Xml.Linq;
using Xunit;

namespace DbcView.Tests;

public sealed class DemoPackageBoundaryTests
{
    private static readonly string[] ControlledTopLevelDirectories = { "src", "tests" };

    private static readonly Dictionary<string, string[]> AllowedProjectReferences = new(StringComparer.Ordinal)
    {
        ["DbcView.Domain"] = Array.Empty<string>(),
        ["DbcView.Application"] = new[] { "DbcView.Domain" },
        ["DbcView.Infrastructure"] = new[] { "DbcView.Domain", "DbcView.Application" },
        ["DbcView"] = new[] { "DbcView.Domain", "DbcView.Application", "DbcView.Infrastructure" },
        ["DbcView.Tests"] = new[]
        {
            "DbcView.Domain",
            "DbcView.Application",
            "DbcView.Infrastructure",
            "DbcView",
        },
    };

    [Fact]
    public void Product_projects_consume_only_the_published_atlas_package()
    {
        var root = FindRepositoryRoot();

        var productProjects = ControlledProjects(root)
            .Where(path => !path.EndsWith("DbcView.Tests.csproj", StringComparison.Ordinal))
            .ToArray();

        foreach (var projectPath in productProjects)
        {
            var document = XDocument.Load(projectPath);
            var packages = PackageReferences(document);
            var name = Path.GetFileNameWithoutExtension(projectPath);

            Assert.DoesNotContain("Atlas.Core", packages);
            Assert.DoesNotContain(
                packages,
                package => package.StartsWith("Atlas.", StringComparison.Ordinal)
                    && package != "Atlas.Blazor");

            if (name == "DbcView")
            {
                Assert.Contains("Atlas.Blazor", packages);
            }
            else
            {
                Assert.Empty(packages);
            }
        }
    }

    [Fact]
    public void Project_references_follow_the_inward_dependency_direction()
    {
        var root = FindRepositoryRoot();
        var projects = ControlledProjects(root);

        foreach (var projectPath in projects)
        {
            var name = Path.GetFileNameWithoutExtension(projectPath);
            Assert.True(
                AllowedProjectReferences.ContainsKey(name),
                $"Unexpected project '{name}' outside the controlled architecture.");

            var references = ProjectReferences(projectPath).ToArray();
            Assert.All(
                references,
                reference =>
                {
                    var target = Path.GetFileNameWithoutExtension(reference);
                    Assert.True(
                        AllowedProjectReferences.ContainsKey(target),
                        $"{name} references unknown project '{target}'.");
                    Assert.Contains(target, AllowedProjectReferences[name]);
                });
        }

        Assert.Empty(ProjectReferences(Path.Combine(root, "src", "DbcView.Domain", "DbcView.Domain.csproj")));
        Assert.Equal(
            new[] { "DbcView.Domain" },
            ProjectReferences(Path.Combine(root, "src", "DbcView.Application", "DbcView.Application.csproj"))
                .Select(Path.GetFileNameWithoutExtension));
    }

    [Fact]
    public void Domain_and_application_are_free_of_outer_framework_packages()
    {
        var root = FindRepositoryRoot();

        foreach (var name in new[] { "DbcView.Domain", "DbcView.Application" })
        {
            var document = XDocument.Load(FindProject(root, name));
            Assert.Empty(PackageReferences(document));
            Assert.Empty(document.Descendants("FrameworkReference"));
        }
    }

    [Fact]
    public void Repository_does_not_link_atlas_sources_or_use_binary_hint_paths()
    {
        var root = FindRepositoryRoot();

        foreach (var projectPath in ControlledProjects(root))
        {
            var document = XDocument.Load(projectPath);
            var name = Path.GetFileNameWithoutExtension(projectPath);

            Assert.DoesNotContain(
                document.Descendants(),
                element => element.Name.LocalName == "HintPath");
            Assert.DoesNotContain(
                document.Descendants(),
                element => element.Name.LocalName == "Compile"
                    && element.Attribute("Include") is not null);

            var linkedAtlas = document.Descendants()
                .Where(element => element.Name.LocalName is "Compile" or "Link"
                    || element.Name.LocalName == "None")
                .SelectMany(element => element.Attributes("Include").Concat(element.Attributes("Link")))
                .Any(attribute => attribute.Value.Contains("Atlas", StringComparison.OrdinalIgnoreCase));
            Assert.False(linkedAtlas, $"{name} links an Atlas source path.");
        }
    }

    [Fact]
    public void Portable_source_has_no_repository_coupling()
    {
        var root = FindRepositoryRoot();
        var forbidden = new[]
        {
            "src" + "/Atlas.Core",
            "src" + "\\Atlas.Core",
            "src" + "/Atlas.Blazor",
            "src" + "\\Atlas.Blazor",
            "C:" + "\\Users\\World\\source\\repos\\Atlas",
            "<Compile" + " Include=\"",
        };

        foreach (var path in ControlledTextFiles(root))
        {
            var text = File.ReadAllText(path);
            foreach (var value in forbidden)
            {
                Assert.False(
                    text.Contains(value, StringComparison.OrdinalIgnoreCase),
                    $"{Path.GetRelativePath(root, path)} contains forbidden repository coupling '{value}'.");
            }
        }
    }

    [Fact]
    public void Boundary_scan_ignores_unrelated_temporary_projects()
    {
        var root = FindRepositoryRoot();
        var temporaryDirectory = Path.Combine(root, ".tmp-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryProject = Path.Combine(temporaryDirectory, "Unrelated.csproj");
        File.WriteAllText(
            temporaryProject,
            "<Project><ItemGroup><ProjectReference Include=\"../../"
            + "src" + "/Atlas.Core/Atlas.Core.csproj\" /></ItemGroup></Project>");

        try
        {
            var projects = ControlledProjects(root);
            var sourcePrefix = Path.Combine(root, "src") + Path.DirectorySeparatorChar;
            var testPrefix = Path.Combine(root, "tests") + Path.DirectorySeparatorChar;

            Assert.DoesNotContain(
                projects,
                path => path.Contains(temporaryDirectory, StringComparison.OrdinalIgnoreCase));
            Assert.All(
                projects,
                path => Assert.True(
                    path.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(testPrefix, StringComparison.OrdinalIgnoreCase),
                    $"Unexpected project outside controlled roots: {path}"));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void Test_project_uses_project_references_instead_of_binary_hint_paths()
    {
        var root = FindRepositoryRoot();
        var testProject = FindProject(root, "DbcView.Tests");
        var document = XDocument.Load(testProject);

        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Name.LocalName == "Reference");
        var references = ProjectReferences(testProject)
            .Select(Path.GetFileNameWithoutExtension)
            .ToArray();
        Assert.Contains("DbcView", references);
        Assert.Contains("DbcView.Domain", references);
        Assert.Contains("DbcView.Application", references);
        Assert.Contains("DbcView.Infrastructure", references);
    }

    private static string[] PackageReferences(XDocument document)
    {
        return document.Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();
    }

    private static IEnumerable<string> ProjectReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        return document.Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(value => value is not null)
            .Select(value => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, value!)))
            .ToArray();
    }

    private static string[] ControlledProjects(string root)
    {
        var projects = new List<string>();
        foreach (var topLevel in ControlledTopLevelDirectories)
        {
            var directory = Path.Combine(root, topLevel);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            projects.AddRange(
                Directory.EnumerateDirectories(directory)
                    .Where(projectDirectory => !HasGeneratedSegment(root, projectDirectory))
                    .SelectMany(projectDirectory =>
                        Directory.EnumerateFiles(projectDirectory, "*.csproj", SearchOption.TopDirectoryOnly)));
        }

        return projects
            .Where(path => !HasGeneratedSegment(root, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> ControlledTextFiles(string root)
    {
        foreach (var topLevel in ControlledTopLevelDirectories)
        {
            var directory = Path.Combine(root, topLevel);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (!HasGeneratedSegment(root, path) && IsTextFile(path))
                {
                    yield return path;
                }
            }
        }
    }

    private static string FindProject(string root, string name)
    {
        var project = ControlledProjects(root)
            .SingleOrDefault(path =>
                string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.Ordinal));
        return project ?? throw new FileNotFoundException($"Project '{name}' was not found.");
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

    private static bool HasGeneratedSegment(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".packages", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".vs", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("TestResults", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".git", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTextFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is
            ".cs" or ".csproj" or ".props" or ".razor" or ".css" or ".html" or ".md" or ".config" or ".sln" or ".json";
    }
}

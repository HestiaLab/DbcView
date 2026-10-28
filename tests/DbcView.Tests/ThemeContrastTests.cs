#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace DbcView.Tests;

/// <summary>
/// Guards the light/dark token layer against WCAG AA regressions:
/// body text >= 4.5:1, UI indicators and focus rings >= 3:1.
/// </summary>
public sealed class ThemeContrastTests
{
    private static readonly (string Foreground, string Background, double Minimum)[] TextPairs =
    {
        ("text", "surface", 4.5),
        ("text", "bg", 4.5),
        ("text", "chrome", 4.5),
        ("text", "selection", 4.5),
        ("text-secondary", "surface", 4.5),
        ("text-muted", "surface", 4.5),
        ("on-accent", "accent", 4.5),
        ("on-accent", "accent-strong", 4.5),
        ("danger-text", "danger-bg", 4.5),
        ("warning-text", "warning-bg", 4.5),
        ("success-text", "success-bg", 4.5),
    };

    private static readonly (string Foreground, string Background, double Minimum)[] IndicatorPairs =
    {
        ("accent", "surface", 3.0),
        ("accent", "chrome", 3.0),
        ("accent", "bg", 3.0),
        ("focus", "surface", 3.0),
        ("focus", "chrome", 3.0),
        ("focus", "bg", 3.0),
        ("danger", "surface", 3.0),
        ("warning", "surface", 3.0),
        ("success", "surface", 3.0),
    };

    [Fact]
    public void Light_theme_tokens_meet_wcag_aa()
    {
        var tokens = ReadTokens(ReadStylesheet(), dark: false);

        AssertPairs(tokens, TextPairs);
        AssertPairs(tokens, IndicatorPairs);
    }

    [Fact]
    public void Dark_theme_tokens_meet_wcag_aa()
    {
        var tokens = ReadTokens(ReadStylesheet(), dark: true);

        AssertPairs(tokens, TextPairs);
        AssertPairs(tokens, IndicatorPairs);
    }

    [Fact]
    public void Theme_exposes_every_required_semantic_token()
    {
        foreach (var dark in new[] { false, true })
        {
            var tokens = ReadTokens(ReadStylesheet(), dark);

            var required = new[]
            {
                "bg", "surface", "chrome", "sunken",
                "text", "text-secondary", "text-muted", "text-disabled",
                "border", "divider",
                "accent", "accent-strong", "on-accent",
                "hover", "selection", "focus",
                "danger", "danger-bg", "danger-text",
                "warning", "warning-bg", "warning-text",
                "success", "success-bg", "success-text",
            };

            foreach (var name in required)
            {
                Assert.True(tokens.ContainsKey(name), $"Missing token --dv-{name} (dark: {dark}).");
            }
        }
    }

    [Fact]
    public void Interactive_rules_use_the_contrast_checked_tokens()
    {
        var css = ReadStylesheet();

        Assert.Contains("background: var(--dv-selection)", css, StringComparison.Ordinal);
        Assert.Contains("box-shadow: inset 2px 0 0 var(--dv-accent)", css, StringComparison.Ordinal);
        Assert.Contains("background: var(--dv-accent)", css, StringComparison.Ordinal);
        Assert.Contains("color: var(--dv-on-accent)", css, StringComparison.Ordinal);
        Assert.Contains("outline: 2px solid var(--dv-focus)", css, StringComparison.Ordinal);
        Assert.Contains(".dbc-upload:focus-within", css, StringComparison.Ordinal);
        Assert.Contains(":focus-visible", css, StringComparison.Ordinal);
        Assert.DoesNotContain("--demo-", css, StringComparison.Ordinal);
    }

    private static void AssertPairs(
        IReadOnlyDictionary<string, string> tokens,
        IEnumerable<(string Foreground, string Background, double Minimum)> pairs)
    {
        foreach (var (foreground, background, minimum) in pairs)
        {
            var ratio = Contrast(tokens[foreground], tokens[background]);
            Assert.True(
                ratio >= minimum,
                $"Contrast {foreground}/{background} = {ratio:F2}, expected >= {minimum:F2}.");
        }
    }

    private static IReadOnlyDictionary<string, string> ReadTokens(string css, bool dark)
    {
        var darkIndex = css.IndexOf(
            "@media (prefers-color-scheme: dark) {",
            StringComparison.Ordinal);
        Assert.True(darkIndex > 0);

        var tokens = ParseTokens(css[..darkIndex]);
        if (dark)
        {
            foreach (var pair in ParseTokens(css[darkIndex..]))
            {
                tokens[pair.Key] = pair.Value;
            }
        }

        foreach (var (foreground, background, _) in TextPairs.Concat(IndicatorPairs))
        {
            Assert.True(tokens.ContainsKey(foreground), $"Missing token --dv-{foreground}.");
            Assert.True(tokens.ContainsKey(background), $"Missing token --dv-{background}.");
        }

        return tokens;
    }

    private static Dictionary<string, string> ParseTokens(string css)
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     css,
                     @"--dv-(?<name>[a-z-]+):\s*(?<value>#[0-9a-fA-F]{6})\s*;"))
        {
            tokens[match.Groups["name"].Value] = match.Groups["value"].Value;
        }

        return tokens;
    }

    private static double Contrast(string first, string second)
    {
        var lighter = Math.Max(Luminance(first), Luminance(second));
        var darker = Math.Min(Luminance(first), Luminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(string hex)
    {
        var value = int.Parse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var red = Channel(((value >> 16) & 0xFF) / 255.0);
        var green = Channel(((value >> 8) & 0xFF) / 255.0);
        var blue = Channel((value & 0xFF) / 255.0);
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private static double Channel(double value)
    {
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static string ReadStylesheet()
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(
            Path.Combine(root, "src", "DbcView.Web", "wwwroot", "css", "app.css"));
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

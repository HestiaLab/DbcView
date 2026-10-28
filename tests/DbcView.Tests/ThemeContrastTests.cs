#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace DbcView.Tests;

/// <summary>
/// Guards the light/dark palette tokens against WCAG AA regressions:
/// normal text >= 4.5:1, UI indicators >= 3:1.
/// </summary>
public sealed class ThemeContrastTests
{
    private static readonly (string Foreground, string Background, double Minimum)[] TextPairs =
    {
        ("accent", "accent-ink", 4.5),
        ("blue", "blue-ink", 4.5),
        ("selection", "selection-ink", 4.5),
        ("ink", "surface", 4.5),
        ("muted", "surface", 4.5),
        ("chrome-ink", "chrome", 4.5),
        ("error-ink", "error-bg", 4.5),
        ("warning-ink", "warning-bg", 4.5),
        ("success-ink", "success-bg", 4.5),
    };

    private static readonly (string Foreground, string Background, double Minimum)[] IndicatorPairs =
    {
        ("ok-ink", "chrome", 3.0),
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
    public void Interactive_rules_use_the_contrast_checked_tokens()
    {
        var css = ReadStylesheet();

        Assert.Contains("color: var(--demo-selection-ink)", css, StringComparison.Ordinal);
        Assert.Contains("background: var(--demo-selection)", css, StringComparison.Ordinal);
        Assert.Contains("background: var(--demo-blue)", css, StringComparison.Ordinal);
        Assert.Contains("color: var(--demo-blue-ink)", css, StringComparison.Ordinal);
        Assert.Contains(".dbc-upload:focus-within", css, StringComparison.Ordinal);
        Assert.Contains(":focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("outline: 3px solid currentColor", css, StringComparison.Ordinal);
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
            Assert.True(tokens.ContainsKey(foreground), $"Missing token --demo-{foreground}.");
            Assert.True(tokens.ContainsKey(background), $"Missing token --demo-{background}.");
        }

        return tokens;
    }

    private static Dictionary<string, string> ParseTokens(string css)
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     css,
                     @"--demo-(?<name>[a-z-]+):\s*(?<value>#[0-9a-fA-F]{6})\s*;"))
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

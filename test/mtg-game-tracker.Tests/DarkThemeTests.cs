using System.Text.RegularExpressions;
using Xunit;

namespace mtg_game_tracker.Tests;

public class DarkThemeTests
{
    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    private static string Css() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "wwwroot", "css", "site.css"));

    private static string Variable(string css, string name) =>
        Regex.Match(css, Regex.Escape(name) + @":\s*([^;]+);").Groups[1].Value.Trim();

    // Relative luminance approximation (0-255) of a #rrggbb or rgba() colour.
    private static double Brightness(string colour)
    {
        if (colour.StartsWith('#'))
        {
            var hex = colour[1..];
            return (Convert.ToInt32(hex[..2], 16) + Convert.ToInt32(hex[2..4], 16) + Convert.ToInt32(hex[4..6], 16)) / 3.0;
        }

        var parts = Regex.Match(colour, @"rgba?\(\s*(\d+),\s*(\d+),\s*(\d+)").Groups;
        return (int.Parse(parts[1].Value) + int.Parse(parts[2].Value) + int.Parse(parts[3].Value)) / 3.0;
    }

    [Fact]
    public void Layout_OptsIntoBootstrapDarkMode()
    {
        var layout = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Pages", "Shared", "_Layout.cshtml"));

        Assert.Matches(@"<html[^>]*data-bs-theme=""dark""", layout);
    }

    [Theory]
    [InlineData("--body-surface")]
    [InlineData("--body-surface-strong")]
    public void Stylesheet_SurfacesAreDark(string variable)
    {
        Assert.True(Brightness(Variable(Css(), variable)) < 80);
    }

    [Fact]
    public void Stylesheet_BodyTextIsLight()
    {
        var css = Css();

        Assert.True(Brightness(Variable(css, "--text-body")) > 170);
        Assert.True(Brightness(Variable(css, "--text-muted")) > 120);
    }

    [Fact]
    public void Stylesheet_DoesNotUseLightBackgroundsForTablesAndTracks()
    {
        var css = Css();

        Assert.DoesNotContain("#f8f9fc", css);
        Assert.DoesNotContain("#eaecf4", css);
    }

    [Fact]
    public void Stylesheet_RemovesBootstrapBordersAndRoundedCorners()
    {
        var css = Css();

        Assert.Equal("0", Variable(css, "--bs-border-width"));
        Assert.Equal("0", Variable(css, "--bs-border-radius"));
        Assert.Equal("0", Variable(css, "--bs-border-radius-lg"));
        Assert.Equal("0", Variable(css, "--bs-border-radius-pill"));
    }

    [Fact]
    public void Stylesheet_DeclaresNoBordersOrRadiiOfItsOwn()
    {
        var css = Css();

        Assert.DoesNotMatch(@"[{;\s]border(-(top|bottom|left|right))?:\s*(?!\s|0\b|none)", css);
        // The matchup card image keeps the radius of a real card's corners.
        var radii = Regex.Matches(css, @"([^{}]*)\{[^}]*(?<![-\w])border-radius:\s*(?!\s|0\b)[^;]+;");
        Assert.All(radii, m => Assert.Contains(".matchup-card", m.Groups[1].Value));
    }

    [Fact]
    public void Stylesheet_TopbarIsNearBlackWithoutABorder()
    {
        var css = Css();

        Assert.True(Brightness(Variable(css, "--topbar-bg")) < 25);
        var topbar = Regex.Match(css, @"header\.topbar\s*\{([^}]*)\}").Groups[1].Value;
        Assert.Contains("var(--topbar-bg)", topbar);
        Assert.DoesNotContain("border", topbar);
        Assert.DoesNotContain("gradient", topbar);
    }

    [Fact]
    public void Layout_NavLinksAreLeftAligned()
    {
        var layout = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Pages", "Shared", "_Layout.cshtml"));

        Assert.Matches(@"<ul[^>]*class=""[^""]*\bnavbar-nav\b[^""]*\bme-auto\b", layout);
        Assert.DoesNotContain("ms-auto", layout);
    }

    [Fact]
    public void Layout_ContentIsLeftAligned()
    {
        var layout = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Pages", "Shared", "_Layout.cshtml"));

        Assert.Matches(@"<main[^>]*class=""[^""]*\bcontainer-xl\b[^""]*\bms-0\b", layout);
        Assert.DoesNotContain("container-fluid\" py-4", layout);
        Assert.DoesNotContain("mx-auto", layout);
    }

    [Fact]
    public void Stylesheet_ContentShellHasNoBackgroundOrShadow()
    {
        var shell = Regex.Match(Css(), @"\.content-shell\s*\{([^}]*)\}").Groups[1].Value;

        Assert.DoesNotContain("background", shell);
        Assert.DoesNotContain("box-shadow", shell);
    }

    [Theory]
    [InlineData("Decks.cshtml")]
    [InlineData("People.cshtml")]
    public void Pages_TablesAreNotBordered(string page)
    {
        var html = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Pages", page));

        Assert.DoesNotContain("table-bordered", html);
    }
}

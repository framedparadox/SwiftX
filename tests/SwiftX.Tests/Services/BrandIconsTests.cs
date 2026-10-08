using SwiftX.Models;
using SwiftX.Services;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// Covers the GitHub logo the default link wears (<see cref="BrandIcons"/>) and the links the
/// first-run guide offers (<see cref="OnboardingCatalog"/>).
/// </summary>
public class BrandIconsTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "swiftx-brand-tests", Guid.NewGuid().ToString("N"));

    public BrandIconsTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch { /* ignore */ }
    }

    private static OrbitItem Link(string url, string? glyph = null, string? picture = null) => new()
    {
        Kind = OrbitItemKind.WebLink,
        DisplayName = url,
        Target = url,
        CustomGlyph = glyph,
        CustomIconPath = picture,
    };

    [Theory]
    [InlineData("https://github.com")]
    [InlineData("https://github.com/framedparadox/swiftx")]
    [InlineData("https://GitHub.com/")]
    [InlineData("https://www.github.com")]
    [InlineData("https://gist.github.com/someone")]
    public void A_link_to_GitHub_is_recognised(string url)
    {
        Assert.True(BrandIcons.IsGitHub(Link(url)));
    }

    [Theory]
    [InlineData("https://notgithub.com")]
    [InlineData("https://github.com.example.org")]
    [InlineData("https://example.com/github.com")]
    [InlineData("github.com")]
    [InlineData("")]
    public void Anything_else_is_not(string url)
    {
        Assert.False(BrandIcons.IsGitHub(Link(url)));
    }

    [Fact]
    public void Only_a_web_link_counts()
    {
        var folder = new OrbitItem { Kind = OrbitItemKind.Folder, Target = "https://github.com" };

        Assert.False(BrandIcons.IsGitHub(folder));
    }

    [Fact]
    public void The_logo_is_found_where_the_app_keeps_its_images()
    {
        var images = Path.Combine(_directory, "Images", "Brands");
        Directory.CreateDirectory(images);
        var logo = Path.Combine(images, "github.png");
        File.WriteAllBytes(logo, new byte[] { 1 });

        Assert.Equal(logo, BrandIcons.PathFor(Link("https://github.com"), _directory));
    }

    [Fact]
    public void No_logo_means_the_favicon_is_used_as_before()
    {
        // The file is not there, and a site that is not GitHub never had one.
        Assert.Null(BrandIcons.PathFor(Link("https://github.com"), _directory));
        Assert.Null(BrandIcons.PathFor(Link("https://example.com"), _directory));
    }

    [Fact]
    public void The_shipped_logo_is_in_the_source_tree()
    {
        var logo = Path.Combine(SourceTree.Root(), "Images", "Brands", "github.png");

        Assert.True(File.Exists(logo), logo);
        // A PNG, not a placeholder: the signature, and enough bytes to be a picture.
        var bytes = File.ReadAllBytes(logo);
        Assert.True(bytes.Length > 1000);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes.Take(4).ToArray());
    }

    [Fact]
    public void The_old_default_glyph_comes_off_a_GitHub_link()
    {
        var github = Link("https://github.com", BrandIcons.LegacyGitHubGlyph);
        var items = new List<OrbitItem> { github };

        Assert.True(BrandIcons.MigrateLegacyGitHubGlyph(items));

        Assert.Null(github.CustomGlyph);
    }

    [Fact]
    public void It_is_found_inside_a_group_too()
    {
        var github = Link("https://github.com", BrandIcons.LegacyGitHubGlyph);
        var group = new OrbitItem { Kind = OrbitItemKind.Group, Children = { github } };

        Assert.True(BrandIcons.MigrateLegacyGitHubGlyph(new[] { group }));

        Assert.Null(github.CustomGlyph);
    }

    [Fact]
    public void A_glyph_or_picture_the_user_chose_is_left_alone()
    {
        var ownGlyph = Link("https://github.com", "\uE774");
        var ownPicture = Link("https://github.com", BrandIcons.LegacyGitHubGlyph, @"C:\icons\mine.png");
        var elsewhere = Link("https://example.com", BrandIcons.LegacyGitHubGlyph);

        Assert.False(BrandIcons.MigrateLegacyGitHubGlyph(new[] { ownGlyph, ownPicture, elsewhere }));

        Assert.Equal("\uE774", ownGlyph.CustomGlyph);
        Assert.Equal(BrandIcons.LegacyGitHubGlyph, ownPicture.CustomGlyph);
        Assert.Equal(BrandIcons.LegacyGitHubGlyph, elsewhere.CustomGlyph);
    }

    [Fact]
    public void The_guide_offers_Google_instead_of_Stack_Overflow()
    {
        var links = OnboardingCatalog.Available()
            .Where(s => s.Item.Kind == OrbitItemKind.WebLink)
            .ToList();

        var google = Assert.Single(links, s => s.Key == "google");
        Assert.Equal("Google", google.Item.DisplayName);
        Assert.Equal("https://www.google.com", google.Item.Target);
        Assert.DoesNotContain(links, s => s.Item.Target.Contains("stackoverflow", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_guides_GitHub_carries_no_glyph_so_it_shows_the_logo()
    {
        var github = Assert.Single(
            OnboardingCatalog.Available(), s => s.Key == "github");

        Assert.Null(github.Item.CustomGlyph);
        Assert.True(github.DefaultOn);
        Assert.True(BrandIcons.IsGitHub(github.Item));
    }
}

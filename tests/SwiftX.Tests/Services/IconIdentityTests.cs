using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Services;

/// <summary>
/// The identity of a resolved icon — what makes one resolution reusable and another stale.
/// <para>
/// <c>RingWindow.RenderCurrent</c> fires an icon resolution per item on every render, and a
/// render happens on every summon, every theme change and every settings save. Resolution is not
/// cheap: an app or file goes through the shell's jumbo image list, <c>GetDIBits</c>, a PNG
/// encode and a XAML decode. Skipping it when the answer cannot have changed is what keeps a
/// summon instant, and <see cref="RingItem.IconKey"/> is the whole basis of that decision — so
/// the properties below are the ones a caching bug would violate.
/// </para>
/// <para>
/// This is a correctness test, not a performance one. The failure mode of a key that is too
/// coarse is a stale picture the user cannot get rid of; the failure mode of one that is too fine
/// is only lost work. The first is what the assertions here are aimed at.
/// </para>
/// </summary>
public class IconIdentityTests
{
    private static RingItem Item() => new()
    {
        Kind = RingItemKind.Application,
        DisplayName = "Notepad",
        Target = @"C:\Windows\notepad.exe",
    };

    /// <summary>Two items resolved from the same inputs share a key — the reuse case.</summary>
    [Fact]
    public void Items_with_identical_inputs_share_a_key()
    {
        Assert.Equal(Item().IconKey, Item().IconKey);
    }

    /// <summary>
    /// Every input <c>IconService.LoadIconAsync</c> reads changes the key. Each of these is a
    /// separate branch in that method — kind selects the resolution strategy, target is what it
    /// resolves, and the two custom fields short-circuit ahead of both — so a key missing any one
    /// of them would pin an icon that a user edit was supposed to replace.
    /// </summary>
    [Fact]
    public void Every_input_that_changes_the_resolution_changes_the_key()
    {
        var baseline = Item().IconKey;

        var differentKind = Item();
        differentKind.Kind = RingItemKind.Folder;
        Assert.NotEqual(baseline, differentKind.IconKey);

        var differentTarget = Item();
        differentTarget.Target = @"C:\Windows\write.exe";
        Assert.NotEqual(baseline, differentTarget.IconKey);

        var customIcon = Item();
        customIcon.CustomIconPath = @"C:\icons\mine.png";
        Assert.NotEqual(baseline, customIcon.IconKey);

        var customGlyph = Item();
        customGlyph.CustomGlyph = "\uE8A5";
        Assert.NotEqual(baseline, customGlyph.IconKey);
    }

    /// <summary>
    /// The display name is deliberately <em>not</em> part of the key. Renaming an item on the
    /// ring does not change what its icon resolves to, and re-extracting a shell icon because
    /// someone fixed a typo would give back the identical bitmap.
    /// </summary>
    [Fact]
    public void Renaming_an_item_does_not_invalidate_its_icon()
    {
        var renamed = Item();
        renamed.DisplayName = "Notepad (old)";
        Assert.Equal(Item().IconKey, renamed.IconKey);
    }

    /// <summary>
    /// Fields cannot bleed into each other. The key is a concatenation, so a separator that could
    /// occur inside a path would let two genuinely different items collide — an item targeting
    /// <c>a</c> with icon <c>b</c> reading as one targeting <c>a\u001fb</c> with no icon. U+001F
    /// is chosen because it cannot appear in a Windows path or a URL.
    /// </summary>
    [Fact]
    public void Fields_cannot_run_together_into_a_colliding_key()
    {
        var targetOnly = new RingItem { Kind = RingItemKind.File, Target = "a", CustomIconPath = "b" };
        var pathOnly = new RingItem { Kind = RingItemKind.File, Target = "a\u001fb" };

        Assert.NotEqual(targetOnly.IconKey, pathOnly.IconKey);
    }

    /// <summary>
    /// Setting the same image back is not a change. A render that finds the icon already correct
    /// still assigns it, and the setter raises three notifications — for the image and both
    /// visibility projections — each of which every bubble's handler then reacts to. Suppressing
    /// the no-op assignment is what keeps a re-render from doing that work for nothing.
    /// </summary>
    [Fact]
    public void Reassigning_the_same_icon_raises_no_change_notification()
    {
        var item = Item();
        int notifications = 0;
        item.PropertyChanged += (_, _) => notifications++;

        item.IconImage = null; // already null
        Assert.Equal(0, notifications);
    }

    /// <summary>
    /// Suppressing the no-op must not suppress a real one: the idempotence guard compares by
    /// reference, so it has to let a genuinely different value through. Asserted on
    /// <see cref="RingItem.CustomGlyph"/>, which carries the identical guard and — unlike
    /// <see cref="RingItem.IconImage"/> — needs no <c>ImageSource</c> to exercise it. Constructing
    /// one of those means activating a WinUI <c>DependencyObject</c>, which needs a XAML thread
    /// this test host does not have.
    /// </summary>
    [Fact]
    public void A_real_change_still_notifies_including_the_derived_properties()
    {
        var item = Item();
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.CustomGlyph = "";

        Assert.Contains(nameof(RingItem.CustomGlyph), changed);
        Assert.Contains(nameof(RingItem.Glyph), changed);
        Assert.Contains(nameof(RingItem.HasCustomIcon), changed);

        // And setting the same value again is silent, the same way the icon setter is.
        changed.Clear();
        item.CustomGlyph = "";
        Assert.Empty(changed);
    }

    /// <summary>
    /// The two visibility projections are exact opposites, and both are derived from the icon
    /// rather than stored. They are what swap a bubble between its bitmap and its glyph, so a
    /// state where neither or both are visible would leave a blank or a double-drawn item.
    /// </summary>
    [Fact]
    public void Image_and_glyph_visibility_are_always_opposites()
    {
        var item = Item();

        Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, item.ImageVisibility);
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, item.GlyphVisibility);
        Assert.NotEqual(item.ImageVisibility, item.GlyphVisibility);
    }

    /// <summary>
    /// The signature is scratch state, not configuration. Persisting it would put a meaningless
    /// resolution stamp in every user's <c>ring.json</c>, and — worse — a stamp read back from
    /// disk would claim an icon had already been resolved in a process that has resolved nothing.
    /// </summary>
    [Fact]
    public void The_icon_signature_is_never_written_to_the_configuration()
    {
        var item = Item();
        item.IconSignature = item.IconKey;

        var json = System.Text.Json.JsonSerializer.Serialize(item);

        Assert.DoesNotContain(nameof(RingItem.IconSignature), json, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(RingItem.IconKey), json, StringComparison.Ordinal);
    }
}

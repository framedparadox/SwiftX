namespace SwiftX.Models;

/// <summary>One ring: its items. Phase 1 uses a single primary ring.</summary>
public sealed class RingProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<RingItem> Items { get; set; } = new();
}

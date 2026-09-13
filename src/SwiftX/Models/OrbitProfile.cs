namespace SwiftX.Models;

/// <summary>One orbit: its items. Phase 1 uses a single primary orbit.</summary>
public sealed class OrbitProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<OrbitItem> Items { get; set; } = new();
}

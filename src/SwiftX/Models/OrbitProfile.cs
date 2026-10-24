namespace SwiftX.Models;

/// <summary>One orbit: its items. Phase 1 uses a single primary orbit.</summary>
public sealed class OrbitProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<OrbitItem> Items { get; set; } = new();

    /// <summary>
    /// Groups the user has made that are not currently placed on the ring: created while the ring was
    /// full, or taken off it without being thrown away. They live in the "ADD TO ORBIT" sidebar's
    /// Groups list alongside the ones on the ring, ready to be dragged back on. A group is in exactly
    /// one place at a time — here or in <see cref="Items"/>, never both.
    /// <para>
    /// New and defaulted empty, so an existing <c>orbit.json</c> with no such field loads unchanged:
    /// nothing is detached until the user removes a group from the layout or makes one with no room.
    /// </para>
    /// </summary>
    public List<OrbitItem> DetachedGroups { get; set; } = new();
}
